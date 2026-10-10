using AlmacenTaller.Messaging;
using Confluent.Kafka;
using Confluent.Kafka.Admin;

namespace AlmacenTaller.Services;

/// <summary>
/// Consumidor de los tópicos del taller.
///
/// Orden de trabajo:
///  1. Espera a la base de datos y a los tópicos del taller.
///  2. Carga shop.catalog completo (regla 22). Además, ANTES de cada mensaje de otro tópico se
///     vuelve a vaciar el catálogo, porque no hay orden entre tópicos: una compra puede llegar
///     antes que el part.upserted que la hace coincidir.
///  3. Consume shop.work_orders, shop.inspections y shop.purchasing con commit MANUAL del offset:
///     solo se confirma después de procesar (o de mandar a pending / DLQ).
///
/// Por cada mensaje: parsea el sobre, deduplica por event_id (processed_events), busca el
/// IEventHandler del event_type, reintenta 3 veces y, si sigue fallando, lo manda a inventory.dlq.
/// </summary>
public partial class KafkaConsumerService : BackgroundService
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan CatalogStartupQuietWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CatalogMaxStall = TimeSpan.FromSeconds(15);

    private readonly ILogger<KafkaConsumerService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly EventStore _store;
    private readonly string _bootstrapServers;
    private readonly string _baseGroupId;
    private readonly bool _replay;

    private readonly HashSet<string> _typesWithoutHandler = new();
    private IProducer<string, string>? _dlqProducer;

    public KafkaConsumerService(
        ILogger<KafkaConsumerService> logger,
        IServiceProvider serviceProvider,
        EventStore store,
        IConfiguration configuration)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _store = store;
        _bootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:19092";
        _baseGroupId = configuration["Kafka:GroupId"] ?? "roceel-inventory-service";
        _replay = configuration.GetValue<bool>("Kafka:Replay");
    }

    private IProducer<string, string> DlqProducer => _dlqProducer ??= new ProducerBuilder<string, string>(
        new ProducerConfig
        {
            BootstrapServers = _bootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true
        }).Build();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunPipelineAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Lo que no se confirmó (commit) se vuelve a leer al reiniciar: no se pierde nada.
                _logger.LogError(ex, "El consumidor falló; se reinicia en 5 s.");
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        if (_dlqProducer != null)
        {
            _dlqProducer.Flush(TimeSpan.FromSeconds(5));
            _dlqProducer.Dispose();
        }
    }

    private async Task RunPipelineAsync(CancellationToken ct)
    {
        // 1. Base lista. Si está vacía (primera vez o la borraron) se relee todo desde el inicio
        //    con un grupo nuevo: así "borrar la base y releer" reconstruye el mismo saldo.
        var processed = await WaitForDatabaseAsync(ct);
        var groupId = _baseGroupId;
        if (processed == 0 || _replay)
        {
            groupId = $"{_baseGroupId}-{DateTime.UtcNow:yyyyMMddHHmmss}";
            _logger.LogInformation("Releyendo los tópicos desde el inicio (grupo {GroupId}).", groupId);
        }

        // 2. Tópicos del taller creados por el simulador.
        var catalogPartitions = await WaitForTopicsAsync(ct);

        // 3. Catálogo completo antes de nada más.
        using var catalog = BuildCatalogConsumer(catalogPartitions);
        await DrainCatalogAsync(catalog, catalogPartitions, CatalogStartupQuietWindow, ct);
        _logger.LogInformation("Catálogo cargado. Procesando {Topics}.", string.Join(", ", KafkaTopics.ShopOperational));

        // 4. Resto de los tópicos, offset confirmado a mano.
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = _bootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            AllowAutoCreateTopics = false
        }).Build();
        consumer.Subscribe(KafkaTopics.ShopOperational);

        while (!ct.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result;
            try
            {
                result = consumer.Consume(ct);
            }
            catch (ConsumeException ex)
            {
                _logger.LogWarning("Error al consumir de Kafka: {Reason}", ex.Error.Reason);
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
                continue;
            }

            if (result?.Message == null) continue;

            // No hay orden entre tópicos: se vacía el catálogo antes de cada mensaje.
            await DrainCatalogAsync(catalog, catalogPartitions, null, ct);

            await ProcessMessageAsync(result, ct);
            CommitSafe(consumer, result);
        }

        consumer.Close();
    }

    // ---------------------------------------------------------------------------------
    // Procesamiento de un mensaje
    // ---------------------------------------------------------------------------------

    private async Task ProcessMessageAsync(ConsumeResult<string, string> msg, CancellationToken ct)
    {
        var raw = msg.Message.Value;
        if (raw == null) return;

        if (!EventEnvelope.TryParse(raw, out var envelope, out var parseError) || envelope == null)
        {
            _logger.LogError("Mensaje ilegible en {Topic} [{Partition}@{Offset}]: {Error}",
                msg.Topic, msg.Partition.Value, msg.Offset.Value, parseError);
            await SendToDlqAsync(msg, null, parseError ?? "mensaje ilegible", 0, ct);
            return;
        }

        // Idempotencia: el mismo event_id no cambia nada ni emite salidas nuevas.
        if (await _store.IsHandledAsync(envelope.EventId, ct))
        {
            _logger.LogDebug("Evento duplicado {EventId} ({EventType}); se ignora.", envelope.EventId, envelope.EventType);
            return;
        }

        Exception? lastError = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            // Un scope nuevo por intento: un DbContext que ya falló no se reutiliza.
            using var scope = _serviceProvider.CreateScope();
            var handlers = scope.ServiceProvider.GetServices<IEventHandler>()
                .Where(h => h.EventTypes.Contains(envelope.EventType))
                .ToList();

            if (handlers.Count == 0)
            {
                // Sin handler todavía: no se marca como procesado, así al implementarlo y releer se procesa.
                if (_typesWithoutHandler.Add(envelope.EventType))
                    _logger.LogInformation("Sin handler para {EventType}: se omite hasta que se implemente.", envelope.EventType);
                return;
            }

            try
            {
                foreach (var handler in handlers)
                    await handler.HandleAsync(envelope, ct);

                await _store.MarkAsync(envelope, "processed", ct);
                return;
            }
            catch (MissingDependencyException ex)
            {
                // Regla 20: lo que referencia algo que aún no llega no se descarta.
                _logger.LogWarning("{EventType} {EventId} espera a {Entity}: {Message}",
                    envelope.EventType, envelope.EventId, ex.Entity, ex.Message);
                await _store.SavePendingAsync(envelope, ex.Entity, ct);
                await _store.MarkAsync(envelope, "pending", ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                _logger.LogWarning(ex, "Intento {Attempt}/{Max} falló para {EventType} {EventId}.",
                    attempt, MaxAttempts, envelope.EventType, envelope.EventId);
                if (attempt < MaxAttempts)
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), ct);
            }
        }

        // Regla 21: después de reintentar va a inventory.dlq, sin detener el consumo.
        await SendToDlqAsync(msg, envelope, lastError?.Message ?? "error desconocido", MaxAttempts, ct);
    }

    private async Task SendToDlqAsync(
        ConsumeResult<string, string> msg, EventEnvelope? envelope, string error, int attempts, CancellationToken ct)
    {
        // La llave de la DLQ es la misma del original.
        await DlqProducer.ProduceAsync(
            KafkaTopics.InventoryDlq,
            new Message<string, string> { Key = msg.Message.Key ?? envelope?.Key ?? string.Empty, Value = msg.Message.Value },
            ct);

        if (envelope != null)
        {
            await _store.SaveDeadLetterAsync(envelope, error, attempts, ct);
            await _store.MarkAsync(envelope, "failed", ct);
        }

        _logger.LogError("Evento enviado a {Dlq}: {EventType} {EventId}. Error: {Error}",
            KafkaTopics.InventoryDlq, envelope?.EventType, envelope?.EventId, error);
    }

    private void CommitSafe(IConsumer<string, string> consumer, ConsumeResult<string, string> result)
    {
        try
        {
            consumer.Commit(result);
        }
        catch (KafkaException ex)
        {
            _logger.LogWarning("No se pudo confirmar el offset {Offset}: {Reason}", result.Offset.Value, ex.Error.Reason);
        }
    }

    // ---------------------------------------------------------------------------------
    // Catálogo primero
    // ---------------------------------------------------------------------------------

    private IConsumer<string, string> BuildCatalogConsumer(IReadOnlyList<int> partitions)
    {
        var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = _bootstrapServers,
            GroupId = $"{_baseGroupId}-catalog",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            AllowAutoCreateTopics = false
        }).Build();

        // Siempre desde el inicio: los upserts del catálogo son idempotentes y processed_events
        // descarta los que ya se aplicaron.
        consumer.Assign(partitions.Select(p => new TopicPartitionOffset(KafkaTopics.ShopCatalog, p, Offset.Beginning)));
        return consumer;
    }

    /// <summary>
    /// Procesa shop.catalog hasta alcanzar el final de todas sus particiones.
    /// quietWindow != null: además espera ese tiempo sin mensajes nuevos (arranque, cuando el
    /// simulador puede seguir publicando la carga inicial).
    /// </summary>
    private async Task DrainCatalogAsync(
        IConsumer<string, string> catalog, IReadOnlyList<int> partitions, TimeSpan? quietWindow, CancellationToken ct)
    {
        var lastActivity = DateTime.UtcNow;

        while (!ct.IsCancellationRequested)
        {
            var caughtUp = IsCaughtUp(catalog, partitions);
            if (caughtUp && (quietWindow is null || DateTime.UtcNow - lastActivity >= quietWindow.Value))
                return;

            try
            {
                var result = catalog.Consume(TimeSpan.FromMilliseconds(250));
                if (result != null)
                {
                    if (result.Message?.Value != null)
                        await ProcessMessageAsync(result, ct);
                    lastActivity = DateTime.UtcNow;
                    continue;
                }
            }
            catch (ConsumeException ex)
            {
                _logger.LogWarning("Error leyendo {Topic}: {Reason}", KafkaTopics.ShopCatalog, ex.Error.Reason);
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }

            // Si no hay quietWindow y el catálogo no avanza, no se bloquea todo el servicio para siempre.
            if (!caughtUp && quietWindow is null && DateTime.UtcNow - lastActivity > CatalogMaxStall)
            {
                _logger.LogWarning("El catálogo no avanza desde hace {Seconds} s; se continúa sin esperar más.",
                    CatalogMaxStall.TotalSeconds);
                return;
            }
        }
    }

    private bool IsCaughtUp(IConsumer<string, string> catalog, IReadOnlyList<int> partitions)
    {
        try
        {
            foreach (var p in partitions)
            {
                var tp = new TopicPartition(KafkaTopics.ShopCatalog, p);
                var watermarks = catalog.QueryWatermarkOffsets(tp, TimeSpan.FromSeconds(5));
                var position = catalog.Position(tp);

                // High = siguiente offset a escribir; Position = siguiente offset a leer.
                if (watermarks.High.Value > 0 && position.Value < watermarks.High.Value)
                    return false;
            }
            return true;
        }
        catch (KafkaException ex)
        {
            _logger.LogWarning("No se pudo consultar el final de {Topic}: {Reason}", KafkaTopics.ShopCatalog, ex.Error.Reason);
            return false;
        }
    }

    // ---------------------------------------------------------------------------------
    // Esperas de arranque
    // ---------------------------------------------------------------------------------

    private async Task<long> WaitForDatabaseAsync(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await _store.CountProcessedAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Base de datos no lista (¿se aplicó init.sql?): {Message}", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }
    }

    /// <summary>Espera a que existan los 4 tópicos de entrada y devuelve las particiones de shop.catalog.</summary>
    private async Task<IReadOnlyList<int>> WaitForTopicsAsync(CancellationToken ct)
    {
        var required = new[] { KafkaTopics.ShopCatalog }.Concat(KafkaTopics.ShopOperational).ToArray();

        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = _bootstrapServers }).Build();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var meta = admin.GetMetadata(TimeSpan.FromSeconds(5));
                var existingTopics = meta.Topics.Where(t => !t.Error.IsError).Select(t => t.Topic).ToHashSet();

                if (required.All(r => existingTopics.Contains(r)))
                {
                    var catalogTopic = meta.Topics.First(t => t.Topic == KafkaTopics.ShopCatalog);
                    var partitionCount = catalogTopic.Partitions.Count;
                    var partitions = Enumerable.Range(0, partitionCount).ToList().AsReadOnly();
                    _logger.LogInformation("Tópicos encontrados. shop.catalog tiene {Partitions} particiones.", partitionCount);
                    return partitions;
                }

                var missing = required.Where(r => !existingTopics.Contains(r)).ToArray();
                _logger.LogInformation("Esperando tópicos de Kafka: faltan {Missing}. Reintentando en 2 s.", string.Join(", ", missing));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error consultando Kafka: {Message}", ex.Message);
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        return Array.Empty<int>();
    }
}