using AlmacenTaller.DataContext;
using AlmacenTaller.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AlmacenTaller.Endpoints;

public static class WarehouseEndpoints
{
    public static void MapWarehouseEndpoints(this IEndpointRouteBuilder app)
    {
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

        app.MapPost("/issues", async (IssueRequest req, AppDbContext db, IKafkaPublisher kafkaPublisher) =>
        {
            var balance = await db.InventoryBalances
                .FirstOrDefaultAsync(b => b.PartId == req.part_id && b.LocationId == req.location_id);

            if (balance == null || balance.OnHand < req.quantity)
            {
                return Results.StatusCode(409);
            }

            balance.OnHand -= req.quantity;

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
            
            await db.SaveChangesAsync();

            var eventPayload = JsonSerializer.Serialize(req);
            await kafkaPublisher.PublishAsync("stock.issued", req.work_order_code, eventPayload);

            return Results.StatusCode(201);
        });

        app.MapGet("/parts/{partId}/ledger", async (int partId, AppDbContext db) =>
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

            entries.Reverse();

            return Results.Ok(new { entries });
        });
    }
}

public record IssueRequest(string work_order_code, int part_id, int location_id, int quantity, string issued_by);

public interface IKafkaPublisher
{
    Task PublishAsync(string topic, string key, string message);
}