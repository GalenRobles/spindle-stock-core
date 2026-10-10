using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AlmacenTaller.Models;

[PrimaryKey("PartId", "LocationId")]
[Table("inventory_balances")]
[Index("LocationId", Name = "idx_balances_location")]
public partial class InventoryBalance
{
    [Key]
    [Column("part_id")]
    public long PartId { get; set; }

    [Key]
    [Column("location_id")]
    public long LocationId { get; set; }

    [Column("on_hand")]
    public int OnHand { get; set; }

    [Column("reserved")]
    public int Reserved { get; set; }

    [Column("available")]
    public int? Available { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [ForeignKey("LocationId")]
    [InverseProperty("InventoryBalances")]
    public virtual Location Location { get; set; } = null!;

    [ForeignKey("PartId")]
    [InverseProperty("InventoryBalances")]
    public virtual Part Part { get; set; } = null!;
}
