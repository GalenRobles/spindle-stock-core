using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AlmacenTaller.Models;

[Table("parts")]
[Index("Active", Name = "idx_parts_active")]
[Index("SkuNorm", Name = "idx_parts_sku_norm")]
public partial class Parts
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)] // el ID viene del sistema del taller
    [Column("part_id")]
    public long PartId { get; set; }

    [Column("sku", TypeName = "character varying")]
    public string? Sku { get; set; }

    [Column("sku_norm", TypeName = "character varying")]
    public string? SkuNorm { get; set; }

    [Column("name", TypeName = "character varying")]
    public string Name { get; set; } = null!;

    [Column("family", TypeName = "character varying")]
    public string? Family { get; set; }

    [Column("part_group", TypeName = "character varying")]
    public string? PartGroup { get; set; }

    [Column("subgroup", TypeName = "character varying")]
    public string? Subgroup { get; set; }

    [Column("unit_id")]
    public long? UnitId { get; set; }

    [Column("unit_name", TypeName = "character varying")]
    public string? UnitName { get; set; }

    [Column("image_url", TypeName = "character varying")]
    public string? ImageUrl { get; set; }

    [Column("active")]
    public bool Active { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [InverseProperty("Part")]
    public virtual ICollection<InventoryBalance> InventoryBalances { get; set; } = new List<InventoryBalance>();

    [InverseProperty("Part")]
    public virtual ICollection<InventoryCount> InventoryCounts { get; set; } = new List<InventoryCount>();

    [InverseProperty("Part")]
    public virtual ICollection<InventoryMovement> InventoryMovements { get; set; } = new List<InventoryMovement>();

    [InverseProperty("Part")]
    public virtual PartPolicy? PartPolicy { get; set; }

    [InverseProperty("MatchedPart")]
    public virtual ICollection<PurchaseReceipt> PurchaseReceipts { get; set; } = new List<PurchaseReceipt>();

    [InverseProperty("Part")]
    public virtual ReorderSuggestion? ReorderSuggestion { get; set; }

    [InverseProperty("Part")]
    public virtual ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();

    [InverseProperty("ResolvedPart")]
    public virtual ICollection<UnmatchedReceipt> UnmatchedReceipts { get; set; } = new List<UnmatchedReceipt>();
}
