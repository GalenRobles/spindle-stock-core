namespace AlmacenTaller.Models
{
    public class Reservation
    {
        public long ReservationId { get; set; }
        public long WorkOrderId { get; set; }
        public long BomLineId { get; set; }
        public long PartId { get; set; }
        public long LocationId { get; set; }
        public int Quantity { get; set; }
        public int FulfilledQty { get; set; }
        public string Status { get; set; } // 'active', 'fulfilled', 'released'
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
