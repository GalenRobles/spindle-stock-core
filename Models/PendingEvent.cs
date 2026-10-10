using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AlmacenTaller.Models;

[Table("pending_events")]
[Index("Status", "NextRetryAt", Name = "idx_pending_retry")]
[Index("EventId", Name = "pending_events_event_id_key", IsUnique = true)]
public partial class PendingEvent
{
    [Key]
    [Column("pending_id")]
    public long PendingId { get; set; }

    [Column("event_id")]
    public Guid EventId { get; set; }

    [Column("event_type")]
    [StringLength(100)]
    public string EventType { get; set; } = null!;

    [Column("event_key")]
    [StringLength(200)]
    public string EventKey { get; set; } = null!;

    [Column("payload", TypeName = "jsonb")]
    public string Payload { get; set; } = null!;

    [Column("missing_entity")]
    [StringLength(50)]
    public string? MissingEntity { get; set; }

    [Column("attempts")]
    public int Attempts { get; set; }

    [Column("next_retry_at")]
    public DateTime NextRetryAt { get; set; }

    [Column("status")]
    [StringLength(20)]
    public string Status { get; set; } = null!;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("processed_at")]
    public DateTime? ProcessedAt { get; set; }
}
