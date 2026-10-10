using AlmacenTaller.DataContext;
using AlmacenTaller.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AlmacenTaller.Endpoints;

public static class WarehouseEndpoints
{
    public static void MapWarehouseEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/work-orders/{code}/materials", async (string code, AppDbContext db) =>
        {
            var order = await db.WorkOrders
                .SingleOrDefaultAsync(workOrder => workOrder.Code == code);
            if (order is null)
            {
                return Results.NotFound();
            }

            var lines = await db.VWorkOrderMaterials
                .Where(material => material.WorkOrderId == order.WorkOrderId
                    && material.NeedStatus == "active")
                .Select(material => new
                {
                    inspection_item_id = material.InspectionItemId,
                    bom_line_id = material.BomLineId,
                    part_id = material.PartId,
                    name = material.Name,
                    required = material.RequiredQty,
                    reserved = material.ReservedQty ?? 0,
                    issued = material.DeliveredQty ?? 0,
                    missing = material.ShortageUnknown == true
                        ? null
                        : material.ShortageQty
                })
                .ToListAsync();

            return Results.Ok(new { work_order_id = order.WorkOrderId, code = order.Code, lines });
        });

        app.MapGet("/shortages", async (string? work_order_code, long? part_id, AppDbContext db) =>
        {
            var query = db.Shortages
                .Where(shortage => shortage.Status == "open")
                .Join(db.WorkOrders,
                    shortage => shortage.WorkOrderId,
                    order => order.WorkOrderId,
                    (shortage, order) => new { Shortage = shortage, Order = order })
                .Where(row => !row.Order.IsDeleted);

            if (!string.IsNullOrWhiteSpace(work_order_code))
            {
                query = query.Where(row => row.Order.Code == work_order_code);
            }

            if (part_id.HasValue)
            {
                query = query.Where(row => row.Shortage.PartId == part_id.Value);
            }

            var items = await query
                .OrderBy(row => row.Shortage.CreatedAt)
                .Select(row => new
                {
                    work_order_id = row.Order.WorkOrderId,
                    work_order_code = row.Order.Code,
                    part_id = row.Shortage.PartId,
                    name = row.Shortage.Name,
                    missing_quantity = row.Shortage.MissingQuantity.HasValue
                        ? (int?)(row.Shortage.MissingQuantity.Value - row.Shortage.CoveredQuantity)
                        : null,
                    inspection_item_id = row.Shortage.InspectionItemId,
                    opened_at = row.Shortage.CreatedAt
                })
                .ToListAsync();

            return Results.Ok(new { items });
        });

        app.MapGet("/reorder-suggestions", async (AppDbContext db) =>
        {
            var items = await db.ReorderSuggestions
                .Where(suggestion => suggestion.Status == "active")
                .Select(suggestion => new
                {
                    suggestion.PartId,
                    suggestion.Part.Sku,
                    suggestion.Part.Name,
                    suggestion.SuggestedQuantity,
                    WorkOrderIds = suggestion.WorkOrders
                        .Where(order => !order.IsDeleted)
                        .Select(order => order.WorkOrderId)
                        .ToArray()
                })
                .ToListAsync();

            return Results.Ok(new
            {
                items = items.Select(item => new
                {
                    part_id = item.PartId,
                    sku = item.Sku,
                    name = item.Name,
                    suggested_quantity = item.SuggestedQuantity,
                    work_order_ids = item.WorkOrderIds
                })
            });
        });

        // 1. Disponibilidad de piezas
        app.MapGet("/parts/{partId:int}/availability",
        async (int partId, AppDbContext db) =>
        {
            var balances = await db.InventoryBalances
                .Where(b => b.PartId == partId)
                .Select(b => new
                {
                    location_id = b.LocationId,
                    on_hand = b.OnHand,
                    reserved = b.Reserved,
                    available = b.OnHand - b.Reserved
                })
                .ToListAsync();

            var onHand = balances.Sum(b => b.on_hand);
            var reserved = balances.Sum(b => b.reserved);

            return Results.Ok(new
            {
                on_hand = onHand,
                reserved = reserved,
                available = onHand - reserved,
                locations = balances
            });
        });

        // 2. Salida de material (Issues)
        app.MapPost("/issues", async (IssueRequest req, AppDbContext db) =>
        {
            var order = await db.WorkOrders
                .FirstOrDefaultAsync(w => w.Code == req.work_order_code);

            if (order == null || order.IsDeleted)
            {
                return Results.NotFound();
            }

            if (req.quantity <= 0)
            {
                return Results.UnprocessableEntity();
            }

            await using var transaction = await db.Database.BeginTransactionAsync();
            var balance = await db.InventoryBalances
                .FromSqlInterpolated(
                    $"SELECT * FROM inventory_balances WHERE part_id = {req.part_id} AND location_id = {req.location_id} FOR UPDATE")
                .SingleOrDefaultAsync();

            if (balance == null || balance.OnHand < req.quantity)
            {
                return Results.StatusCode(409);
            }

            var reservations = await db.Reservations
                .Where(reservation => reservation.WorkOrderId == order.WorkOrderId
                    && reservation.PartId == req.part_id
                    && reservation.LocationId == req.location_id
                    && reservation.Status == "active")
                .OrderBy(reservation => reservation.CreatedAt)
                .ToListAsync();

            var reservedForOrder = reservations.Sum(
                reservation => Math.Max(0, reservation.Quantity - reservation.FulfilledQty));
            if (req.quantity > balance.OnHand - balance.Reserved + reservedForOrder)
            {
                return Results.StatusCode(409);
            }

            var now = DateTime.UtcNow;
            balance.OnHand -= req.quantity;
            var remainingIssue = req.quantity;
            foreach (var reservation in reservations)
            {
                if (remainingIssue == 0)
                {
                    break;
                }

                var reservationRemaining = reservation.Quantity - reservation.FulfilledQty;
                var fulfilled = Math.Min(remainingIssue, reservationRemaining);
                if (fulfilled <= 0)
                {
                    continue;
                }

                reservation.FulfilledQty += fulfilled;
                reservation.UpdatedAt = now;
                balance.Reserved = Math.Max(0, balance.Reserved - fulfilled);
                remainingIssue -= fulfilled;

                if (reservation.FulfilledQty >= reservation.Quantity)
                {
                    reservation.Status = "fulfilled";
                }
            }
            balance.UpdatedAt = now;

            var movement = new StockMovement
            {
                PartId = req.part_id,
                LocationId = req.location_id,
                MovementType = "issue",
                Quantity = -req.quantity,
                ReferenceId = req.work_order_code,
                CreatedAt = now
            };
            db.StockMovements.Add(movement);

            var payload = new
            {
                work_order_id = order.WorkOrderId,
                work_order_code = req.work_order_code,
                part_id = req.part_id,
                location_id = req.location_id,
                quantity = req.quantity,
                issued_by = req.issued_by
            };

            db.OutboxEvents.Add(new OutboxEvent
            {
                EventId = Guid.NewGuid(),
                EventType = "stock.issued",
                EventVersion = 1,
                OccurredAt = now,
                EventKey = req.work_order_code,
                Payload = JsonSerializer.Serialize(payload),
                Published = false,
                CreatedAt = now
            });

            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            return Results.Created($"/issues/{movement.MovementId}", new
            {
                work_order_id = order.WorkOrderId,
                work_order_code = req.work_order_code,
                part_id = req.part_id,
                location_id = req.location_id,
                quantity = req.quantity,
                issued_by = req.issued_by
            });
        });

        // 3. Kardex / Libro Mayor
        app.MapGet("/parts/{partId:int}/ledger", async (int partId, AppDbContext db) =>
        {
            var movements = await db.StockMovements
                .Where(m => m.PartId == partId)
                .OrderBy(m => m.CreatedAt)
                .ThenBy(m => m.MovementId)
                .ToListAsync();

            var entries = new List<object>();
            long currentBalance = 0;

            foreach (var m in movements)
            {
                currentBalance += m.Quantity;

                entries.Add(new
                {
                    occurred_at = m.CreatedAt,
                    type = m.MovementType,
                    quantity = m.Quantity,
                    balance = currentBalance
                });
            }

            entries.Reverse();

            return Results.Ok(new { entries });
        });

        // 4. Recepciones sin relacionar
        app.MapGet("/unmatched-receipts", async (AppDbContext db) =>
        {
            var unmatched = await db.UnmatchedReceipts
                .Where(u => u.Status == "open")
                .Join(db.PurchaseReceipts,
                    unmatchedReceipt => unmatchedReceipt.LineId,
                    receipt => receipt.LineId,
                    (unmatchedReceipt, receipt) => new
                {
                    id = unmatchedReceipt.UnmatchedReceiptId,
                    purchase_line_id = receipt.LineId,
                    part_number = receipt.PartNumber,
                    description = receipt.Description,
                    quantity = receipt.Quantity,
                    unit_price = receipt.UnitPrice.ToString(),
                    currency = receipt.Currency,
                    received_at = receipt.ReceivedAt,
                    work_order_code = receipt.WorkOrderCode
                })
                .ToListAsync();

            return Results.Ok(new { items = unmatched });
        });

        app.MapPost("/unmatched-receipts/{id:long}/resolve",
            async (long id, ResolveUnmatchedReceiptRequest request, AppDbContext db) =>
            {
                var unmatched = await db.UnmatchedReceipts
                    .Include(receipt => receipt.Line)
                    .SingleOrDefaultAsync(receipt => receipt.UnmatchedReceiptId == id);
                if (unmatched is null)
                {
                    return Results.NotFound();
                }

                if (unmatched.Status != "open")
                {
                    return Results.Conflict();
                }

                var part = await db.Parts.SingleOrDefaultAsync(candidate => candidate.PartId == request.part_id);
                if (part is null)
                {
                    return Results.NotFound();
                }

                var locationId = request.location_id ?? 100;
                if (!await db.Locations.AnyAsync(location => location.LocationId == locationId))
                {
                    return Results.NotFound();
                }

                await using var transaction = await db.Database.BeginTransactionAsync();
                var balance = await db.InventoryBalances
                    .FromSqlInterpolated(
                        $"SELECT * FROM inventory_balances WHERE part_id = {part.PartId} AND location_id = {locationId} FOR UPDATE")
                    .SingleOrDefaultAsync();
                if (balance is null)
                {
                    balance = new InventoryBalance
                    {
                        PartId = part.PartId,
                        LocationId = locationId,
                        OnHand = 0,
                        Reserved = 0,
                        UpdatedAt = DateTime.UtcNow
                    };
                    db.InventoryBalances.Add(balance);
                }

                var receipt = unmatched.Line;
                var occurredAt = receipt.ReceivedAt;
                balance.OnHand += receipt.Quantity;
                balance.UpdatedAt = occurredAt;
                receipt.MatchedPartId = part.PartId;
                receipt.LocationId = locationId;
                receipt.Status = "resolved";
                unmatched.ResolvedPartId = part.PartId;
                unmatched.Status = "resolved";
                unmatched.ResolvedAt = occurredAt;

                db.StockMovements.Add(new StockMovement
                {
                    EventId = Guid.NewGuid().ToString(),
                    PartId = checked((int)part.PartId),
                    LocationId = checked((int)locationId),
                    Quantity = receipt.Quantity,
                    UnitCost = receipt.UnitPrice,
                    MovementType = "receipt",
                    ReferenceId = receipt.LineId.ToString(),
                    CreatedAt = occurredAt
                });

                db.OutboxEvents.Add(new OutboxEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = "stock.received",
                    EventVersion = 1,
                    OccurredAt = occurredAt,
                    EventKey = $"part:{part.PartId}",
                    Payload = JsonSerializer.Serialize(new
                    {
                        part_id = part.PartId,
                        location_id = locationId,
                        quantity = receipt.Quantity,
                        unit_cost = receipt.UnitPrice.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        currency = receipt.Currency,
                        purchase_line_id = receipt.LineId
                    }),
                    Published = false,
                    CreatedAt = occurredAt
                });

                await db.SaveChangesAsync();
                await transaction.CommitAsync();

                return Results.Ok(new
                {
                    part_id = part.PartId,
                    location_id = locationId,
                    quantity = receipt.Quantity,
                    unit_cost = receipt.UnitPrice.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    currency = receipt.Currency,
                    purchase_line_id = receipt.LineId
                });
            });
    }
}

public record IssueRequest(
    string work_order_code,
    int part_id,
    int location_id,
    int quantity,
    JsonElement issued_by);

public record ResolveUnmatchedReceiptRequest(long part_id, long? location_id);