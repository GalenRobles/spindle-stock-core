using System.Text.Json;
using System.Text.Json.Serialization;
using AlmacenTaller.DataContext;
using AlmacenTaller.Messaging;
using AlmacenTaller.Models;

namespace AlmacenTaller.Services.Handlers;

public class WorkOrderOpenedHandler : IEventHandler
{
    private readonly AppDbContext _db;
    private readonly ILogger<WorkOrderOpenedHandler> _logger;

    public WorkOrderOpenedHandler(AppDbContext db, ILogger<WorkOrderOpenedHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public IReadOnlyCollection<string> EventTypes { get; } = new[] { "work_order.opened" };

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        var data = envelope.Data.Deserialize<WorkOrderOpenedPayload>();
        if (data is null || data.WorkOrderId <= 0 || data.ModelId <= 0
            || string.IsNullOrWhiteSpace(data.Code) || string.IsNullOrWhiteSpace(data.Department)
            || data.ReceivedAt == default)
        {
            throw new InvalidOperationException("work_order.opened tiene datos de orden inválidos.");
        }

        var order = await _db.WorkOrders.FindAsync(new object[] { data.WorkOrderId }, ct);
        var openedAt = envelope.OccurredAt.UtcDateTime;

        if (order == null)
        {
            order = new WorkOrder
            {
                WorkOrderId = data.WorkOrderId,
                Stage = "awaiting_quick_inspection",
                CreatedAt = openedAt
            };

            _db.WorkOrders.Add(order);
        }

        order.Code = data.Code;
        order.ModelId = data.ModelId;
        order.Department = data.Department;
        order.ReceivedAt = data.ReceivedAt.UtcDateTime;
        order.UpdatedAt = openedAt;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Orden de trabajo registrada: {Code} (ID: {WorkOrderId})", data.Code, data.WorkOrderId);
    }

    private sealed class WorkOrderOpenedPayload
    {
        [JsonPropertyName("work_order_id")]
        public long WorkOrderId { get; init; }

        [JsonPropertyName("code")]
        public string Code { get; init; } = string.Empty;

        [JsonPropertyName("model_id")]
        public long ModelId { get; init; }

        [JsonPropertyName("department")]
        public string Department { get; init; } = string.Empty;

        [JsonPropertyName("received_at")]
        public DateTimeOffset ReceivedAt { get; init; }
    }
}