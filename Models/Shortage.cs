using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AlmacenTaller.Models;

[Table("shortages")]
[Index("InspectionId", "Status", Name = "idx_shortages_inspection")]
[Index("WorkOrderId", "Status", Name = "idx_shortages_work_order")]
public partial class Shortage
{
    [Key]
    [Column("shortage_id")]
    public long ShortageId { get; set; }

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

    [Column("missing_quantity")]
    public int? MissingQuantity { get; set; }

    [Column("covered_quantity")]
    public int CoveredQuantity { get; set; }

    [Column("status")]
    [StringLength(20)]
    public string Status { get; set; } = null!;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("resolved_at")]
    public DateTime? ResolvedAt { get; set; }

    [ForeignKey("WorkOrderId")]
    [InverseProperty("Shortages")]
    public virtual WorkOrder WorkOrder { get; set; } = null!;
}
