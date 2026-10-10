using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AlmacenTaller.DataContext;
using AlmacenTaller.Messaging;
using AlmacenTaller.Models;
using Microsoft.EntityFrameworkCore;

namespace AlmacenTaller.Services.Handlers;

public class PurchaseItemReceivedHandler : IEventHandler
{
    private const long ReceivingLocationId = 100;

    private readonly AppDbContext _db;
    private readonly ILogger<PurchaseItemReceivedHandler> _logger;

    public IReadOnlyCollection<string> EventTypes => new[] { "purchase.item_received", "item_received" };

    public PurchaseItemReceivedHandler(AppDbContext db, ILogger<PurchaseItemReceivedHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        var data = envelope.Data;
        var purpose = ReadString(data, "purpose") ?? "restock";
        var lineId = ReadRequiredInt64(data, "line_id");
        var purchaseRequestId = ReadRequiredInt64(data, "purchase_request_id");
        var requestNumber = ReadRequiredString(data, "request_number");
        var partNumber = ReadRequiredString(data, "part_number");
        var description = ReadRequiredString(data, "description");
        var quantity = ReadRequiredInt32(data, "quantity");
        var unitPrice = ReadDecimal(data, "unit_price");
        var currency = ReadRequiredString(data, "currency");
        var receivedAt = ReadRequiredDateTimeOffset(data, "received_at").UtcDateTime;
        var workOrderCode = ReadString(data, "work_order_code");

        if (lineId <= 0 || purchaseRequestId <= 0 || quantity <= 0
            || unitPrice < 0 || purpose is not ("restock" or "customer_order"))
        {
            throw new InvalidOperationException("purchase.item_received tiene datos inválidos.");
        }

        if (await _db.PurchaseReceipts.AnyAsync(receipt => receipt.LineId == lineId, ct))
        {
            _logger.LogInformation("Recepción de compra {LineId} ya procesada.", lineId);
            return;
        }

        var receipt = new PurchaseReceipt
        {
            LineId = lineId,
            PurchaseRequestId = purchaseRequestId,
            RequestNumber = requestNumber,
            PartNumber = partNumber,
            Description = description,
            Quantity = quantity,
            UnitPrice = unitPrice,
            Currency = currency,
            Purpose = purpose,
            ReceivedAt = receivedAt,
            WorkOrderCode = workOrderCode,
            Status = purpose == "customer_order" ? "ignored_customer_order" : "pending",
            CreatedAt = envelope.OccurredAt.UtcDateTime
        };
        _db.PurchaseReceipts.Add(receipt);

        if (purpose == "customer_order")
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Compra {LineId} ignorada por ser customer_order.", lineId);
            return;
        }

        var normalizedPartNumber = NormalizePartNumber(partNumber);
        var matchingParts = await _db.Parts
            .Where(part => part.Sku != null)
            .ToListAsync(ct);
        var matches = matchingParts
            .Where(part => NormalizePartNumber(part.Sku!) == normalizedPartNumber)
            .ToList();

        if (matches.Count != 1)
        {
            var candidates = matches.Select(part => part.PartId).ToList();
            receipt.Status = "unmatched";
            _db.UnmatchedReceipts.Add(new UnmatchedReceipt
            {
                LineId = lineId,
                PartNumber = partNumber,
                CandidatePartIds = candidates,
                Reason = matches.Count == 0 ? "not_found" : "ambiguous",
                Status = "open",
                CreatedAt = envelope.OccurredAt.UtcDateTime
            });

            AddOutboxEvent("stock.unmatched_receipt", $"purchase_line:{lineId}", new
            {
                purchase_line_id = lineId,
                part_number = partNumber,
                description,
                quantity,
                candidates
            }, envelope.OccurredAt.UtcDateTime);

            await _db.SaveChangesAsync(ct);
            _logger.LogWarning(
                "Recepción {LineId} requiere relación manual: part_number='{PartNumber}', coincidencias={Count}.",
                lineId, partNumber, matches.Count);
            return;
        }

        var matchedPart = matches[0];
        receipt.MatchedPartId = matchedPart.PartId;
        receipt.LocationId = ReceivingLocationId;
        receipt.Status = "received";

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        var balance = await GetBalanceForUpdateAsync(matchedPart.PartId, ct);
        if (balance is null)
        {
            if (!await _db.Locations.AnyAsync(location => location.LocationId == ReceivingLocationId, ct))
            {
                throw new MissingDependencyException("Location",
                    $"La ubicación de recepción {ReceivingLocationId} aún no existe.");
            }

            balance = new InventoryBalance
            {
                PartId = matchedPart.PartId,
                LocationId = ReceivingLocationId,
                OnHand = 0,
                Reserved = 0,
                UpdatedAt = receivedAt
            };
            _db.InventoryBalances.Add(balance);
        }

