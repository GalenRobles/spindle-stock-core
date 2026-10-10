using System.Text.Json.Serialization;

namespace AlmacenTaller.Models.Events;

public class InspectionApprovedPayload
{
    // Propiedades directas que busca el compilador en Reservations.cs
    public Guid EventId { get; set; }
    public long WorkOrderId { get; set; }
    public long BomLineId { get; set; }
    public string? Sku { get; set; }
    public int RequiredQuantity { get; set; }

    // Propiedades estándar del evento inspection.approved
    [JsonPropertyName("inspection_id")]
    public long InspectionId { get; set; }

    [JsonPropertyName("work_order_id")]
    public long WorkOrderIdJson { get => WorkOrderId; set => WorkOrderId = value; }

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("approved_by")]
    public long ApprovedBy { get; set; }

    [JsonPropertyName("approved_at")]
    public DateTime ApprovedAt { get; set; }

    [JsonPropertyName("items")]
    public List<InspectionApprovedItem> Items { get; set; } = new();
}

public class InspectionApprovedItem
{
    [JsonPropertyName("inspection_item_id")]
    public long InspectionItemId { get; set; }

    [JsonPropertyName("bom_line_id")]
    public long? BomLineId { get; set; }

    [JsonPropertyName("part_id")]
    public long? PartId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("group_name")]
    public string? GroupName { get; set; }

    [JsonPropertyName("condition")]
    public string Condition { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("quantity")]
    public int? Quantity { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}