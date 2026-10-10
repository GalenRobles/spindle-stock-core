using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AlmacenTaller.Models;

[Table("bom_lines")]
[Index("ModelId", "KitKey", Name = "idx_bom_lines_kit")]
[Index("PartId", Name = "idx_bom_lines_part")]
public partial class BomLine
{
    [Key]
    [Column("bom_line_id")]
    public long BomLineId { get; set; }

    [Column("model_id")]
    public long ModelId { get; set; }

    [Column("part_id")]
    public long? PartId { get; set; }

    [Column("name", TypeName = "character varying")]
    public string Name { get; set; } = null!;

    [Column("group_name", TypeName = "character varying")]
    public string? GroupName { get; set; }

    [Column("unit_id")]
    public long? UnitId { get; set; }

    [Column("unit_name", TypeName = "character varying")]
    public string? UnitName { get; set; }

    [Column("qty_per_unit")]
    public int? QtyPerUnit { get; set; }

    [Column("sort_order")]
    public int SortOrder { get; set; }

    [Column("removed_at")]
    public DateTime? RemovedAt { get; set; }

    [Column("kit_key", TypeName = "character varying")]
    public string? KitKey { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [ForeignKey("ModelId")]
    [InverseProperty("BomLines")]
    public virtual EquipmentModel Model { get; set; } = null!;
}
