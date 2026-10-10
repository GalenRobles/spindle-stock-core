using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AlmacenTaller.Models;

[Table("inspections")]
[Index("WorkOrderId", "OccurredAt", Name = "idx_inspections_work_order")]
public partial class Inspection
{
    [Key]
    [Column("inspection_id")]
    public long InspectionId { get; set; }

    [Column("work_order_id")]
    public long WorkOrderId { get; set; }

    [Column("kind")]
    [StringLength(20)]
    public string Kind { get; set; } = null!;

    [Column("status")]
    [StringLength(20)]
    public string Status { get; set; } = null!;

    [Column("approved_by")]
    public long? ApprovedBy { get; set; }

    [Column("approved_at")]
    public DateTime? ApprovedAt { get; set; }

    [Column("voided_at")]
    public DateTime? VoidedAt { get; set; }

    [Column("reason", TypeName = "character varying")]
    public string? Reason { get; set; }

    [Column("occurred_at")]
    public DateTime OccurredAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [InverseProperty("Inspection")]
    public virtual ICollection<InspectionItem> InspectionItems { get; set; } = new List<InspectionItem>();

    [InverseProperty("Inspection")]
    public virtual ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
}
