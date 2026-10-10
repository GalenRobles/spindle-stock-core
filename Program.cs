using AlmacenTaller.DataContext;
using AlmacenTaller.Endpoints;
using AlmacenTaller.Hubs;
using AlmacenTaller.Messaging;
using AlmacenTaller.Services;
using AlmacenTaller.Services.Handlers;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Conexión a PostgreSQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// Productor de Kafka (Outbox u hosted service)
builder.Services.AddHostedService<KafkaProducerService>();

// Servicios web, tiempo real (SignalR) y consumidor de Kafka
builder.Services.AddRazorPages();
builder.Services.AddSignalR();
builder.Services.AddHostedService<KafkaConsumerService>();

// Procesamiento de eventos por handlers
builder.Services.AddSingleton<EventStore>();
builder.Services.AddScoped<IEventHandler, PartUpsertedHandler>();
builder.Services.AddScoped<IEventHandler, LocationUpsertedHandler>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

// Endpoint de salud
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// Rutas REST para los tests de contrato (a implementar por el rol de BD)
app.MapWarehouseEndpoints();

// Mapeo de rutas existentes y SignalR
app.MapRazorPages();
app.MapHub<InventarioHub>("/inventarioHub");

// Puerto obligatorio para el evaluador de tests
app.Run("http://0.0.0.0:5012");