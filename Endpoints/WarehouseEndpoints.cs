using AlmacenTaller.DataContext;
using AlmacenTaller.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AlmacenTaller.Endpoints;

public static class WarehouseEndpoints
{
    public static void MapWarehouseEndpoints(this IEndpointRouteBuilder app)
    {
        // 1. Disponibilidad de piezas
        app.MapGet("/parts/{partId:int}/availability", async (int partId, AppDbContext db) =>
        {
            var balances = await db.InventoryBalances
                .Where(b => b.PartId == partId)
                .Select(b => new
                {
                    location_id = b.LocationId,
                    on_hand = b.OnHand
                })
                .ToListAsync();

            return Results.Ok(new { locations = balances });
        });

        // 2. Salida de material (Issues)
        app.MapPost("/issues", async (IssueRequest req, AppDbContext db) =>
        {
            var balance = await db.InventoryBalances
                .FirstOrDefaultAsync(b => b.PartId == req.part_id && b.LocationId == req.location_id);

            // Regla: Si no hay saldo suficiente, devolver 409 Conflict
            if (balance == null || balance.OnHand < req.quantity)
            {
                return Results.StatusCode(409);
            }

            // Descontar inventario
            balance.OnHand -= req.quantity;

            // Registrar movimiento en el libro mayor
            var movement = new StockMovement
            {
                PartId = req.part_id,
                LocationId = req.location_id,
                MovementType = "issue",
                Quantity = -req.quantity,
                ReferenceId = req.work_order_code,
                CreatedAt = DateTime.UtcNow
            };
            db.StockMovements.Add(movement);

            // Registrar evento de salida en el Outbox para que KafkaProducerService lo publique
            var payload = new
            {
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
                OccurredAt = DateTime.UtcNow,
                EventKey = req.work_order_code,
                Payload = JsonSerializer.Serialize(payload),
                Published = false
            });

            await db.SaveChangesAsync();

            return Results.StatusCode(201);
        });

        // 3. Kardex / Libro Mayor
        app.MapGet("/parts/{partId:int}/ledger", async (int partId, AppDbContext db) =>
        {
            var movements = await db.StockMovements
                .Where(m => m.PartId == partId)
                .OrderBy(m => m.CreatedAt)
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

            // Orden descendente (más reciente primero)
            entries.Reverse();

            return Results.Ok(new { entries });
        });
    }
}

public record IssueRequest(string work_order_code, int part_id, int location_id, int quantity, string issued_by);