using System.Text.Json;
using System.Text.Json.Serialization;
using AlmacenTaller.DataContext;
using AlmacenTaller.Messaging;
using AlmacenTaller.Models;
using Microsoft.EntityFrameworkCore;

namespace AlmacenTaller.Services.Handlers;

public class InspectionSubmittedHandler : IEventHandler
{
    private readonly AppDbContext _db;
    private readonly ILogger<InspectionSubmittedHandler> _logger;

    public InspectionSubmittedHandler(AppDbContext db, ILogger<InspectionSubmittedHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public IReadOnlyCollection<string> EventTypes => new[] { "inspection.submitted", "inspection_submitted" };

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        var data = envelope.Data.Deserialize<InspectionPayload>();
        if (data is null || data.InspectionId <= 0 || data.WorkOrderId <= 0
            || data.Kind is not ("quick" or "full") || data.Items is null)
        {
            throw new InvalidOperationException("inspection.submitted tiene datos inválidos.");
        }

        if (!await _db.WorkOrders.AnyAsync(order => order.WorkOrderId == data.WorkOrderId, ct))
        {
            throw new MissingDependencyException("WorkOrder",
                $"La orden {data.WorkOrderId} de la inspección aún no existe.");
        }

        if (data.Items.Any(item => item.InspectionItemId <= 0 || item.BomLineId <= 0
            || string.IsNullOrWhiteSpace(item.Name)
            || item.Condition is not ("ok" or "damaged" or "missing")))
        {
            throw new InvalidOperationException("inspection.submitted contiene renglones inválidos.");
        }

        var itemIds = data.Items.Select(item => item.InspectionItemId).ToArray();
        if (itemIds.Distinct().Count() != itemIds.Length)
        {
            throw new InvalidOperationException("inspection.submitted contiene inspection_item_id duplicados.");
        }

        var occurredAt = envelope.OccurredAt.UtcDateTime;
        var inspection = await _db.Inspections.SingleOrDefaultAsync(
            candidate => candidate.InspectionId == data.InspectionId, ct);
        if (inspection is null)
        {
            inspection = new Inspection
            {
                InspectionId = data.InspectionId,
                WorkOrderId = data.WorkOrderId,
                Kind = data.Kind,
                Status = "submitted",
                OccurredAt = occurredAt,
                CreatedAt = occurredAt
            };
            _db.Inspections.Add(inspection);
        }
        else
        {
            if (inspection.WorkOrderId != data.WorkOrderId)
            {
                throw new InvalidOperationException("La inspección ya está asociada a otra orden.");
            }

            inspection.Kind = data.Kind;
        }

        var existingItems = await _db.InspectionItems
            .Where(item => item.InspectionId == data.InspectionId || itemIds.Contains(item.InspectionItemId))
            .ToListAsync(ct);
        var itemsById = existingItems.ToDictionary(item => item.InspectionItemId);

        foreach (var itemData in data.Items)
        {
            if (itemsById.TryGetValue(itemData.InspectionItemId, out var item))
            {
                if (item.InspectionId != data.InspectionId)
                {
                    throw new InvalidOperationException(
                        $"El renglón {itemData.InspectionItemId} ya pertenece a otra inspección.");
                }
            }
            else
            {
                item = new InspectionItem
                {
                    InspectionItemId = itemData.InspectionItemId,
                    InspectionId = data.InspectionId,
                    CreatedAt = occurredAt
                };
                _db.InspectionItems.Add(item);
            }

            item.BomLineId = itemData.BomLineId;
            item.PartId = itemData.PartId;
            item.Name = itemData.Name;
            item.GroupName = itemData.GroupName;
            item.Condition = itemData.Condition;
            item.Action = itemData.Action;
            item.Quantity = itemData.Quantity;
            item.Note = itemData.Note;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Inspección {InspectionId} registrada con {ItemCount} renglones.",
            data.InspectionId, data.Items.Count);
    }

    private sealed class InspectionPayload
    {
        [JsonPropertyName("inspection_id")]
        public long InspectionId { get; init; }

        [JsonPropertyName("work_order_id")]
        public long WorkOrderId { get; init; }

        [JsonPropertyName("kind")]
        public string Kind { get; init; } = string.Empty;

        [JsonPropertyName("items")]
        public List<InspectionItemPayload>? Items { get; init; }
    }

    private sealed class InspectionItemPayload
    {
        [JsonPropertyName("inspection_item_id")]
        public long InspectionItemId { get; init; }

        [JsonPropertyName("bom_line_id")]
        public long BomLineId { get; init; }

        [JsonPropertyName("part_id")]
        public long? PartId { get; init; }

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("group_name")]
        public string? GroupName { get; init; }

        [JsonPropertyName("condition")]
        public string Condition { get; init; } = string.Empty;

        [JsonPropertyName("action")]
        public string? Action { get; init; }

        [JsonPropertyName("quantity")]
        public int? Quantity { get; init; }

        [JsonPropertyName("note")]
        public string? Note { get; init; }
    }
}