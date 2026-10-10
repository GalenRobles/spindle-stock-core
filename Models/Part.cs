using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AlmacenTaller.Models;

[Table("parts")]
public class Part
{
    [Key]
    [Column("part_id")]
    public int PartId { get; set; }

    [Column("sku")]
    public string Sku { get; set; } = string.Empty;

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("family")]
    public string? Family { get; set; }

    [Column("part_group")]
    public string? PartGroup { get; set; }

    [Column("subgroup")]
    public string? Subgroup { get; set; }

    [Column("active")]
    public bool Active { get; set; } = true;
}