namespace AlmacenTaller.Models;

public class Part
{
    public long PartId { get; set; }
    public string Sku { get; set; } // Puede ser nulo según el script
    public string Name { get; set; }
    public string Family { get; set; }
    public string PartGroup { get; set; }
    public string Subgroup { get; set; }
    public string Unit { get; set; }
    public string ImageUrl { get; set; }
    public bool Active { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}