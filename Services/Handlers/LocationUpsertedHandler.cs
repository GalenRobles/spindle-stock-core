using AlmacenTaller.Messaging;
using Dapper;

namespace AlmacenTaller.Services.Handlers;

/// <summary>
/// location.upserted (shop.catalog). Se necesita antes que cualquier recepción:
/// las entradas van a la ubicación U-100 (location_id 100) y inventory_balances tiene FK a locations.
/// Usa SQL directo con ON CONFLICT, así que es idempotente y no depende del modelo Location de EF.
/// </summary>
public class LocationUpsertedHandler : IEventHandler
{
    private readonly EventStore _store;
    private readonly ILogger<LocationUpsertedHandler> _logger;

    public LocationUpsertedHandler(EventStore store, ILogger<LocationUpsertedHandler> logger)
    {
        _store = store;
        _logger = logger;
    }

    public IReadOnlyCollection<string> EventTypes { get; } = new[] { "location.upserted" };

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        var d = envelope.Data;
        var locationId = d.GetProperty("location_id").GetInt64();
        var code = d.GetProperty("code").GetString() ?? throw new InvalidOperationException("location.upserted sin code.");
        var name = d.GetProperty("name").GetString() ?? string.Empty;
        var isWorkbench = d.GetProperty("is_workbench").GetBoolean();
        var active = d.GetProperty("active").GetBoolean();

        await using var connection = await _store.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO locations (location_id, code, name, is_workbench, active)
              VALUES (@locationId, @code, @name, @isWorkbench, @active)
              ON CONFLICT (location_id)
              DO UPDATE SET code = EXCLUDED.code,
                            name = EXCLUDED.name,
                            is_workbench = EXCLUDED.is_workbench,
                            active = EXCLUDED.active",
            new { locationId, code, name, isWorkbench, active },
            cancellationToken: ct));

        _logger.LogInformation("Ubicación registrada: {Code} (ID: {LocationId})", code, locationId);
    }
}
