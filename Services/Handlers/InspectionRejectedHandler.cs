using AlmacenTaller.DataContext;
using AlmacenTaller.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlmacenTaller.Services.Handlers;

public class InspectionRejectedHandler : IEventHandler
{
    private readonly AppDbContext _db;
    private readonly ILogger<InspectionRejectedHandler> _logger;

    public IReadOnlyCollection<string> EventTypes => new[] { "inspection.rejected", "inspection_rejected" };

    public InspectionRejectedHandler(AppDbContext db, ILogger<InspectionRejectedHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        var data = envelope.Data;
        if (!data.TryGetProperty("inspection_id", out var inspectionElement)
            || !inspectionElement.TryGetInt64(out var inspectionId) || inspectionId <= 0)
        {
            throw new InvalidOperationException("inspection.rejected sin inspection_id válido.");
        }

        var inspection = await _db.Inspections.SingleOrDefaultAsync(
            candidate => candidate.InspectionId == inspectionId, ct);
        if (inspection is null)
        {
            throw new MissingDependencyException("Inspection",
                $"La inspección {inspectionId} aún no existe.");
        }

        inspection.Status = "rejected";
        inspection.Reason = data.TryGetProperty("reason", out var reason)
            ? reason.GetString()
            : null;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Inspección {InspectionId} rechazada; no se generan reservas.", inspectionId);
    }
}