namespace AlmacenTaller.Models;
public class Pieza
{
    public int Id {  get; set; }
    public string Sku {  get; set; }= string.Empty;
    public string Nombre {  get; set; }= string.Empty;
    public string Categoria {  get; set; }= string.Empty;
    public int StockFisico {  get; set; }
    public int Reservado {  get; set; }
    public int StockMinimo {  get; set; }
    public string TiempoReposicion { get; set; } = "3 dias";

    public int Disponible => StockFisico - Reservado;
}