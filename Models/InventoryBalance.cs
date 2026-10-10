namespace AlmacenTaller.Models
{
    public class InventoryBalance
    {
        public long PartId { get; set; }
        public long LocationId { get; set; }
        public int OnHand { get; set; }
        public int Reserved { get; set; }
        public DateTime UpdatedAt { get; set; }

        // LÓGICA DE NEGOCIO: Cálculo al vuelo del disponible
        public int Available => OnHand - Reserved;
    }
}
