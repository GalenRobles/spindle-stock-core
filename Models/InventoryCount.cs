using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AlmacenTaller.Models;

[Table("inventory_counts")]
[Index("PartId", "LocationId", "CountedAt", Name = "idx_counts_part_location", IsDescending = new[] { false, false, true })]
public partial class InventoryCount
{
    [Key]
    [Column("count_id")]
    public long CountId { get; set; }

    [Column("part_id")]
    public long PartId { get; set; }

    [Column("location_id")]
    public long LocationId { get; set; }

    [Column("expected_qty")]
    public int ExpectedQty { get; set; }

    [Column("counted_qty")]
    public int CountedQty { get; set; }

    [Column("difference_qty")]
    public int? DifferenceQty { get; set; }

    [Column("reason", TypeName = "character varying")]
    public string Reason { get; set; } = null!;

    [Column("counted_by", TypeName = "character varying")]
    public string? CountedBy { get; set; }

    [Column("movement_id")]
    public long? MovementId { get; set; }

    [Column("counted_at")]
    public DateTime CountedAt { get; set; }

    [ForeignKey("LocationId")]
    [InverseProperty("InventoryCounts")]
    public virtual Location Location { get; set; } = null!;

    [ForeignKey("MovementId")]
    [InverseProperty("InventoryCounts")]
    public virtual InventoryMovement? Movement { get; set; }

    [ForeignKey("PartId")]
    [InverseProperty("InventoryCounts")]
    public virtual Parts Part { get; set; } = null!;
}
