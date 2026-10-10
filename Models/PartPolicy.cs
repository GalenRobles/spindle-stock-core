using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AlmacenTaller.Models;

[Table("part_policies")]
public partial class PartPolicy
{
    [Key]
    [Column("part_id")]
    public long PartId { get; set; }

    [Column("min_qty")]
    public int MinQty { get; set; }

    [Column("max_qty")]
    public int? MaxQty { get; set; }

    [Column("reorder_qty")]
    public int? ReorderQty { get; set; }

    [Column("lead_time_days")]
    public int? LeadTimeDays { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [ForeignKey("PartId")]
    [InverseProperty("PartPolicy")]
    public virtual Part Part { get; set; } = null!;
}
