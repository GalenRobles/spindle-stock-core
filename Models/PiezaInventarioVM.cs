namespace AlmacenTaller.Models;

/// <summary>
/// Vista combinada de una pieza (parts) con sus saldos agregados (inventory_balances).
/// Es lo que consume Pages/Index.cshtml.
/// </summary>
public class PiezaInventarioVM
{
    public long PartId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Categoria { get; set; } = string.Empty;
    public int StockFisico { get; set; }
    public int Reservado { get; set; }
    public int StockMinimo { get; set; }
    public string TiempoReposicion { get; set; } = "N/D";

    // Misma regla de negocio que InventoryBalance.Available
    public int Disponible => StockFisico - Reservado;
}
