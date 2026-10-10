using AlmacenTaller.DataContext;
using AlmacenTaller.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AlmacenTaller.Endpoints;

public static class WarehouseEndpoints
{
    public static void MapWarehouseEndpoints(this IEndpointRouteBuilder app)
    {
        // 1. GET /parts/{partId}/availability
        app.MapGet("/parts/{partId}/availability", async (int partId, AppDbContext db) =>
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

        // 2. POST /issues
        app.MapPost("/issues", async (IssueRequest req, AppDbContext db, IKafkaPublisher kafkaPublisher) =>
        {
            // Validar saldo en InventoryBalances
            var balance = await db.InventoryBalances
                .FirstOrDefaultAsync(b => b.PartId == req.part_id && b.LocationId == req.location_id);

            if (balance == null || balance.OnHand < req.quantity)
            {
                return Results.StatusCode(409); // Conflicto: Saldo insuficiente o negativo
            }

            // Restar quantity al balance
            balance.OnHand -= req.quantity;

            // Insertar en StockMovements utilizando las propiedades exactas de tu modelo
            var movement = new StockMovement
            {
                PartId = req.part_id,
                LocationId = req.location_id, 
                MovementType = "issue",          // Propiedad ajustada a MovementType
                Quantity = -req.quantity,        // Cantidad negativa para la salida del balance
                ReferenceId = req.work_order_code, // Ideal para guardar el work_order_code
                CreatedAt = DateTime.UtcNow
            };
            
            db.StockMovements.Add(movement);
            
            await db.SaveChangesAsync();

            // Emitir evento a Kafka
            var eventPayload = JsonSerializer.Serialize(req);
            await kafkaPublisher.PublishAsync("stock.issued", req.work_order_code, eventPayload);

            return Results.StatusCode(201);
        });

        // 3. GET /parts/{partId}/ledger
        app.MapGet("/parts/{partId}/ledger", async (int partId, AppDbContext db) =>
        {
            // Ordenar ascendente primero para calcular el saldo corrido matemático correctamente
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
                    type = m.MovementType, // Mapeado desde MovementType
                    quantity = m.Quantity,
                    balance = currentBalance
                });
            }

            // Invertir para cumplir con OrderByDescending en la respuesta final
            entries.Reverse();

            return Results.Ok(new { entries });
        });
    }
}

// Registro adaptado con int en lugar de long para empatar con StockMovement
public record IssueRequest(string work_order_code, int part_id, int location_id, int quantity, string issued_by);

// Interfaz representativa de Kafka
public interface IKafkaPublisher
{
    Task PublishAsync(string topic, string key, string message);
}