using AlmacenTaller.DataContext;
using AlmacenTaller.Messaging;
using AlmacenTaller.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlmacenTaller.Services.Handlers;

public class InspectionSubmittedHandler : IEventHandler
{
    private readonly AppDbContext _db;
    private readonly ILogger<InspectionSubmittedHandler> _logger;

    public IReadOnlyCollection<string> EventTypes => new[] { "inspection.submitted", "inspection_submitted" };

    public InspectionSubmittedHandler(AppDbContext db, ILogger<InspectionSubmittedHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        // Solo registramos la recepción de la inspección para trazabilidad
        _logger.LogInformation("Inspección sometida recibida para el evento {EventId}", envelope.EventId);
        await Task.CompletedTask;
    }
}