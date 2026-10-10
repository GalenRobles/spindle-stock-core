using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AlmacenTaller.Models;

[Table("dead_letter_events")]
[Index("EventId", Name = "dead_letter_events_event_id_key", IsUnique = true)]
public partial class DeadLetterEvent
{
    [Key]
    [Column("dead_letter_id")]
    public long DeadLetterId { get; set; }

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

    [Column("error_message", TypeName = "character varying")]
    public string ErrorMessage { get; set; } = null!;

    [Column("attempts")]
    public int Attempts { get; set; }

    [Column("failed_at")]
    public DateTime FailedAt { get; set; }

    [Column("resolved")]
    public bool Resolved { get; set; }

    [Column("resolved_at")]
    public DateTime? ResolvedAt { get; set; }
}
