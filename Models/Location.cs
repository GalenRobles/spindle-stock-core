namespace AlmacenTaller.Models
{
    public class Location
    {
        public long LocationId { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public bool IsWorkbench { get; set; }
        public bool Active { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
