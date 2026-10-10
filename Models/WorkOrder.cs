namespace AlmacenTaller.Models
{
    public class WorkOrder
    {
        public long WorkOrderId { get; set; }
        public string Code { get; set; }
        public long ModelId { get; set; }
        public string Department { get; set; }
        public string Stage { get; set; }
        public DateTime ReceivedAt { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; } // Nullable
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
