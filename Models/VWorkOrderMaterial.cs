using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AlmacenTaller.Models;

[Keyless]
public partial class VWorkOrderMaterial
{
    [Column("work_order_code")]
    [StringLength(50)]
    public string? WorkOrderCode { get; set; }

    [Column("work_order_id")]
    public long? WorkOrderId { get; set; }

    [Column("need_id")]
    public long? NeedId { get; set; }

    [Column("inspection_item_id")]
    public long? InspectionItemId { get; set; }

    [Column("bom_line_id")]
    public long? BomLineId { get; set; }

    [Column("part_id")]
    public long? PartId { get; set; }

    [Column("name", TypeName = "character varying")]
    public string? Name { get; set; }

    [Column("required_qty")]
    public int? RequiredQty { get; set; }

    [Column("need_status")]
    [StringLength(20)]
    public string? NeedStatus { get; set; }

    [Column("reserved_qty")]
    public long? ReservedQty { get; set; }

    [Column("delivered_qty")]
    public long? DeliveredQty { get; set; }

    [Column("shortage_qty")]
    public long? ShortageQty { get; set; }

    [Column("shortage_unknown")]
    public bool? ShortageUnknown { get; set; }
}
