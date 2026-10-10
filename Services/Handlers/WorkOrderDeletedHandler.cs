using System.Text.Json;
using AlmacenTaller.DataContext;
using AlmacenTaller.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlmacenTaller.Services.Handlers;

public class WorkOrderDeletedHandler : IEventHandler
{
    private readonly AppDbContext _db;
    private readonly ILogger<WorkOrderDeletedHandler> _logger;

    public IReadOnlyCollection<string> EventTypes => new[] { "work_order.deleted", "work_order_deleted" };

    public WorkOrderDeletedHandler(AppDbContext db, ILogger<WorkOrderDeletedHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        var root = envelope.Data;
        long workOrderId = root.TryGetProperty("work_order_id", out var woId) ? woId.GetInt64() : 0;

        if (workOrderId == 0) return;

        // 1. Liberar reservas activas de la orden de trabajo
        var reservations = await _db.Reservations
            .Where(r => r.WorkOrderId == workOrderId && r.Status == "active")
            .ToListAsync(ct);

        foreach (var res in reservations)
        {
            res.Status = "cancelled";
            res.CancelledAt = DateTime.UtcNow;
            res.UpdatedAt = DateTime.UtcNow;

            var balance = await _db.InventoryBalances
                .FirstOrDefaultAsync(b => b.PartId == res.PartId && b.LocationId == res.LocationId, ct);

            if (balance != null)
            {
                balance.Reserved = Math.Max(0, balance.Reserved - res.Quantity);
                balance.UpdatedAt = DateTime.UtcNow;
            }
        }

        // 2. Cerrar faltantes
        var shortages = await _db.Shortages
            .Where(s => s.WorkOrderId == workOrderId && s.Status == "open")
            .ToListAsync(ct);

        foreach (var s in shortages)
        {
            s.Status = "closed";
            s.ResolvedAt = DateTime.UtcNow;
        }

        var needs = await _db.WorkOrderNeeds
            .Where(need => need.WorkOrderId == workOrderId && need.Status == "active")
            .ToListAsync(ct);
        foreach (var need in needs)
        {
            need.Status = "closed";
            need.UpdatedAt = DateTime.UtcNow;
        }

        var order = await _db.WorkOrders.SingleOrDefaultAsync(
            workOrder => workOrder.WorkOrderId == workOrderId, ct);
        if (order is not null)
        {
            order.IsDeleted = true;
            order.DeletedAt = DateTime.UtcNow;
            order.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Orden de trabajo {WorkOrderId} dada de baja. Reservas y faltantes limpiados.", workOrderId);
    }
}