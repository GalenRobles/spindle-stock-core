using System.Text.Json;
using System.Text.Json.Serialization;
using AlmacenTaller.DataContext;
using AlmacenTaller.Messaging;
using AlmacenTaller.Models;
using Microsoft.EntityFrameworkCore;

namespace AlmacenTaller.Services.Handlers;

/// <summary>bom.upserted (shop.catalog): sincroniza un modelo y su lista completa de materiales.</summary>
public class BomUpsertedHandler : IEventHandler
{
    private readonly AppDbContext _db;
    private readonly ILogger<BomUpsertedHandler> _logger;

    public BomUpsertedHandler(AppDbContext db, ILogger<BomUpsertedHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public IReadOnlyCollection<string> EventTypes { get; } = new[] { "bom.upserted" };

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        var data = envelope.Data.Deserialize<BomPayload>();
        if (data is null || data.ModelId <= 0 || string.IsNullOrWhiteSpace(data.ModelName)
            || string.IsNullOrWhiteSpace(data.Brand) || data.Lines is null)
        {
            throw new InvalidOperationException("bom.upserted tiene un modelo o una lista de líneas inválidos.");
        }

        if (data.Lines.Any(line => line.BomLineId <= 0 || string.IsNullOrWhiteSpace(line.Name)))
        {
            throw new InvalidOperationException("bom.upserted contiene una línea sin bom_line_id o name válido.");
        }

        var lineIds = data.Lines.Select(line => line.BomLineId).ToArray();
        if (lineIds.Distinct().Count() != lineIds.Length)
        {
            throw new InvalidOperationException("bom.upserted contiene bom_line_id duplicados.");
        }

        var updatedAt = envelope.OccurredAt.UtcDateTime;
        var model = await _db.EquipmentModels
            .SingleOrDefaultAsync(candidate => candidate.ModelId == data.ModelId, ct);

        if (model is null)
        {
            model = new EquipmentModel
            {
                ModelId = data.ModelId,
                CreatedAt = updatedAt
            };
            _db.EquipmentModels.Add(model);
        }

        model.Name = data.ModelName;
        model.Brand = data.Brand;
        model.UpdatedAt = updatedAt;

        var existingLines = await _db.BomLines
            .Where(line => lineIds.Contains(line.BomLineId)
                || (line.ModelId == data.ModelId && line.RemovedAt == null))
            .ToListAsync(ct);
        var linesById = existingLines.ToDictionary(line => line.BomLineId);
        var incomingIds = lineIds.ToHashSet();

        foreach (var lineData in data.Lines)
        {
            if (!linesById.TryGetValue(lineData.BomLineId, out var line))
            {
                line = new BomLine { BomLineId = lineData.BomLineId };
                _db.BomLines.Add(line);
            }

            line.ModelId = data.ModelId;
            line.PartId = lineData.PartId;
            line.Name = lineData.Name;
            line.GroupName = lineData.GroupName;
            line.UnitId = lineData.Unit?.Id;
            line.UnitName = lineData.Unit?.Name;
            line.QtyPerUnit = lineData.QuantityPerUnit;
            line.SortOrder = lineData.SortOrder;
            line.RemovedAt = null;
            line.UpdatedAt = updatedAt;
        }

        foreach (var removedLine in existingLines.Where(line =>
                     line.ModelId == data.ModelId && !incomingIds.Contains(line.BomLineId)))
        {
            removedLine.RemovedAt = updatedAt;
            removedLine.UpdatedAt = updatedAt;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Lista de materiales registrada para el modelo {ModelId}: {LineCount} líneas.",
            data.ModelId,
            data.Lines.Count);
    }

    private sealed class BomPayload
    {
        [JsonPropertyName("model_id")]
        public long ModelId { get; init; }

        [JsonPropertyName("model_name")]
        public string ModelName { get; init; } = string.Empty;

        [JsonPropertyName("brand")]
        public string Brand { get; init; } = string.Empty;

        [JsonPropertyName("lines")]
        public List<BomLinePayload>? Lines { get; init; }
    }

    private sealed class BomLinePayload
    {
        [JsonPropertyName("bom_line_id")]
        public long BomLineId { get; init; }

        [JsonPropertyName("part_id")]
        public long? PartId { get; init; }

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("group_name")]
        public string? GroupName { get; init; }

        [JsonPropertyName("unit")]
        public UnitPayload? Unit { get; init; }

        [JsonPropertyName("qty_per_unit")]
        public int? QuantityPerUnit { get; init; }

        [JsonPropertyName("sort_order")]
        public int SortOrder { get; init; }
    }

    private sealed class UnitPayload
    {
        [JsonPropertyName("id")]
        public long? Id { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }
    }
}
