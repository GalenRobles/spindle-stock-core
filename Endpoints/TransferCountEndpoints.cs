using AlmacenTaller.DataContext;
using AlmacenTaller.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AlmacenTaller.Endpoints;

public static class TransferCountEndpoints
{
    public static void MapTransferCountEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapPost("/transfers",
            async (TransferRequest req, AppDbContext db) =>
            {
                if (req.quantity <= 0 ||
                    req.from_location_id == req.to_location_id)
                    return Results.StatusCode(422);

                await using var transaction =
                    await db.Database.BeginTransactionAsync(
                        System.Data.IsolationLevel.Serializable);

                var source = await db.InventoryBalances
                    .FirstOrDefaultAsync(b =>
                        b.PartId == req.part_id &&
                        b.LocationId == req.from_location_id);

                // Solo se transfiere existencia disponible.
                if (source == null ||
                    source.OnHand - source.Reserved < req.quantity)
                    return Results.StatusCode(409);

                if (!await db.Locations.AnyAsync(l =>
                        l.LocationId == req.to_location_id))
                    return Results.NotFound();

                var destination = await db.InventoryBalances
                    .FirstOrDefaultAsync(b =>
                        b.PartId == req.part_id &&
                        b.LocationId == req.to_location_id);

                if (destination == null)
                {
                    destination = new InventoryBalance
                    {
                        PartId = req.part_id,
                        LocationId = req.to_location_id,
                        OnHand = 0,
                        Reserved = 0
                    };
                    db.InventoryBalances.Add(destination);
                }

                var now = DateTime.UtcNow;
                var reference = Guid.NewGuid().ToString();

                source.OnHand -= req.quantity;
                destination.OnHand += req.quantity;
                source.UpdatedAt = now;
                destination.UpdatedAt = now;

                db.StockMovements.AddRange(
                    new StockMovement
                    {
                        PartId = req.part_id,
                        LocationId = req.from_location_id,
                        Quantity = -req.quantity,
                        MovementType = "transfer_out",
                        ReferenceId = reference,
                        CreatedAt = now
                    },
                    new StockMovement
                    {
                        PartId = req.part_id,
                        LocationId = req.to_location_id,
                        Quantity = req.quantity,
                        MovementType = "transfer_in",
                        ReferenceId = reference,
                        CreatedAt = now
                    });

                AddEvent(db, "stock.transferred", req.part_id, new
                {
                    part_id = req.part_id,
                    from_location_id = req.from_location_id,
                    to_location_id = req.to_location_id,
                    quantity = req.quantity
                });

                await db.SaveChangesAsync();
                await transaction.CommitAsync();

                return Results.StatusCode(201);
            });

        app.MapPost("/counts",
            async (CountRequest req, AppDbContext db) =>
            {
                if (string.IsNullOrWhiteSpace(req.reason) ||
                    req.lines == null ||
                    req.lines.Length == 0 ||
                    req.lines.Any(l => l.counted_quantity < 0) ||
                    req.lines.Select(l => l.part_id).Distinct().Count()
                        != req.lines.Length)
                    return Results.StatusCode(422);

                await using var transaction =
                    await db.Database.BeginTransactionAsync(
                        System.Data.IsolationLevel.Serializable);

                if (!await db.Locations.AnyAsync(l =>
                        l.LocationId == req.location_id))
                    return Results.NotFound();

                var adjustments = new List<object>();

                foreach (var line in req.lines)
                {
                    if (!await db.Parts.AnyAsync(p =>
                            p.PartId == line.part_id))
                        return Results.NotFound();

                    var balance = await db.InventoryBalances
                        .FirstOrDefaultAsync(b =>
                            b.PartId == line.part_id &&
                            b.LocationId == req.location_id);

                    if (line.counted_quantity < (balance?.Reserved ?? 0))
                        return Results.StatusCode(409);

                    var delta =
                        line.counted_quantity - (balance?.OnHand ?? 0);

                    if (delta == 0)
                        continue;

                    if (balance == null)
                    {
                        balance = new InventoryBalance
                        {
                            PartId = line.part_id,
                            LocationId = req.location_id,
                            Reserved = 0
                        };
                        db.InventoryBalances.Add(balance);
                    }

                    balance.OnHand = line.counted_quantity;
                    balance.UpdatedAt = DateTime.UtcNow;

                    db.StockMovements.Add(new StockMovement
                    {
                        PartId = line.part_id,
                        LocationId = req.location_id,
                        Quantity = delta,
                        MovementType = "adjustment",
                        ReferenceId = req.reason,
                        CreatedAt = DateTime.UtcNow
                    });

                    AddEvent(db, "stock.adjusted", line.part_id, new
                    {
                        part_id = line.part_id,
                        location_id = req.location_id,
                        delta,
                        reason = req.reason
                    });

                    adjustments.Add(new
                    {
                        part_id = line.part_id,
                        delta
                    });
                }

                await db.SaveChangesAsync();
                await transaction.CommitAsync();

                return Results.Json(
                    new { adjustments }, statusCode: 201);
            });
    }

    private static void AddEvent(
        AppDbContext db, string type, int partId, object data)
    {
        db.OutboxEvents.Add(new OutboxEvent
        {
            EventId = Guid.NewGuid(),
            EventType = type,
            EventVersion = 1,
            OccurredAt = DateTime.UtcNow,
            EventKey = $"part:{partId}",
            Payload = JsonSerializer.Serialize(data),
            Published = false
        });
    }
}

public record TransferRequest(
    int part_id,
    int from_location_id,
    int to_location_id,
    int quantity);

public record CountRequest(
    int location_id,
    string reason,
    CountLine[] lines);

public record CountLine(
    int part_id,
    int counted_quantity);