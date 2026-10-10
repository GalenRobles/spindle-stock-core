using System.Text.Json;

namespace AlmacenTaller.Messaging;

/// <summary>
/// Sobre común de todos los eventos de entrada (contract/README.md §1).
/// El tipo de evento (event_type) manda; el tópico solo agrupa varios tipos.
/// </summary>
public sealed class EventEnvelope
{
    public Guid EventId { get; init; }
    public string EventType { get; init; } = string.Empty;
    public int EventVersion { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public string Source { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;

    /// <summary>El objeto "data" del evento (ya clonado, se puede usar sin conservar el JsonDocument).</summary>
    public JsonElement Data { get; init; }

    /// <summary>El mensaje original tal cual llegó (para DLQ y pending_events).</summary>
    public string Raw { get; init; } = string.Empty;

    public static bool TryParse(string raw, out EventEnvelope? envelope, out string? error)
    {
        envelope = null;
        error = null;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "El mensaje no es un objeto JSON.";
                return false;
            }

            if (!root.TryGetProperty("event_id", out var idEl)
                || idEl.ValueKind != JsonValueKind.String
                || !idEl.TryGetGuid(out var eventId))
            {
                error = "Falta event_id o no es un UUID.";
                return false;
            }

            if (!root.TryGetProperty("event_type", out var typeEl)
                || typeEl.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(typeEl.GetString()))
            {
                error = "Falta event_type.";
                return false;
            }

            if (!root.TryGetProperty("occurred_at", out var atEl)
                || atEl.ValueKind != JsonValueKind.String
                || !atEl.TryGetDateTimeOffset(out var occurredAt))
            {
                error = "Falta occurred_at o no es una fecha ISO 8601.";
                return false;
            }

            if (!root.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Object)
            {
                error = "Falta data o no es un objeto.";
                return false;
            }

            var version = 1;
            if (root.TryGetProperty("event_version", out var vEl)
                && vEl.ValueKind == JsonValueKind.Number
                && vEl.TryGetInt32(out var v))
            {
                version = v;
            }

            envelope = new EventEnvelope
            {
                EventId = eventId,
                EventType = typeEl.GetString()!,
                EventVersion = version,
                OccurredAt = occurredAt,
                Source = root.TryGetProperty("source", out var sEl) && sEl.ValueKind == JsonValueKind.String
                    ? sEl.GetString() ?? string.Empty
                    : string.Empty,
                Key = root.TryGetProperty("key", out var kEl) && kEl.ValueKind == JsonValueKind.String
                    ? kEl.GetString() ?? string.Empty
                    : string.Empty,
                Data = dataEl.Clone(),
                Raw = raw
            };
            return true;
        }
        catch (JsonException ex)
        {
            error = $"JSON inválido: {ex.Message}";
            return false;
        }
    }
}
