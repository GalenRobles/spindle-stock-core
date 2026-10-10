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

        // 1. Buscar las reservas activas ligadas a esta inspección o orden de trabajo y liberarlas
        var reservations = await _db.Reservations
            .Where(r => (inspectionId != 0 && r.InspectionId == inspectionId) || (workOrderId != 0 && r.WorkOrderId == workOrderId))
            .Where(r => r.Status == "active")
            .ToListAsync(ct);

        foreach (var res in reservations)
        {
            res.Status = "voided";

            // Reintegrar la cantidad reservada al balance general de inventario
            var balance = await _db.InventoryBalances
                .FirstOrDefaultAsync(b => b.PartId == res.PartId && b.LocationId == res.LocationId, ct);

            if (balance != null)
            {
                balance.Reserved = Math.Max(0, balance.Reserved - res.Quantity);
                balance.UpdatedAt = DateTime.UtcNow;
            }
        }

        // 2. Cerrar o actualizar faltantes abiertos de esta orden
        var shortages = await _db.Shortages
            .Where(s => s.WorkOrderId == workOrderId && s.Status == "open")
            .ToListAsync(ct);

        foreach (var s in shortages)
        {
            s.Status = "voided";
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Inspección {InspectionId} anulada. Reservas liberadas.", inspectionId);
    }
}