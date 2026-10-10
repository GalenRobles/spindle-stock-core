using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AlmacenTaller.Models;

[Table("stock_movements")]
[Index("EventId", Name = "stock_movements_event_id_key", IsUnique = true)]
public partial class StockMovement
{
    [Key]
    [Column("movement_id")]
    public int MovementId { get; set; }

    [Column("event_id")]
    [StringLength(100)]
    public string? EventId { get; set; }

    [Column("part_id")]
    public int PartId { get; set; }

    [Column("location_id")]
    public int LocationId { get; set; }

    [Column("quantity")]
    public int Quantity { get; set; }

    [Column("unit_cost")]
    [Precision(12, 4)]
    public decimal? UnitCost { get; set; }

    [Column("movement_type")]
    [StringLength(50)]
    public string MovementType { get; set; } = null!;

    [Column("reference_id")]
    [StringLength(100)]
    public string? ReferenceId { get; set; }

    [Column("created_at", TypeName = "timestamp without time zone")]
    public DateTime? CreatedAt { get; set; }
}
