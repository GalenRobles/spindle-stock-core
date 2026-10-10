using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using AlmacenTaller.Messaging;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Dapper;
using Npgsql;

namespace AlmacenTaller.Services;

/// <summary>
/// Publica en inventory.events lo que otros servicios dejan en la tabla outbox_events
/// (patrón outbox: el evento se guarda en la misma transacción que el cambio de inventario).
///
/// Contrato de la fila de outbox_events:
///   event_id, event_type, event_version, occurred_at -> van tal cual al sobre
///   event_key -> la llave del evento (contract/README.md §2): código de la orden, "part:{id}",
///                "purchase_line:{id}"... según el tipo de evento
///   payload   -> SOLO el objeto "data" del evento (con los campos de su schema de salida)
///
/// El sobre completo lo arma este servicio:
///   { event_id, event_type, event_version, occurred_at (UTC, termina en Z), source: "inventory", key, data }
/// </summary>
public class KafkaProducerService : BackgroundService
{
    private readonly ILogger<KafkaProducerService> _logger;
    private readonly IConfiguration _configuration;

    public KafkaProducerService(ILogger<KafkaProducerService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        var bootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:19092";
        var connectionString = _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:DefaultConnection en appsettings.json");

        _logger.LogInformation("Iniciando Kafka Producer Service (outbox -> {Topic})...", KafkaTopics.InventoryEvents);

        try
        {
            await EnsureOutputTopicsAsync(bootstrapServers, stoppingToken);

            using var producer = new ProducerBuilder<string, string>(new ProducerConfig
            {
                BootstrapServers = bootstrapServers,
                Acks = Acks.All,
                EnableIdempotence = true
            }).Build();

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PublishPendingAsync(producer, connectionString, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error procesando la tabla outbox_events.");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
            }

            producer.Flush(TimeSpan.FromSeconds(10));
        }
        catch (OperationCanceledException)
        {
            // apagado normal
        }
    }

    private async Task PublishPendingAsync(
        IProducer<string, string> producer, string connectionString, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);

        // Del más antiguo al más nuevo, para conservar el orden en que se generaron.
        var rows = (await connection.QueryAsync<OutboxEventDto>(new CommandDefinition(
            @"SELECT outbox_id     AS OutboxId,
                     event_id      AS EventId,
                     event_type    AS EventType,
                     event_version AS EventVersion,
                     occurred_at   AS OccurredAt,
                     event_key     AS EventKey,
                     payload       AS Payload
              FROM outbox_events
              WHERE published = FALSE
              ORDER BY outbox_id ASC
              LIMIT 50",
            cancellationToken: ct))).ToList();

        foreach (var row in rows)
        {
            string value;
            try
            {
                value = BuildEnvelope(row);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "outbox_events {OutboxId} ({EventType}): payload inválido, se omite.",
                    row.OutboxId, row.EventType);
                continue;
            }

            try
            {
                // Todos los eventos stock.* van al MISMO tópico (inventory.events); el tipo viaja en el sobre.
                var delivery = await producer.ProduceAsync(
                    KafkaTopics.InventoryEvents,
                    new Message<string, string> { Key = row.EventKey, Value = value },
                    ct);

                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE outbox_events SET published = TRUE, published_at = NOW() WHERE outbox_id = @OutboxId",
                    new { row.OutboxId },
                    cancellationToken: ct));

                _logger.LogInformation("{EventType} {EventId} publicado en {Topic} [{Partition}@{Offset}]",
                    row.EventType, row.EventId, delivery.Topic, delivery.Partition.Value, delivery.Offset.Value);
            }
            catch (ProduceException<string, string> ex)
            {
                _logger.LogError("No se pudo entregar {EventType} {EventId} a Kafka: {Reason}",
                    row.EventType, row.EventId, ex.Error.Reason);
                // Se corta el ciclo para no saltarse el orden; se reintenta en la siguiente vuelta.
                break;
            }
        }
    }

    private static string BuildEnvelope(OutboxEventDto row)
    {
        var data = JsonNode.Parse(row.Payload);
        if (data is not JsonObject)
            throw new JsonException("El payload debe ser un objeto JSON (el \"data\" del evento).");

        var occurredAtUtc = row.OccurredAt.Kind == DateTimeKind.Local
            ? row.OccurredAt.ToUniversalTime()
            : DateTime.SpecifyKind(row.OccurredAt, DateTimeKind.Utc);

        var envelope = new JsonObject
        {
            ["event_id"] = row.EventId.ToString(),
            ["event_type"] = row.EventType,
            ["event_version"] = row.EventVersion,
            ["occurred_at"] = occurredAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["source"] = "inventory",
            ["key"] = row.EventKey,
            ["data"] = data
        };

        return envelope.ToJsonString();
    }

    /// <summary>Crea inventory.events e inventory.dlq si no existen (el simulador ya las crea con 3 particiones).</summary>
    private async Task EnsureOutputTopicsAsync(string bootstrapServers, CancellationToken ct)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers }).Build();

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await admin.CreateTopicsAsync(
                    new[]
                    {
                        new TopicSpecification { Name = KafkaTopics.InventoryEvents, NumPartitions = 3, ReplicationFactor = 1 },
                        new TopicSpecification { Name = KafkaTopics.InventoryDlq, NumPartitions = 3, ReplicationFactor = 1 }
                    },
                    new CreateTopicsOptions { RequestTimeout = TimeSpan.FromSeconds(10) });
                return;
            }
            catch (CreateTopicsException ex)
                when (ex.Results.All(r => r.Error.Code == ErrorCode.NoError || r.Error.Code == ErrorCode.TopicAlreadyExists))
            {
                return; // ya existían
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Esperando al broker para preparar los tópicos de salida: {Message}", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
    }
}

/// <summary>Fila de outbox_events tal como la lee el publicador.</summary>
public class OutboxEventDto
{
    public long OutboxId { get; set; }
    public Guid EventId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public int EventVersion { get; set; }
    public DateTime OccurredAt { get; set; }
    public string EventKey { get; set; } = string.Empty;
    public string Payload { get; set; } = "{}";
}
