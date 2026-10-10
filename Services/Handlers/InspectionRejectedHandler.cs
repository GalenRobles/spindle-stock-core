using AlmacenTaller.DataContext;
using AlmacenTaller.Messaging;
using Microsoft.Extensions.Logging;

namespace AlmacenTaller.Services.Handlers;

public class InspectionRejectedHandler : IEventHandler
{
    private readonly AppDbContext _db;
    private readonly ILogger<InspectionRejectedHandler> _logger;

    public IReadOnlyCollection<string> EventTypes => new[] { "inspection.rejected", "inspection_rejected" };

    public InspectionRejectedHandler(AppDbContext db, ILogger<InspectionRejectedHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        _logger.LogInformation("Inspección rechazada procesada correctamente. No se generan reservas.");
        await Task.CompletedTask;
    }
}