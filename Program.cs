using AlmacenTaller.DataContext;
using AlmacenTaller.Hubs;
using AlmacenTaller.Messaging;
using AlmacenTaller.Services;
using AlmacenTaller.Services.Handlers;
using Microsoft.EntityFrameworkCore;


// Registramos el productor de Kafka para que lea la tabla Outbox y publique los eventos
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHostedService<KafkaProducerService>();


// Conexión a PostgreSQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// Servicios web, tiempo real (SignalR) y consumidor de Kafka
builder.Services.AddRazorPages();
builder.Services.AddSignalR();
builder.Services.AddHostedService<KafkaConsumerService>();

// Procesamiento de eventos: control (processed/pending/dead-letter) y un handler por tipo de evento.
// Para agregar uno nuevo: implementar IEventHandler y registrarlo aquí.
builder.Services.AddSingleton<EventStore>();
builder.Services.AddScoped<IEventHandler, PartUpsertedHandler>();
builder.Services.AddScoped<IEventHandler, LocationUpsertedHandler>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

// app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

// Mapeo de rutas y Hub de SignalR
app.MapRazorPages();
app.MapHub<InventarioHub>("/inventarioHub");

app.Run();