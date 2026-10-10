using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AlmacenTaller.Models;

[Table("reservations")]
[Index("InspectionId", "Status", Name = "idx_reservations_inspection")]
[Index("PartId", "LocationId", "Status", Name = "idx_reservations_part")]
[Index("WorkOrderId", "Status", Name = "idx_reservations_work_order")]
public partial class Reservation
{
    [Key]
    [Column("reservation_id")]
    public long ReservationId { get; set; }

    [Column("work_order_id")]
    public long WorkOrderId { get; set; }

    [Column("bom_line_id")]
    public long BomLineId { get; set; }

    [Column("part_id")]
    public long PartId { get; set; }

    [Column("location_id")]
    public long LocationId { get; set; }

    [Column("inspection_id")]
    public long InspectionId { get; set; }

    [Column("inspection_item_id")]
    public long InspectionItemId { get; set; }

    [Column("quantity")]
    public int Quantity { get; set; }

    [Column("fulfilled_qty")]
    public int FulfilledQty { get; set; }

    [Column("status")]
    [StringLength(20)]
    public string Status { get; set; } = null!;

    [Column("cancelled_at")]
    public DateTime? CancelledAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [ForeignKey("InspectionId")]
    [InverseProperty("Reservations")]
    public virtual Inspection Inspection { get; set; } = null!;

    [ForeignKey("InspectionItemId")]
    [InverseProperty("Reservations")]
    public virtual InspectionItem InspectionItem { get; set; } = null!;

    [ForeignKey("LocationId")]
    [InverseProperty("Reservations")]
    public virtual Location Location { get; set; } = null!;

    [ForeignKey("PartId")]
    [InverseProperty("Reservations")]
    public virtual Part Part { get; set; } = null!;

    [ForeignKey("WorkOrderId")]
    [InverseProperty("Reservations")]
    public virtual WorkOrder WorkOrder { get; set; } = null!;
}
