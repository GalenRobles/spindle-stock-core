using AlmacenTaller.DataContext;
using AlmacenTaller.Messaging;
using AlmacenTaller.Models;

namespace AlmacenTaller.Services.Handlers;

public class WorkOrderOpenedHandler : IEventHandler
{
    private readonly AppDbContext _db;

    public WorkOrderOpenedHandler(AppDbContext db)
    {
        _db = db;
    }

    public IReadOnlyCollection<string> EventTypes =>
        new[] { "work_order.opened" };

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        var data = envelope.Data;
        var id = data.GetProperty("work_order_id").GetInt64();

        var order = await _db.WorkOrders.FindAsync(
            new object[] { id }, ct);

        if (order == null)
        {
            order = new WorkOrder
            {
                WorkOrderId = id,
                Stage = "awaiting_quick_inspection",
                CreatedAt = DateTime.UtcNow
            };

            _db.WorkOrders.Add(order);
        }

        order.Code = data.GetProperty("code").GetString()!;
        order.ModelId = data.GetProperty("model_id").GetInt64();
        order.Department = data.GetProperty("department").GetString()!;
        order.ReceivedAt = data.GetProperty("received_at")
            .GetDateTimeOffset().UtcDateTime;
        order.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
    }
}