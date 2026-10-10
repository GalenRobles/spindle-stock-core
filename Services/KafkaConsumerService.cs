using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using AlmacenTaller.DataContext;
using AlmacenTaller.Models;

namespace AlmacenTaller.Services;

public class KafkaConsumerService : BackgroundService
{
    private readonly ILogger<KafkaConsumerService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConsumerConfig _consumerConfig;

    public KafkaConsumerService(ILogger<KafkaConsumerService> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;

        _consumerConfig = new ConsumerConfig
        {
            BootstrapServers = "localhost:19092",
            GroupId = "almacen-consumidor-v2",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        using var consumer = new ConsumerBuilder<string, string>(_consumerConfig).Build();
        consumer.Subscribe(new[] { "part.upserted", "location.upserted", "stock.movement_recorded" });

        _logger.LogInformation("Escuchando eventos de Redpanda en localhost:19092...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(stoppingToken);
                if (result?.Message?.Value == null) continue;

                if (result.Topic == "part.upserted")
                {
                    await ProcessPartUpsertedAsync(result.Message.Value);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al consumir mensaje");
            }
        }

        consumer.Close();
    }

    private async Task ProcessPartUpsertedAsync(string json)
    {
        var wrapper = JsonSerializer.Deserialize<PartEventWrapper>(json);
        if (wrapper?.Data == null || wrapper.Data.PartId <= 0) return;

        var partData = wrapper.Data;

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existing = await db.Parts.FirstOrDefaultAsync(p => p.PartId == partData.PartId);

        if (existing == null)
        {
            db.Parts.Add(new Part
            {
                PartId = partData.PartId,
                Sku = partData.Sku,
                Name = partData.Name,
                Family = partData.Family,
                PartGroup = partData.Group,
                Subgroup = partData.Subgroup,
                Active = partData.Active
            });
            _logger.LogInformation("Pieza insertada: {Sku} (ID: {PartId})", partData.Sku, partData.PartId);
        }
        else
        {
            existing.Sku = partData.Sku;
            existing.Name = partData.Name;
            existing.Family = partData.Family;
            existing.PartGroup = partData.Group;
            existing.Subgroup = partData.Subgroup;
            existing.Active = partData.Active;
            _logger.LogInformation("Pieza actualizada: {Sku} (ID: {PartId})", partData.Sku, partData.PartId);
        }

        await db.SaveChangesAsync();
    }
}