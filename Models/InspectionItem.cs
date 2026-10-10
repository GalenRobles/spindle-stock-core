using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AlmacenTaller.Models;

[Table("inspection_items")]
[Index("BomLineId", Name = "idx_inspection_items_bom_line")]
[Index("InspectionId", Name = "idx_inspection_items_inspection")]
public partial class InspectionItem
{
    [Key]
    [Column("inspection_item_id")]
    public long InspectionItemId { get; set; }

    [Column("inspection_id")]
    public long InspectionId { get; set; }

    [Column("bom_line_id")]
    public long BomLineId { get; set; }

    [Column("part_id")]
    public long? PartId { get; set; }

    [Column("name", TypeName = "character varying")]
    public string Name { get; set; } = null!;

    [Column("group_name", TypeName = "character varying")]
    public string? GroupName { get; set; }

    [Column("condition", TypeName = "character varying")]
    public string? Condition { get; set; }

    [Column("action")]
    [StringLength(20)]
    public string? Action { get; set; }

    [Column("quantity")]
    public int? Quantity { get; set; }

    [Column("note", TypeName = "character varying")]
    public string? Note { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [ForeignKey("InspectionId")]
    [InverseProperty("InspectionItems")]
    public virtual Inspection Inspection { get; set; } = null!;

    [InverseProperty("InspectionItem")]
    public virtual ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
}
