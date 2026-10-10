using System.Text.RegularExpressions;
using AlmacenTaller.DataContext;
using AlmacenTaller.Messaging;
using AlmacenTaller.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Globalization;

namespace AlmacenTaller.Services.Handlers;

public class PurchaseItemReceivedHandler : IEventHandler
{
    public IReadOnlyCollection<string> EventTypes => new[] { "purchase.item_received", "item_received" };

    private readonly AppDbContext _db;
    private readonly ILogger<PurchaseItemReceivedHandler> _logger;

    public PurchaseItemReceivedHandler(AppDbContext db, ILogger<PurchaseItemReceivedHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        try
        {
            var data = envelope.Data;

            string purpose = data.TryGetProperty("purpose", out var pElem) ? pElem.GetString() ?? "restock" : "restock";
            if (purpose == "customer_order")
            {
                _logger.LogInformation("Compra {EventId} ignorada por ser customer_order.", envelope.EventId);
                return;
            }

            long lineId = data.GetProperty("line_id").GetInt64();
            string rawPartNumber = data.GetProperty("part_number").GetString() ?? string.Empty;
            string description = data.TryGetProperty("description", out var dElem) ? dElem.GetString() ?? string.Empty : string.Empty;
            int quantity = data.GetProperty("quantity").GetInt32();

            decimal unitPrice = 0;
            if (data.TryGetProperty("unit_price", out var upElem))
            {
                if (upElem.ValueKind == JsonValueKind.Number)
                    unitPrice = upElem.GetDecimal();
                else if (decimal.TryParse(upElem.GetString(), out var parsedPrice))
                    unitPrice = parsedPrice;
            }

            string currency = data.TryGetProperty("currency", out var cElem) ? cElem.GetString() ?? "MXN" : "MXN";

            static string NormalizeSku(string input)
            {
                if (string.IsNullOrWhiteSpace(input)) return string.Empty;
                var s = input.Trim();
                if (s.StartsWith("NP:", StringComparison.OrdinalIgnoreCase))
                    s = s[3..].Trim();
                else if (s.StartsWith("NP ", StringComparison.OrdinalIgnoreCase))
                    s = s[3..].Trim();

                return Regex.Replace(s, @"[\s\-_.]+", "").ToLowerInvariant();
            }

            string cleanSku = NormalizeSku(rawPartNumber);
            string cleanDesc = NormalizeSku(description);

            var matchingParts = await _db.Parts.Where(p =>
                p.SkuNorm == cleanSku ||
                (p.Sku != null && p.Sku == rawPartNumber.Trim()) ||
                (cleanDesc != "" && (p.SkuNorm == cleanDesc || p.Sku == description.Trim()))).ToListAsync(ct);

            if (!matchingParts.Any())
            {
                var allParts = await _db.Parts.Select(p => new { p.PartId, p.Sku, p.SkuNorm }).ToListAsync(ct);
                var memoryMatches = allParts.Where(p =>
                    (p.SkuNorm != null && (p.SkuNorm == cleanSku || (cleanDesc != "" && p.SkuNorm == cleanDesc))) ||
                    (p.Sku != null && (NormalizeSku(p.Sku) == cleanSku || (cleanDesc != "" && NormalizeSku(p.Sku) == cleanDesc)))).ToList();

                if (memoryMatches.Any())
                {
                    var matchedIds = memoryMatches.Select(m => m.PartId).ToList();
                    matchingParts = await _db.Parts.Where(p => matchedIds.Contains(p.PartId)).ToListAsync(ct);
                }
            }

            const long RECEIVING_LOCATION = 100;

            if (matchingParts.Count != 1)
            {
                var candidateIds = matchingParts.Select(p => p.PartId).ToList();
                _logger.LogWarning("Recepción a manual: part_number='{Raw}' tiene {Count} posibles coincidencias.", rawPartNumber, candidateIds.Count);

                _db.UnmatchedReceipts.Add(new UnmatchedReceipt
                {
                    LineId = lineId,
                    PartNumber = rawPartNumber, // <-- ¡Asignamos el número de parte obligatorio!
                    Status = "open",
                    CandidatePartIds = candidateIds
                });

                var unmatchedPayload = new
                {
                    purchase_line_id = lineId,
                    part_number = rawPartNumber,
                    description = description,
                    quantity = quantity,
                    candidates = candidateIds
                };

                _db.OutboxEvents.Add(new OutboxEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = "stock.unmatched_receipt",
                    EventVersion = 1,
                    OccurredAt = DateTime.UtcNow,
                    EventKey = $"purchase_line:{lineId}",
                    Payload = JsonSerializer.Serialize(unmatchedPayload),
                    Published = false
                });

                await _db.SaveChangesAsync(ct);
                return;
            }

            var part = matchingParts.First();
            _logger.LogInformation("Match exitoso: ID={PartId}, SKU={Sku}", part.PartId, part.Sku);

            var balance = await _db.InventoryBalances
                .FirstOrDefaultAsync(b => b.PartId == part.PartId && b.LocationId == RECEIVING_LOCATION, ct);

            if (balance == null)
            {
                balance = new InventoryBalance
                {
                    PartId = part.PartId,
                    LocationId = RECEIVING_LOCATION,
                    OnHand = quantity,
                    Reserved = 0,
                    UpdatedAt = DateTime.UtcNow
                };
                _db.InventoryBalances.Add(balance);
            }
            else
            {
                balance.OnHand += quantity;
                balance.UpdatedAt = DateTime.UtcNow;
            }

            _db.StockMovements.Add(new StockMovement
            {
                EventId = envelope.EventId.ToString(),
                PartId = (int)part.PartId,
                LocationId = (int)RECEIVING_LOCATION,
                Quantity = quantity,
                UnitCost = unitPrice,
                MovementType = "receipt",
                ReferenceId = lineId.ToString(),
                CreatedAt = DateTime.UtcNow
            });

            var receivedPayload = new
            {
                part_id = part.PartId,
                location_id = RECEIVING_LOCATION,
                quantity = quantity,
                unit_cost = unitPrice.ToString(CultureInfo.InvariantCulture),
                currency = currency,
                purchase_line_id = lineId
            };

            _db.OutboxEvents.Add(new OutboxEvent
            {
                EventId = Guid.NewGuid(),
                EventType = "stock.received",
                EventVersion = 1,
                OccurredAt = DateTime.UtcNow,
                EventKey = $"part:{part.PartId}",
                Payload = JsonSerializer.Serialize(receivedPayload),
                Published = false
            });

            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("stock.received publicado en outbox para part_id {PartId}", part.PartId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error crítico procesando PurchaseItemReceivedHandler para el evento {EventId}", envelope.EventId);
            throw;
        }
    }
}