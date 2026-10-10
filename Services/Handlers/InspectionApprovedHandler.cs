using System.Text.Json;
using AlmacenTaller.DataContext;
using AlmacenTaller.Messaging;
using AlmacenTaller.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlmacenTaller.Services.Handlers;

public class InspectionApprovedHandler : IEventHandler
{
    private readonly AppDbContext _db;
    private readonly ILogger<InspectionApprovedHandler> _logger;

    public IReadOnlyCollection<string> EventTypes => new[] { "inspection.approved", "inspection_approved" };

    public InspectionApprovedHandler(AppDbContext db, ILogger<InspectionApprovedHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("--> [INSPECTION APPROVED] Recibido evento ID: {EventId}", envelope.EventId);

            var root = envelope.Data;

            long workOrderId = root.TryGetProperty("work_order_id", out var woId) ? woId.GetInt64() : 0;
            long inspectionId = root.TryGetProperty("inspection_id", out var inspId) ? inspId.GetInt64() : 0;

            var items = new List<JsonElement>();
            if (root.TryGetProperty("items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in itemsElement.EnumerateArray()) items.Add(item);
            }
            else
            {
                items.Add(root);
            }

            foreach (var item in items)
            {
                string sku = item.TryGetProperty("sku", out var s) ? s.GetString() ?? "" : "";
                long partIdParam = item.TryGetProperty("part_id", out var pi) && pi.ValueKind == JsonValueKind.Number ? pi.GetInt64() : 0;

                long partId = 0;

                if (!string.IsNullOrWhiteSpace(sku))
                {
                    string cleanSku = sku.Trim().ToUpperInvariant();
                    // Buscamos usando EF.Property para evitar problemas de nombres de propiedades
                    var foundPart = await _db.Parts.FirstOrDefaultAsync(p =>
                        EF.Property<string>(p, "SkuNorm") == cleanSku ||
                        EF.Property<string>(p, "Sku") == sku.Trim(), ct);

                    if (foundPart != null)
                    {
                        partId = EF.Property<long>(foundPart, "PartId");
                    }
                }
                else if (partIdParam > 0)
                {
                    partId = partIdParam;
                }

                if (partId == 0)
                {
                    _logger.LogWarning("Pieza no encontrada en catálogo durante InspectionApproved. SKU: {Sku}, PartId: {PartId}", sku, partIdParam);
                    throw new MissingDependencyException("Part", $"La pieza no se encuentra en el catálogo.");
                }

                long bomLineId = item.TryGetProperty("bom_line_id", out var bl) ? bl.GetInt64() : 0;
                long inspectionItemId = item.TryGetProperty("inspection_item_id", out var ii) ? ii.GetInt64() : 0;

                int qty = 0;
                if (item.TryGetProperty("quantity", out var q) && q.ValueKind == JsonValueKind.Number) qty = q.GetInt32();
                else if (item.TryGetProperty("required_quantity", out var rq) && rq.ValueKind == JsonValueKind.Number) qty = rq.GetInt32();
                else qty = 1;

                long locationId = 100;

                var sql = "SELECT * FROM inventory_balances WHERE part_id = {0} AND location_id = {1} FOR UPDATE";
                var balance = await _db.InventoryBalances
                                   .FromSqlRaw(sql, partId, locationId)
                                   .SingleOrDefaultAsync(ct);

                if (balance == null)
                {
                    balance = new InventoryBalance { PartId = partId, LocationId = locationId, OnHand = 0, Reserved = 0 };
                    _db.InventoryBalances.Add(balance);
                }

                int availableQty = Math.Max(0, balance.OnHand - balance.Reserved);
                int toReserve = Math.Min(availableQty, qty);
                int shortageQty = qty - toReserve;

                if (toReserve > 0)
                {
                    balance.Reserved += toReserve;
                    balance.UpdatedAt = DateTime.UtcNow;

                    _db.Reservations.Add(new Reservation
                    {
                        WorkOrderId = workOrderId,
                        BomLineId = bomLineId,
                        PartId = partId,
                        LocationId = locationId,
                        InspectionId = inspectionId,
                        InspectionItemId = inspectionItemId,
                        Quantity = toReserve,
                        Status = "active",
                        CreatedAt = DateTime.UtcNow
                    });

                    var reservedPayload = new { work_order_id = workOrderId, inspection_id = inspectionId, bom_line_id = bomLineId, part_id = partId, location_id = locationId, quantity = toReserve };

                    _db.OutboxEvents.Add(new OutboxEvent
                    {
                        EventId = Guid.NewGuid(),
                        EventType = "stock.reserved",
                        EventKey = $"part:{partId}",
                        Payload = JsonSerializer.Serialize(reservedPayload),
                        EventVersion = 1,
                        OccurredAt = DateTime.UtcNow,
                        Published = false
                    });
                }

                if (shortageQty > 0)
                {
                    _db.Shortages.Add(new Shortage
                    {
                        WorkOrderId = workOrderId,
                        BomLineId = bomLineId,
                        PartId = partId,
                        MissingQuantity = shortageQty,
                        CoveredQuantity = 0,
                        Status = "open",
                        CreatedAt = DateTime.UtcNow
                    });

                    var shortagePayload = new { work_order_id = workOrderId, bom_line_id = bomLineId, part_id = partId, missing_quantity = shortageQty };

                    _db.OutboxEvents.Add(new OutboxEvent
                    {
                        EventId = Guid.NewGuid(),
                        EventType = "stock.shortage_detected",
                        EventKey = $"part:{partId}",
                        Payload = JsonSerializer.Serialize(shortagePayload),
                        EventVersion = 1,
                        OccurredAt = DateTime.UtcNow,
                        Published = false
                    });
                }

                _logger.LogInformation("Inspección procesada exitosamente para Part {PartId}. Reserved: {Res}, Shortage: {Short}", partId, toReserve, shortageQty);
            }

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ERROR CRÍTICO procesando InspectionApprovedHandler para el evento {EventId}", envelope.EventId);
            throw;
        }
    }
}