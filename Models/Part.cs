using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AlmacenTaller.Models;

[Table("parts")]
public class Part
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)] // el ID viene del sistema del taller
    [Column("part_id")]
    public int PartId { get; set; }

    [Column("sku")]
    public string? Sku { get; set; } // puede ser NULL en el catálogo

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("family")]
    public string? Family { get; set; }

    [Column("part_group")]
    public string? PartGroup { get; set; }

    [Column("subgroup")]
    public string? Subgroup { get; set; }

    [Column("unit")]
    public string Unit { get; set; } = string.Empty; // columna NOT NULL; en el evento puede venir null

    [Column("active")]
    public bool Active { get; set; } = true;
}