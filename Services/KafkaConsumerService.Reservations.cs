using System.Text.Json;
using AlmacenTaller.DataContext;
using AlmacenTaller.Models.Events;
using AlmacenTaller.Models;
using Microsoft.EntityFrameworkCore;

namespace AlmacenTaller.Services;

// =====================================================================================
// CÓDIGO DEL EQUIPO, MOVIDO SIN CAMBIOS desde KafkaConsumerService.cs.
// Todavía NO se invoca desde el enrutador: depende de los modelos pendientes (Shortage,
// OutboxEvent, PendingEvent, InspectionApprovedPayload) y hay que reescribirlo contra el
// contrato real (inspection.approved trae data.items[], no sku/required_quantity).
// Cuando esté listo, conviértanlo en un IEventHandler (ver Messaging/IEventHandler.cs).
// =====================================================================================
public partial class KafkaConsumerService
{
    private async Task ProcessAtomicReservationAsync(long workOrderId, long bomLineId, long partId, int requiredQuantity)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // 1. Start the transaction
        using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            // 2. ATOMIC LOCK: Read the inventory blocking the row
            var sql = "SELECT * FROM inventory_balances WHERE part_id = {0} AND location_id = 100 FOR UPDATE";
            var balance = await db.InventoryBalances
                                  .FromSqlRaw(sql, partId)
                                  .SingleOrDefaultAsync();

            if (balance == null)
            {
                _logger.LogWarning("No inventory record found for part {PartId}", partId);
                return;
            }

            // 3. INVENTORY RULE: OnHand - Reserved = Available
            int availableQty = balance.OnHand - balance.Reserved;

            if (availableQty >= requiredQuantity)
            {
                // IN STOCK: Increase reservation
                balance.Reserved += requiredQuantity;
                balance.UpdatedAt = DateTime.UtcNow;

                db.Reservations.Add(new Reservation
                {
                    WorkOrderId = workOrderId,
                    BomLineId = bomLineId,
                    PartId = partId,
                    LocationId = 100,
                    Quantity = requiredQuantity,
                    FulfilledQty = 0,
                    Status = "active",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });

                // Emit Outbox Event (stock.reserved)
                var outboxPayload = new { part_id = partId, quantity = requiredQuantity, work_order_id = workOrderId };
                db.OutboxEvents.Add(new OutboxEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = "stock.reserved",
                    EventKey = $"part:{partId}",
                    Payload = JsonSerializer.Serialize(outboxPayload),
                    Published = false,
                    CreatedAt = DateTime.UtcNow
                });
            }
            else
            {
                // OUT OF STOCK: Shortage detection
                int shortageQty = requiredQuantity - availableQty;

                db.Shortages.Add(new Shortage
                {
                    WorkOrderId = workOrderId,
                    BomLineId = bomLineId,
                    PartId = partId,
                    MissingQuantity = shortageQty,
                    Status = "open",
                    CreatedAt = DateTime.UtcNow
                });

                // Emit Outbox Event (stock.shortage_detected)
                var shortagePayload = new { part_id = partId, missing_quantity = shortageQty, work_order_id = workOrderId };
                db.OutboxEvents.Add(new OutboxEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = "stock.shortage_detected",
                    EventKey = $"part:{partId}",
                    Payload = JsonSerializer.Serialize(shortagePayload),
                    Published = false,
                    CreatedAt = DateTime.UtcNow
                });
            }

            // 4. Save and release lock
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Critical concurrency error reserving part {PartId}", partId);
        }
    }
    private async Task SavePendingEventAsync(AppDbContext db, Guid eventId, string eventType, string eventKey, string payload, string missingEntity)
    {
        var exists = await db.PendingEvents.AnyAsync(pe => pe.EventId == eventId);
        if (!exists)
        {
            db.PendingEvents.Add(new PendingEvent
            {
                EventId = eventId,
                EventType = eventType,
                EventKey = eventKey,
                Payload = payload,
                MissingEntity = missingEntity,
                Attempts = 0,
                Status = "pending",
                CreatedAt = DateTime.UtcNow,
                NextRetryAt = DateTime.UtcNow.AddSeconds(30)
            });
            await db.SaveChangesAsync();
            _logger.LogWarning("Event {EventId} saved as pending. Missing entity: {Entity}", eventId, missingEntity);
        }
    }

    private async Task ProcessInspectionApprovedAsync(string json)
    {
        var payload = JsonSerializer.Deserialize<InspectionApprovedPayload>(json);
        if (payload == null || string.IsNullOrWhiteSpace(payload.Sku)) return;

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (string.IsNullOrWhiteSpace(payload.Sku))
        {
            // Pieza sin identificar: registrar como faltante o enviar a revisión
            return;
        }
        // 1. Limpiar el SKU para evitar problemas de espacios o mayúsculas
        string cleanSku = payload.Sku.Trim().ToUpperInvariant();
        var part = await db.Parts.FirstOrDefaultAsync(p => p.Sku != null && p.Sku.ToUpper() == cleanSku);
        if (part == null)
        {
            // CASO DE PRUEBA "DESORDEN": Nos piden reservar algo que aún no existe en catálogo.
            // Lo guardamos en la tabla de pendientes para reintentarlo después.
            await SavePendingEventAsync(db, payload.EventId, "inspection.approved", payload.WorkOrderId.ToString(), json, "Part");
            return;
        }

        // 2. Si la pieza existe, llamamos al Jefe Final (Reserva Atómica)
        await ProcessAtomicReservationAsync(
            payload.WorkOrderId,
            payload.BomLineId,
            part.PartId,
            payload.RequiredQuantity
        );
    }
}
