namespace AlmacenTaller.Messaging;

/// <summary>
/// Un handler procesa uno o más tipos de evento (event_type).
/// Para agregar uno nuevo: implementar esta interfaz y registrarla en Program.cs con
/// builder.Services.AddScoped&lt;IEventHandler, MiHandler&gt;();
///
/// Reglas:
///  - Debe ser idempotente (el consumidor ya deduplica por event_id, pero un fallo entre el
///    handler y el registro en processed_events puede repetirlo).
///  - Si falta algo que aún no llegó (pieza, orden, línea de BOM): lanzar MissingDependencyException
///    para que el evento se guarde en pending_events y no se pierda (regla 20).
///  - Cualquier otra excepción se reintenta 3 veces y después va a inventory.dlq (regla 21).
/// </summary>
public interface IEventHandler
{
    IReadOnlyCollection<string> EventTypes { get; }

    Task HandleAsync(EventEnvelope envelope, CancellationToken ct);
}

/// <summary>Lo que el evento referencia todavía no existe; hay que reintentarlo después.</summary>
public class MissingDependencyException : Exception
{
    public string Entity { get; }

    public MissingDependencyException(string entity, string message) : base(message)
    {
        Entity = entity;
    }
}
