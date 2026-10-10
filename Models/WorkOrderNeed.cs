using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AlmacenTaller.Models;

[Table("work_order_needs")]
[Index("PartId", "Status", Name = "idx_needs_active_part")]
[Index("WorkOrderId", "Status", Name = "idx_needs_work_order")]
[Index("WorkOrderId", "BomLineId", Name = "work_order_needs_work_order_id_bom_line_id_key", IsUnique = true)]
public partial class WorkOrderNeed
{
    [Key]
    [Column("need_id")]
    public long NeedId { get; set; }

    [Column("work_order_id")]
    public long WorkOrderId { get; set; }

    [Column("bom_line_id")]
    public long BomLineId { get; set; }

    [Column("part_id")]
    public long? PartId { get; set; }

    [Column("name", TypeName = "character varying")]
    public string Name { get; set; } = null!;

    [Column("inspection_id")]
    public long InspectionId { get; set; }

    [Column("inspection_item_id")]
    public long InspectionItemId { get; set; }

    [Column("required_qty")]
    public int? RequiredQty { get; set; }

    [Column("status")]
    [StringLength(20)]
    public string Status { get; set; } = null!;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [InverseProperty("Need")]
    public virtual ICollection<NeedSource> NeedSources { get; set; } = new List<NeedSource>();

    [ForeignKey("WorkOrderId")]
    [InverseProperty("WorkOrderNeeds")]
    public virtual WorkOrder WorkOrder { get; set; } = null!;
}
