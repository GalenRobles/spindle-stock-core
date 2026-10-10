using AlmacenTaller.Messaging;
using Dapper;
using Npgsql;

namespace AlmacenTaller.Services;

/// <summary>
/// Acceso directo (SQL) a las tablas de control: processed_events, pending_events y dead_letter_events.
/// Se usa SQL directo para no depender de los modelos de EF mientras se terminan.
/// </summary>
public class EventStore
{
    private readonly string _connectionString;

    public EventStore(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:DefaultConnection en appsettings.json");
    }

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct = default)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    public async Task<long> CountProcessedAsync(CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        return await connection.ExecuteScalarAsync<long>(
            new CommandDefinition("SELECT COUNT(*) FROM processed_events", cancellationToken: ct));
    }

    /// <summary>true si el evento ya se procesó (o se ignoró a propósito): no hay que tocarlo otra vez.</summary>
    public async Task<bool> IsHandledAsync(Guid eventId, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            @"SELECT EXISTS (SELECT 1 FROM processed_events
                             WHERE event_id = @eventId AND result IN ('processed', 'ignored'))",
            new { eventId },
            cancellationToken: ct));
    }

    /// <summary>result: processed | ignored | pending | failed</summary>
    public async Task MarkAsync(EventEnvelope envelope, string result, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO processed_events (event_id, event_type, occurred_at, result)
              VALUES (@eventId, @eventType, @occurredAt, @result)
              ON CONFLICT (event_id)
              DO UPDATE SET result = EXCLUDED.result, processed_at = NOW()",
            new
            {
                eventId = envelope.EventId,
                eventType = envelope.EventType,
                occurredAt = envelope.OccurredAt.UtcDateTime,
                result
            },
            cancellationToken: ct));
    }

    public async Task SavePendingAsync(EventEnvelope envelope, string missingEntity, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO pending_events (event_id, event_type, event_key, payload, missing_entity, next_retry_at)
              VALUES (@eventId, @eventType, @eventKey, @payload::jsonb, @missingEntity, NOW() + INTERVAL '2 seconds')
              ON CONFLICT (event_id) DO NOTHING",
            new
            {
                eventId = envelope.EventId,
                eventType = envelope.EventType,
                eventKey = envelope.Key,
                payload = envelope.Raw,
                missingEntity
            },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<PendingEventRecord>> GetDuePendingAsync(CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        var dueEvents = await connection.QueryAsync<PendingEventRecord>(new CommandDefinition(
            @"WITH due AS (
                  SELECT pending_id
                    FROM pending_events
                   WHERE status = 'pending' AND next_retry_at <= NOW()
                   ORDER BY next_retry_at, pending_id
                   LIMIT 50
                   FOR UPDATE SKIP LOCKED
              )
              UPDATE pending_events AS pending
                 SET attempts = pending.attempts + 1,
                     next_retry_at = NOW() + INTERVAL '2 seconds'
                FROM due
               WHERE pending.pending_id = due.pending_id
              RETURNING pending.event_id AS EventId, pending.payload::text AS Payload",
            cancellationToken: ct));
        return dueEvents.AsList();
    }

    public async Task ReschedulePendingAsync(Guid eventId, string missingEntity, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE pending_events
                 SET missing_entity = @missingEntity,
                     next_retry_at = NOW() + INTERVAL '2 seconds'
               WHERE event_id = @eventId AND status = 'pending'",
            new { eventId, missingEntity },
            cancellationToken: ct));
    }

    public async Task CompletePendingAsync(Guid eventId, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE pending_events
                 SET status = 'processed', processed_at = NOW()
               WHERE event_id = @eventId AND status = 'pending'",
            new { eventId },
            cancellationToken: ct));
    }

    public async Task FailPendingAsync(Guid eventId, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE pending_events
                 SET status = 'failed', processed_at = NOW()
               WHERE event_id = @eventId AND status = 'pending'",
            new { eventId },
            cancellationToken: ct));
    }

    public async Task SaveDeadLetterAsync(EventEnvelope envelope, string error, int attempts, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO dead_letter_events (event_id, event_type, event_key, payload, error_message, attempts)
              VALUES (@eventId, @eventType, @eventKey, @payload::jsonb, @error, @attempts)
              ON CONFLICT (event_id) DO NOTHING",
            new
            {
                eventId = envelope.EventId,
                eventType = envelope.EventType,
                eventKey = envelope.Key,
                payload = envelope.Raw,
                error,
                attempts
            },
            cancellationToken: ct));
    }
}

public sealed class PendingEventRecord
{
    public Guid EventId { get; set; }
    public string Payload { get; set; } = string.Empty;
}
