using AlmacenTaller.DataContext;
using AlmacenTaller.Hubs;
using AlmacenTaller.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Conexión a PostgreSQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// Servicios web, tiempo real (SignalR) y consumidor de Kafka
builder.Services.AddRazorPages();
builder.Services.AddSignalR();
builder.Services.AddHostedService<KafkaConsumerService>();

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