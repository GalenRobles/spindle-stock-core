using System.Text.Json;
using AlmacenTaller.DataContext;
using AlmacenTaller.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlmacenTaller.Services.Handlers;

public class InspectionSubmittedHandler : IEventHandler
{
    private readonly AppDbContext _db;
    private readonly ILogger<InspectionSubmittedHandler> _logger;

    public IReadOnlyCollection<string> EventTypes => new[] { "inspection.submitted", "inspection_submitted" };

    public InspectionSubmittedHandler(AppDbContext db, ILogger<InspectionSubmittedHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        try
        {
            var root = envelope.Data;
            long inspectionId = root.TryGetProperty("inspection_id", out var inspId) ? inspId.GetInt64() : 0;
            long workOrderId = root.TryGetProperty("work_order_id", out var woId) ? woId.GetInt64() : 0;
            string kind = root.TryGetProperty("kind", out var kindElem) ? kindElem.GetString() ?? "quick" : "quick";

            DateTime occurredAt = DateTime.UtcNow;
            if (root.TryGetProperty("occurred_at", out var occElem))
            {
                if (occElem.TryGetDateTime(out var dt))
                {
                    occurredAt = dt;
                }
                else if (occElem.TryGetDateTimeOffset(out var dto))
                {
                    occurredAt = dto.UtcDateTime;
                }
            }
            else if (envelope.OccurredAt != default)
            {
                occurredAt = envelope.OccurredAt.UtcDateTime;
            }

            if (inspectionId > 0)
            {
                // Incluimos todas las columnas obligatorias para evitar violaciones NOT NULL
                await _db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO inspections (inspection_id, work_order_id, kind, status, created_at, occurred_at) VALUES ({0}, {1}, {2}, {3}, {4}, {5}) ON CONFLICT DO NOTHING",
                    new object[] { inspectionId, workOrderId, kind, "submitted", DateTime.UtcNow, occurredAt }, ct);
            }

            _logger.LogInformation("Inspección {InspectionId} registrada localmente con tipo {Kind}.", inspectionId, kind);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registrando inspection.submitted");
            throw;
        }
    }
}