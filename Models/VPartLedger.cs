using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AlmacenTaller.Models;

[Keyless]
public partial class VPartLedger
{
    [Column("movement_id")]
    public long? MovementId { get; set; }

    [Column("part_id")]
    public long? PartId { get; set; }

    [Column("location_id")]
    public long? LocationId { get; set; }

    [Column("movement_type")]
    [StringLength(20)]
    public string? MovementType { get; set; }

    [Column("on_hand_delta")]
    public int? OnHandDelta { get; set; }

    [Column("reserved_delta")]
    public int? ReservedDelta { get; set; }

    [Column("work_order_id")]
    public long? WorkOrderId { get; set; }

    [Column("reservation_id")]
    public long? ReservationId { get; set; }

    [Column("purchase_line_id")]
    public long? PurchaseLineId { get; set; }

    [Column("transfer_id")]
    public Guid? TransferId { get; set; }

    [Column("actor", TypeName = "character varying")]
    public string? Actor { get; set; }

    [Column("reason", TypeName = "character varying")]
    public string? Reason { get; set; }

    [Column("source_event_id")]
    public Guid? SourceEventId { get; set; }

    [Column("dedupe_key", TypeName = "character varying")]
    public string? DedupeKey { get; set; }

    [Column("occurred_at")]
    public DateTime? OccurredAt { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("on_hand_running")]
    public long? OnHandRunning { get; set; }

    [Column("reserved_running")]
    public long? ReservedRunning { get; set; }
}
