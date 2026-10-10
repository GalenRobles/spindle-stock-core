using System.Text.Json;
using AlmacenTaller.DataContext;
using AlmacenTaller.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlmacenTaller.Services.Handlers;

public class InspectionVoidedHandler : IEventHandler
{
    private readonly AppDbContext _db;
    private readonly ILogger<InspectionVoidedHandler> _logger;

    public IReadOnlyCollection<string> EventTypes => new[] { "inspection.voided", "inspection_voided" };

    public InspectionVoidedHandler(AppDbContext db, ILogger<InspectionVoidedHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        var root = envelope.Data;
        long inspectionId = root.TryGetProperty("inspection_id", out var inspId) ? inspId.GetInt64() : 0;
        long workOrderId = root.TryGetProperty("work_order_id", out var woId) ? woId.GetInt64() : 0;

        if (inspectionId <= 0)
        {
            throw new InvalidOperationException("inspection.voided sin inspection_id válido.");
        }

        var inspection = await _db.Inspections.SingleOrDefaultAsync(
            candidate => candidate.InspectionId == inspectionId, ct);
        if (inspection is null)
        {
            throw new MissingDependencyException("Inspection",
                $"La inspección {inspectionId} aún no existe.");
        }

        if (workOrderId > 0 && inspection.WorkOrderId != workOrderId)
        {
            throw new InvalidOperationException("La inspección no pertenece a la orden indicada.");
        }

        var needs = await _db.WorkOrderNeeds
            .Where(need => need.WorkOrderId == inspection.WorkOrderId
                && need.InspectionId == inspectionId
                && need.Status == "active")
            .ToListAsync(ct);
        var lineIds = needs.Select(need => need.BomLineId).ToArray();

        var reservations = await _db.Reservations
            .Where(reservation => reservation.WorkOrderId == inspection.WorkOrderId
                && lineIds.Contains(reservation.BomLineId)
                && reservation.Status == "active")
            .ToListAsync(ct);
        foreach (var reservation in reservations)
        {
            reservation.Status = "cancelled";
            reservation.CancelledAt = DateTime.UtcNow;
            reservation.UpdatedAt = DateTime.UtcNow;

            var balance = await _db.InventoryBalances
                .FirstOrDefaultAsync(candidate => candidate.PartId == reservation.PartId
                    && candidate.LocationId == reservation.LocationId, ct);
            if (balance is not null)
            {
                balance.Reserved = Math.Max(0, balance.Reserved - reservation.Quantity);
                balance.UpdatedAt = DateTime.UtcNow;
            }
        }

        var shortages = await _db.Shortages
            .Where(shortage => shortage.WorkOrderId == inspection.WorkOrderId
                && lineIds.Contains(shortage.BomLineId)
                && shortage.Status == "open")
            .ToListAsync(ct);

        foreach (var shortage in shortages)
        {
            shortage.Status = "closed";
            shortage.ResolvedAt = DateTime.UtcNow;
        }

        foreach (var need in needs)
        {
            need.Status = "closed";
            need.UpdatedAt = DateTime.UtcNow;
        }

        inspection.Status = "voided";
        inspection.VoidedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Inspección {InspectionId} anulada. Reservas liberadas.", inspectionId);
    }
}