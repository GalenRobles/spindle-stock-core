using System.Text.Json;
using System.Text.Json.Serialization;
using AlmacenTaller.DataContext;
using AlmacenTaller.Messaging;
using AlmacenTaller.Models;
using Microsoft.EntityFrameworkCore;

namespace AlmacenTaller.Services.Handlers;

public class InspectionApprovedHandler : IEventHandler
{
    private const long ReceivingLocationId = 100;

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
        var data = envelope.Data.Deserialize<InspectionApprovedPayload>();
        if (data is null || data.InspectionId <= 0 || data.WorkOrderId <= 0
            || data.Kind is not ("quick" or "full") || data.Items is null
            || data.ApprovedBy <= 0 || data.ApprovedAt == default)
        {
            throw new InvalidOperationException("inspection.approved tiene datos inválidos.");
        }

        if (data.Items.Any(item => item.InspectionItemId <= 0 || item.BomLineId <= 0
            || string.IsNullOrWhiteSpace(item.Name)
            || item.Condition is not ("ok" or "damaged" or "missing")))
        {
            throw new InvalidOperationException("inspection.approved contiene renglones inválidos.");
        }

<<<<<<< HEAD
        if (data.Items.Select(item => item.InspectionItemId).Distinct().Count() != data.Items.Count)
        {
            throw new InvalidOperationException("inspection.approved contiene inspection_item_id duplicados.");
        }
=======
            long workOrderId = root.TryGetProperty("work_order_id", out var woId) ? woId.GetInt64() : 0;
            long inspectionId = root.TryGetProperty("inspection_id", out var inspId) ? inspId.GetInt64() : 0;
            string kind = root.TryGetProperty("kind", out var kindElem) ? kindElem.GetString() ?? "quick" : "quick";

            // Extracción segura de la fecha de ocurrencia compatible con DateTime / DateTimeOffset
            DateTime occurredAt = DateTime.UtcNow;
            if (root.TryGetProperty("occurred_at", out var occElem))
            {
                if (occElem.TryGetDateTime(out var dt)) occurredAt = dt;
                else if (occElem.TryGetDateTimeOffset(out var dto)) occurredAt = dto.UtcDateTime;
            }
            else if (envelope.OccurredAt != default)
            {
                occurredAt = envelope.OccurredAt.UtcDateTime;
            }

            if (inspectionId > 0)
            {
                // GARANTÍA DE LLAVE FORÁNEA: Insertamos preventivamente la inspección si aún no existe
                await _db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO inspections (inspection_id, work_order_id, kind, status, created_at, occurred_at) VALUES ({0}, {1}, {2}, {3}, {4}, {5}) ON CONFLICT (inspection_id) DO NOTHING",
                    new object[] { inspectionId, workOrderId, kind, "approved", DateTime.UtcNow, occurredAt }, ct);
            }