        balance.OnHand += quantity;
        balance.UpdatedAt = receivedAt;
        _db.StockMovements.Add(new StockMovement
        {
            EventId = envelope.EventId.ToString(),
            PartId = checked((int)matchedPart.PartId),
            LocationId = checked((int)ReceivingLocationId),
            Quantity = quantity,
            UnitCost = unitPrice,
            MovementType = "receipt",
            ReferenceId = lineId.ToString(CultureInfo.InvariantCulture),
            CreatedAt = receivedAt
        });

        AddOutboxEvent("stock.received", $"part:{matchedPart.PartId}", new
        {
            part_id = matchedPart.PartId,
            location_id = ReceivingLocationId,
            quantity,
            unit_cost = unitPrice.ToString(CultureInfo.InvariantCulture),
            currency,
            purchase_line_id = lineId
        }, receivedAt);

        await _db.SaveChangesAsync(ct);
        await CoverShortagesAsync(matchedPart.PartId, workOrderCode, receivedAt, ct);
        await RefreshReorderSuggestionAsync(matchedPart.PartId, receivedAt, ct);
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        _logger.LogInformation("Recepción {LineId} registrada para la pieza {PartId}.",
            lineId, matchedPart.PartId);
    }

    private async Task CoverShortagesAsync(
        long partId, string? workOrderCode, DateTime occurredAt, CancellationToken ct)
    {
        var balance = await GetBalanceForUpdateAsync(partId, ct)
            ?? throw new InvalidOperationException($"No existe saldo para la pieza {partId} tras la recepción.");

        var shortages = await _db.Shortages
            .Where(shortage => shortage.PartId == partId
                && shortage.Status == "open"
                && shortage.MissingQuantity.HasValue)
            .Join(_db.WorkOrders,
                shortage => shortage.WorkOrderId,
                order => order.WorkOrderId,
                (shortage, order) => new { Shortage = shortage, OrderCode = order.Code, order.IsDeleted })
            .Where(item => !item.IsDeleted)
            .OrderBy(item => item.OrderCode == workOrderCode && workOrderCode != null ? 0 : 1)
            .ThenBy(item => item.Shortage.CreatedAt)
            .Select(item => item.Shortage)
            .ToListAsync(ct);

        foreach (var shortage in shortages)
        {
            var remaining = shortage.MissingQuantity!.Value - shortage.CoveredQuantity;
            var quantityToCover = Math.Min(Math.Max(0, balance.OnHand - balance.Reserved), remaining);
            if (quantityToCover <= 0)
            {
                continue;
            }

            var workOrder = await _db.WorkOrders.SingleAsync(
                order => order.WorkOrderId == shortage.WorkOrderId, ct);
            var need = await _db.WorkOrderNeeds.SingleOrDefaultAsync(
                candidate => candidate.WorkOrderId == shortage.WorkOrderId
                    && candidate.BomLineId == shortage.BomLineId
                    && candidate.Status == "active", ct);
            if (need is null || need.InspectionId != shortage.InspectionId
                || need.InspectionItemId != shortage.InspectionItemId)
            {
                continue;
            }

            if (!await _db.Inspections.AnyAsync(
                    inspection => inspection.InspectionId == shortage.InspectionId, ct)
                || !await _db.InspectionItems.AnyAsync(
                    item => item.InspectionItemId == shortage.InspectionItemId, ct))
            {
                throw new MissingDependencyException("Inspection",
                    $"Falta inspección asociada al faltante {shortage.ShortageId}.");
            }

            var reservation = await _db.Reservations.SingleOrDefaultAsync(
                candidate => candidate.WorkOrderId == shortage.WorkOrderId
                    && candidate.BomLineId == shortage.BomLineId
                    && candidate.LocationId == ReceivingLocationId
                    && candidate.Status == "active", ct);
            if (reservation is null)
            {
                reservation = new Reservation
                {
                    WorkOrderId = shortage.WorkOrderId,
                    BomLineId = shortage.BomLineId,
                    PartId = partId,
                    LocationId = ReceivingLocationId,
                    InspectionId = shortage.InspectionId,
                    InspectionItemId = shortage.InspectionItemId,
                    Quantity = quantityToCover,
                    FulfilledQty = 0,
                    Status = "active",
                    CreatedAt = occurredAt,
                    UpdatedAt = occurredAt
                };
                _db.Reservations.Add(reservation);
            }
            else
            {
                reservation.Quantity += quantityToCover;
                reservation.UpdatedAt = occurredAt;
            }

            balance.Reserved += quantityToCover;
            shortage.CoveredQuantity += quantityToCover;
            if (shortage.CoveredQuantity >= shortage.MissingQuantity.Value)
            {
                shortage.Status = "resolved";
                shortage.ResolvedAt = occurredAt;
            }

            await _db.SaveChangesAsync(ct);
            AddOutboxEvent("stock.reserved", workOrder.Code, new
            {
                reservation_id = reservation.ReservationId,
                work_order_id = workOrder.WorkOrderId,
                part_id = partId,
                quantity = quantityToCover,
                inspection_item_id = shortage.InspectionItemId
            }, occurredAt);
            AddOutboxEvent("stock.shortage_resolved", workOrder.Code, new
            {
                work_order_id = workOrder.WorkOrderId,
                part_id = partId,
                quantity = quantityToCover
            }, occurredAt);
        }
    }

    private async Task<InventoryBalance?> GetBalanceForUpdateAsync(long partId, CancellationToken ct)
    {
        return await _db.InventoryBalances
            .FromSqlInterpolated(
                $"SELECT * FROM inventory_balances WHERE part_id = {partId} AND location_id = {ReceivingLocationId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
    }

    private async Task RefreshReorderSuggestionAsync(long partId, DateTime occurredAt, CancellationToken ct)
    {
        var shortages = await _db.Shortages
            .Where(shortage => shortage.PartId == partId && shortage.Status == "open")
            .Select(shortage => new
            {
                shortage.WorkOrderId,
                shortage.MissingQuantity,
                shortage.CoveredQuantity
            })
            .ToListAsync(ct);
        var suggestedQuantity = shortages
            .Where(shortage => shortage.MissingQuantity.HasValue)
            .Sum(shortage => Math.Max(
                0, shortage.MissingQuantity!.Value - shortage.CoveredQuantity));
        var workOrderIds = shortages.Select(shortage => shortage.WorkOrderId)
            .Distinct()
            .Order()
            .ToArray();
        var suggestion = await _db.ReorderSuggestions
            .Include(candidate => candidate.WorkOrders)
            .SingleOrDefaultAsync(candidate => candidate.PartId == partId
                && candidate.Status == "active", ct);

        if (suggestedQuantity <= 0)
        {
            if (suggestion is not null)
            {
                suggestion.Status = "fulfilled";
                suggestion.UpdatedAt = occurredAt;
                suggestion.WorkOrders.Clear();
            }

            return;
        }

        var changed = suggestion is null
            || suggestion.SuggestedQuantity != suggestedQuantity
            || !suggestion.WorkOrders.Select(order => order.WorkOrderId)
                .Order().SequenceEqual(workOrderIds);
        if (!changed)
        {
            return;
        }

        if (suggestion is null)
        {
            suggestion = new ReorderSuggestion
            {
                PartId = partId,
                Status = "active",
                CreatedAt = occurredAt
            };
            _db.ReorderSuggestions.Add(suggestion);
        }

        suggestion.SuggestedQuantity = suggestedQuantity;
        suggestion.UpdatedAt = occurredAt;
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
            suggested_quantity = suggestedQuantity,
            work_order_ids = workOrderIds
        }, occurredAt);
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

    private static string NormalizePartNumber(string value)
    {
        return Regex.Replace(value.Trim(), @"\s+", "").ToUpperInvariant();
    }

    private static string? ReadString(JsonElement data, string propertyName)
    {
        return data.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string ReadRequiredString(JsonElement data, string propertyName)
    {
        var value = ReadString(data, propertyName);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"purchase.item_received sin {propertyName} válido.")
            : value;
    }

    private static long ReadRequiredInt64(JsonElement data, string propertyName)
    {
        return data.TryGetProperty(propertyName, out var value) && value.TryGetInt64(out var result)
            ? result
            : throw new InvalidOperationException($"purchase.item_received sin {propertyName} válido.");
    }

    private static int ReadRequiredInt32(JsonElement data, string propertyName)
    {
        return data.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var result)
            ? result
            : throw new InvalidOperationException($"purchase.item_received sin {propertyName} válido.");
    }

    private static decimal ReadDecimal(JsonElement data, string propertyName)
    {
        if (!data.TryGetProperty(propertyName, out var value))
        {
            throw new InvalidOperationException($"purchase.item_received sin {propertyName} válido.");
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        throw new InvalidOperationException($"purchase.item_received sin {propertyName} válido.");
    }

    private static DateTimeOffset ReadRequiredDateTimeOffset(JsonElement data, string propertyName)
    {
        return data.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
            && value.TryGetDateTimeOffset(out var result)
            ? result
            : throw new InvalidOperationException($"purchase.item_received sin {propertyName} válido.");
    }
}
