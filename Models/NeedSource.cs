using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AlmacenTaller.Models;

[PrimaryKey("NeedId", "InspectionItemId")]
[Table("need_sources")]
[Index("InspectionId", Name = "idx_need_sources_inspection")]
public partial class NeedSource
{
    [Key]
    [Column("need_id")]
    public long NeedId { get; set; }

    [Key]
    [Column("inspection_item_id")]
    public long InspectionItemId { get; set; }

    [Column("inspection_id")]
    public long InspectionId { get; set; }

    [Column("required_qty")]
    public int? RequiredQty { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [ForeignKey("NeedId")]
    [InverseProperty("NeedSources")]
    public virtual WorkOrderNeed Need { get; set; } = null!;
}
