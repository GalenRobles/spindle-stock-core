using System.Text.Json.Serialization;

namespace AlmacenTaller.Models;

public class PartEventWrapper
{
    [JsonPropertyName("event_id")]
    public string EventId { get; set; } = string.Empty;

    [JsonPropertyName("event_type")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("occurred_at")]
    public DateTime OccurredAt { get; set; }

    [JsonPropertyName("data")]
    public PartPayload Data { get; set; } = new();
}

public class PartPayload
{
    [JsonPropertyName("part_id")]
    public int PartId { get; set; }

    [JsonPropertyName("sku")]
    public string? Sku { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("family")]
    public string? Family { get; set; }

    [JsonPropertyName("group")]
    public string? Group { get; set; }

    [JsonPropertyName("subgroup")]
    public string? Subgroup { get; set; }

    [JsonPropertyName("unit")]
    public UnitPayload? Unit { get; set; }

    [JsonPropertyName("active")]
    public bool Active { get; set; }
}

public class UnitPayload
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}