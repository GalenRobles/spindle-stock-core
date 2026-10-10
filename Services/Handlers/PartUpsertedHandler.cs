using System.Text.Json;
using AlmacenTaller.DataContext;
using AlmacenTaller.Messaging;
using AlmacenTaller.Models;
using Microsoft.EntityFrameworkCore;

namespace AlmacenTaller.Services.Handlers;

/// <summary>part.upserted (shop.catalog): alta o edición de una pieza. Es un upsert, por lo tanto idempotente.</summary>
public class PartUpsertedHandler : IEventHandler
{
    private readonly AppDbContext _db;
    private readonly ILogger<PartUpsertedHandler> _logger;

    public PartUpsertedHandler(AppDbContext db, ILogger<PartUpsertedHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public IReadOnlyCollection<string> EventTypes { get; } = new[] { "part.upserted" };

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        var data = envelope.Data.Deserialize<PartPayload>();
        if (data is null || data.PartId <= 0)
            throw new InvalidOperationException("part.upserted sin part_id válido.");

        var existing = await _db.Parts.FirstOrDefaultAsync(p => p.PartId == data.PartId, ct);

        if (existing is null)
        {
            _db.Parts.Add(new Parts
            {
                PartId = data.PartId,
                Sku = data.Sku,
                Name = data.Name,
                Family = data.Family,
                PartGroup = data.Group,
                Subgroup = data.Subgroup,
                UnitName = data.Unit?.Name ?? string.Empty,
                Active = data.Active
            });
            _logger.LogInformation("Pieza insertada: {Sku} (ID: {PartId})", data.Sku, data.PartId);
        }
        else
        {
            existing.Sku = data.Sku;
            existing.Name = data.Name;
            existing.Family = data.Family;
            existing.PartGroup = data.Group;
            existing.Subgroup = data.Subgroup;
            existing.UnitName = data.Unit?.Name ?? string.Empty;
            existing.Active = data.Active;
            _logger.LogInformation("Pieza actualizada: {Sku} (ID: {PartId})", data.Sku, data.PartId);
        }

        await _db.SaveChangesAsync(ct);
    }
}
