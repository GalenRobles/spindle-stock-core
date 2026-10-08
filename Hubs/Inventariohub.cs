using Microsoft.AspNetCore.SignalR;

namespace AlmacenTaller.Hubs;

public class InventarioHub : Hub
{
    // Métodos para broadcast en vivo cuando cambie el inventario
    public async Task NotificarCambioStock(string mensaje)
    {
        await Clients.All.SendAsync("RecibirActualizacion", mensaje);
    }
}