>>>>>>> c6c32682a7533a0f3e80a7c2cbb8a1ce2fa37844

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var workOrder = await _db.WorkOrders.SingleOrDefaultAsync(
            order => order.WorkOrderId == data.WorkOrderId, ct);
        if (workOrder is null)
        {
            throw new MissingDependencyException("WorkOrder",
                $"La orden {data.WorkOrderId} de la inspección aún no existe.");
        }

        var inspection = await _db.Inspections.SingleOrDefaultAsync(
            candidate => candidate.InspectionId == data.InspectionId, ct);
        if (inspection is null)
        {
            throw new MissingDependencyException("Inspection",
                $"La inspección {data.InspectionId} aún no existe.");
        }

        if (inspection.WorkOrderId != data.WorkOrderId)
        {
            throw new InvalidOperationException("La inspección no pertenece a la orden indicada.");
        }

        inspection.Status = "approved";
        inspection.Kind = data.Kind;
        inspection.ApprovedBy = data.ApprovedBy;
        inspection.ApprovedAt = data.ApprovedAt.UtcDateTime;

        var affectedLineIds = data.Items.Select(item => item.BomLineId).Distinct().ToArray();
        var affectedPartIds = data.Items
            .Where(item => item.PartId.HasValue)
            .Select(item => item.PartId!.Value)
            .ToHashSet();
        var existingNeedParts = await _db.WorkOrderNeeds
            .Where(need => need.WorkOrderId == workOrder.WorkOrderId
                && affectedLineIds.Contains(need.BomLineId)
                && need.PartId.HasValue)
            .Select(need => need.PartId!.Value)
            .ToListAsync(ct);
        affectedPartIds.UnionWith(existingNeedParts);

        foreach (var item in data.Items)
        {
            var inspectionItem = await _db.InspectionItems.SingleOrDefaultAsync(
                candidate => candidate.InspectionItemId == item.InspectionItemId
                    && candidate.InspectionId == data.InspectionId, ct);
            if (inspectionItem is null)
            {
                throw new MissingDependencyException("InspectionItem",
                    $"El renglón {item.InspectionItemId} aún no existe en la inspección.");
            }

            inspectionItem.BomLineId = item.BomLineId;
            inspectionItem.PartId = item.PartId;
            inspectionItem.Name = item.Name;
            inspectionItem.GroupName = item.GroupName;
            inspectionItem.Condition = item.Condition;
            inspectionItem.Action = item.Action;
            inspectionItem.Quantity = item.Quantity;
            inspectionItem.Note = item.Note;

            if (item.Action != "buy" || item.Condition == "ok")
            {
                await CloseNeedAsync(workOrder.WorkOrderId, item.BomLineId, ct);
                continue;
            }

            var bomLine = await _db.BomLines.SingleOrDefaultAsync(
                line => line.BomLineId == item.BomLineId
                    && line.ModelId == workOrder.ModelId
                    && line.RemovedAt == null, ct);
            if (bomLine is null)
            {
                throw new MissingDependencyException("BomLine",
                    $"La línea {item.BomLineId} aún no existe en la lista de materiales vigente.");
            }

            var partId = item.PartId;
            if (partId.HasValue
                && !await _db.Parts.AnyAsync(part => part.PartId == partId.Value, ct))
            {
                throw new MissingDependencyException("Part",
                    $"La pieza {partId.Value} aún no existe en el catálogo.");
            }

            var requiredQuantity = item.Quantity ?? bomLine.QtyPerUnit;
            if (requiredQuantity is <= 0)
            {
                throw new InvalidOperationException(
                    $"La cantidad requerida para la línea {item.BomLineId} debe ser positiva.");
            }

            await UpsertNeedAsync(workOrder, inspection, inspectionItem, item, partId,
                requiredQuantity, ct);
        }

        await RefreshReorderSuggestionsAsync(affectedPartIds.ToArray(), ct);

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        _logger.LogInformation("Inspección {InspectionId} aprobada para la orden {WorkOrderId}.",
            data.InspectionId, data.WorkOrderId);
    }

    private async Task UpsertNeedAsync(
        WorkOrder workOrder,
        Inspection inspection,
        InspectionItem inspectionItem,
        InspectionItemPayload item,
        long? partId,
        int? requiredQuantity,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var need = await _db.WorkOrderNeeds.SingleOrDefaultAsync(
            candidate => candidate.WorkOrderId == workOrder.WorkOrderId
                && candidate.BomLineId == item.BomLineId, ct);

        if (need is null)
        {
            need = new WorkOrderNeed
            {
                WorkOrderId = workOrder.WorkOrderId,
                BomLineId = item.BomLineId,
                Status = "active"
            };
            _db.WorkOrderNeeds.Add(need);
        }

        need.PartId = partId;
        need.Name = item.Name;
        need.InspectionId = inspection.InspectionId;
        need.InspectionItemId = inspectionItem.InspectionItemId;
        need.RequiredQty = requiredQuantity;
        need.Status = "active";
        need.UpdatedAt = now;

        var oldReservation = await _db.Reservations.SingleOrDefaultAsync(
            reservation => reservation.WorkOrderId == workOrder.WorkOrderId
                && reservation.BomLineId == item.BomLineId
                && reservation.LocationId == ReceivingLocationId
                && reservation.Status == "active", ct);

        InventoryBalance? oldBalance = null;
        if (oldReservation is not null)
        {
            oldBalance = await GetBalanceForUpdateAsync(oldReservation.PartId, ct);
            if (oldBalance is not null)
            {
                oldBalance.Reserved = Math.Max(0, oldBalance.Reserved - oldReservation.Quantity);
                oldBalance.UpdatedAt = now;
            }
        }

        var reservedQuantity = 0;
        InventoryBalance? balance = null;
        if (partId.HasValue && requiredQuantity.HasValue)
        {
            balance = await GetBalanceForUpdateAsync(partId.Value, ct);
            if (balance is null)
            {
                if (!await _db.Locations.AnyAsync(location => location.LocationId == ReceivingLocationId, ct))
                {
                    throw new MissingDependencyException("Location",
                        $"La ubicación de recepción {ReceivingLocationId} aún no existe.");
                }

                balance = new InventoryBalance
                {
                    PartId = partId.Value,
                    LocationId = ReceivingLocationId,
                    OnHand = 0,
                    Reserved = 0,
                    UpdatedAt = now
                };
                _db.InventoryBalances.Add(balance);
            }

            reservedQuantity = Math.Min(
                Math.Max(0, balance.OnHand - balance.Reserved),
                requiredQuantity.Value);
            balance.Reserved += reservedQuantity;
            balance.UpdatedAt = now;
        }

        if (oldReservation is not null)
        {
            if (reservedQuantity == 0)
            {
                oldReservation.Status = "cancelled";
                oldReservation.CancelledAt = now;
                oldReservation.UpdatedAt = now;
            }
            else
            {
                oldReservation.PartId = partId!.Value;
                oldReservation.Quantity = reservedQuantity;
                oldReservation.InspectionId = inspection.InspectionId;
                oldReservation.InspectionItemId = inspectionItem.InspectionItemId;
                oldReservation.UpdatedAt = now;
            }
<<<<<<< HEAD
=======

            foreach (var item in items)
            {
                string sku = item.TryGetProperty("sku", out var s) ? s.GetString() ?? "" : "";
                long partIdParam = item.TryGetProperty("part_id", out var pi) && pi.ValueKind == JsonValueKind.Number ? pi.GetInt64() : 0;

                long partId = 0;

                if (!string.IsNullOrWhiteSpace(sku))
                {
                    string cleanSku = sku.Trim().ToUpperInvariant();
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
                string itemName = item.TryGetProperty("name", out var n) ? n.GetString() ?? $"Item-{inspectionItemId}" : $"Item-{inspectionItemId}";

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
                        Name = itemName, // Asignamos el nombre para cumplir con la restricción NOT NULL de shortages
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
>>>>>>> c6c32682a7533a0f3e80a7c2cbb8a1ce2fa37844
        }
        else if (reservedQuantity > 0)
        {
            oldReservation = new Reservation
            {
                WorkOrderId = workOrder.WorkOrderId,
                BomLineId = item.BomLineId,
                PartId = partId!.Value,
                LocationId = ReceivingLocationId,
                InspectionId = inspection.InspectionId,
                InspectionItemId = inspectionItem.InspectionItemId,
                Quantity = reservedQuantity,
                FulfilledQty = 0,
                Status = "active",
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.Reservations.Add(oldReservation);
        }

        var missingQuantity = requiredQuantity.HasValue
            ? requiredQuantity.Value - reservedQuantity
            : (int?)null;
        var shortage = await _db.Shortages.SingleOrDefaultAsync(
            candidate => candidate.WorkOrderId == workOrder.WorkOrderId
                && candidate.BomLineId == item.BomLineId
                && candidate.Status == "open", ct);

        if (missingQuantity is null or > 0)
        {
            if (shortage is null)
            {
                shortage = new Shortage
                {
                    WorkOrderId = workOrder.WorkOrderId,
                    BomLineId = item.BomLineId,
                    CreatedAt = now
                };
                _db.Shortages.Add(shortage);
            }

            shortage.PartId = partId;
            shortage.Name = item.Name;
            shortage.InspectionId = inspection.InspectionId;
            shortage.InspectionItemId = inspectionItem.InspectionItemId;
            shortage.MissingQuantity = missingQuantity;
            shortage.CoveredQuantity = 0;
            shortage.Status = "open";
            shortage.ResolvedAt = null;
        }
        else if (shortage is not null)
        {
            shortage.Status = "closed";
            shortage.ResolvedAt = now;
        }

        await _db.SaveChangesAsync(ct);

        if (reservedQuantity > 0)
        {
            AddOutboxEvent("stock.reserved", workOrder.Code, new
            {
                reservation_id = oldReservation!.ReservationId,
                work_order_id = workOrder.WorkOrderId,
                part_id = partId!.Value,
                quantity = reservedQuantity,
                inspection_item_id = inspectionItem.InspectionItemId
            }, now);
        }

        if (missingQuantity is null or > 0)
        {
            AddOutboxEvent("stock.shortage_detected", workOrder.Code, new
            {
                work_order_id = workOrder.WorkOrderId,
                part_id = partId,
                name = item.Name,
                missing_quantity = missingQuantity,
                inspection_item_id = inspectionItem.InspectionItemId
            }, now);
        }
    }

    private async Task CloseNeedAsync(long workOrderId, long bomLineId, CancellationToken ct)
    {
        var need = await _db.WorkOrderNeeds.SingleOrDefaultAsync(
            candidate => candidate.WorkOrderId == workOrderId
                && candidate.BomLineId == bomLineId
                && candidate.Status == "active", ct);
        if (need is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var reservations = await _db.Reservations
            .Where(reservation => reservation.WorkOrderId == workOrderId
                && reservation.BomLineId == bomLineId
                && reservation.Status == "active")
            .ToListAsync(ct);
        foreach (var reservation in reservations)
        {
            var balance = await GetBalanceForUpdateAsync(reservation.PartId, ct);
            if (balance is not null)
            {
                balance.Reserved = Math.Max(0, balance.Reserved - reservation.Quantity);
                balance.UpdatedAt = now;
            }

            reservation.Status = "cancelled";
            reservation.CancelledAt = now;
            reservation.UpdatedAt = now;
        }

        var shortages = await _db.Shortages
            .Where(shortage => shortage.WorkOrderId == workOrderId
                && shortage.BomLineId == bomLineId
                && shortage.Status == "open")
            .ToListAsync(ct);
        foreach (var shortage in shortages)
        {
            shortage.Status = "closed";
            shortage.ResolvedAt = now;
        }

        need.Status = "closed";
        need.UpdatedAt = now;
    }

    private async Task<InventoryBalance?> GetBalanceForUpdateAsync(long partId, CancellationToken ct)
    {
        return await _db.InventoryBalances
            .FromSqlInterpolated(
                $"SELECT * FROM inventory_balances WHERE part_id = {partId} AND location_id = {ReceivingLocationId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
    }

    private async Task RefreshReorderSuggestionsAsync(long[] partIds, CancellationToken ct)
    {
        foreach (var partId in partIds)
        {
            var shortages = await _db.Shortages
                .Where(shortage => shortage.PartId == partId && shortage.Status == "open")
                .Select(shortage => new { shortage.WorkOrderId, shortage.MissingQuantity, shortage.CoveredQuantity })
                .ToListAsync(ct);
            var requestedQuantity = shortages
                .Where(shortage => shortage.MissingQuantity.HasValue)
                .Sum(shortage => Math.Max(0, shortage.MissingQuantity!.Value - shortage.CoveredQuantity));
            var workOrderIds = shortages.Select(shortage => shortage.WorkOrderId).Distinct().Order().ToArray();
            var suggestion = await _db.ReorderSuggestions
                .Include(candidate => candidate.WorkOrders)
                .SingleOrDefaultAsync(candidate => candidate.PartId == partId && candidate.Status == "active", ct);
            if (requestedQuantity <= 0)
            {
                if (suggestion is not null)
                {
                    suggestion.Status = "fulfilled";
                    suggestion.UpdatedAt = DateTime.UtcNow;
                    suggestion.WorkOrders.Clear();
                }

                continue;
            }

            var changed = suggestion is null
                || suggestion.SuggestedQuantity != requestedQuantity
                || !suggestion.WorkOrders.Select(order => order.WorkOrderId).Order().SequenceEqual(workOrderIds);

            if (!changed)
            {
                continue;
            }

            var now = DateTime.UtcNow;
            if (suggestion is null)
            {
                suggestion = new ReorderSuggestion
                {
                    PartId = partId,
                    Status = "active",
                    CreatedAt = now
                };
                _db.ReorderSuggestions.Add(suggestion);
            }

            suggestion.SuggestedQuantity = requestedQuantity;
            suggestion.UpdatedAt = now;
            suggestion.WorkOrders.Clear();
            var workOrders = await _db.WorkOrders
                .Where(order => workOrderIds.Contains(order.WorkOrderId))
                .ToListAsync(ct);
            foreach (var order in workOrders)
            {
                suggestion.WorkOrders.Add(order);
            }

            AddOutboxEvent("stock.reorder_suggested", $"part:{partId}", new
            {
                part_id = partId,
                suggested_quantity = requestedQuantity,
                work_order_ids = workOrderIds
            }, now);
        }
    }

    private void AddOutboxEvent(string eventType, string key, object payload, DateTime occurredAt)
    {
        _db.OutboxEvents.Add(new OutboxEvent
        {
            EventId = Guid.NewGuid(),
            EventType = eventType,
            EventVersion = 1,
            OccurredAt = occurredAt,
            EventKey = key,
            Payload = JsonSerializer.Serialize(payload),
            Published = false,
            CreatedAt = occurredAt
        });
    }

    private sealed class InspectionApprovedPayload
    {
        [JsonPropertyName("inspection_id")]
        public long InspectionId { get; init; }

        [JsonPropertyName("work_order_id")]
        public long WorkOrderId { get; init; }

        [JsonPropertyName("kind")]
        public string Kind { get; init; } = string.Empty;

        [JsonPropertyName("items")]
        public List<InspectionItemPayload>? Items { get; init; }

        [JsonPropertyName("approved_by")]
        public long ApprovedBy { get; init; }

        [JsonPropertyName("approved_at")]
        public DateTimeOffset ApprovedAt { get; init; }
    }

    private sealed class InspectionItemPayload
    {
        [JsonPropertyName("inspection_item_id")]
        public long InspectionItemId { get; init; }

        [JsonPropertyName("bom_line_id")]
        public long BomLineId { get; init; }

        [JsonPropertyName("part_id")]
        public long? PartId { get; init; }

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("group_name")]
        public string? GroupName { get; init; }

        [JsonPropertyName("condition")]
        public string Condition { get; init; } = string.Empty;

        [JsonPropertyName("action")]
        public string? Action { get; init; }

        [JsonPropertyName("quantity")]
        public int? Quantity { get; init; }

        [JsonPropertyName("note")]
        public string? Note { get; init; }
    }
}
