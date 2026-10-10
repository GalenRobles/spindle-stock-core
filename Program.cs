using AlmacenTaller.DataContext;
using AlmacenTaller.Endpoints;
using AlmacenTaller.Hubs;
using AlmacenTaller.Messaging;
using AlmacenTaller.Services;
using AlmacenTaller.Services.Handlers;
using Microsoft.EntityFrameworkCore;
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
var builder = WebApplication.CreateBuilder(args);

// Conexión a PostgreSQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// Productor de Kafka (Outbox u hosted service  
// Servicios web, tiempo real (SignalR) y consumidor de Kafka
builder.Services.AddRazorPages();
builder.Services.AddSignalR();
builder.Services.AddHostedService<KafkaProducerService>();
builder.Services.AddHostedService<KafkaConsumerService>();

// Procesamiento de eventos por handlers
builder.Services.AddSingleton<EventStore>();
builder.Services.AddScoped<IEventHandler, PartUpsertedHandler>();
builder.Services.AddScoped<IEventHandler, LocationUpsertedHandler>();
builder.Services.AddScoped<IEventHandler, BomUpsertedHandler>();
builder.Services.AddScoped<IEventHandler, PurchaseItemReceivedHandler>();    
builder.Services.AddScoped<IEventHandler, WorkOrderOpenedHandler>();
builder.Services.AddScoped<IEventHandler, InspectionApprovedHandler>();
builder.Services.AddScoped<IEventHandler, InspectionSubmittedHandler>();
builder.Services.AddScoped<IEventHandler, InspectionRejectedHandler>();
builder.Services.AddScoped<IEventHandler, InspectionVoidedHandler>();
builder.Services.AddScoped<IEventHandler, WorkOrderDeletedHandler>();
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
app.MapTransferCountEndpoints();

// Mapeo de rutas existentes y SignalR
app.MapRazorPages();
app.MapHub<InventarioHub>("/inventarioHub");

// Puerto obligatorio para el evaluador de tests
app.Run("http://0.0.0.0:5012");