using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AlmacenTaller.Models;

[Table("outbox_events")]
[Index("EventId", Name = "outbox_events_event_id_key", IsUnique = true)]
public partial class OutboxEvent
{
    [Key]
    [Column("outbox_id")]
    public long OutboxId { get; set; }

    [Column("event_id")]
    public Guid EventId { get; set; }

    [Column("event_type")]
    [StringLength(100)]
    public string EventType { get; set; } = null!;

    [Column("event_version")]
    public int EventVersion { get; set; }

    [Column("occurred_at")]
    public DateTime OccurredAt { get; set; }

    [Column("event_key")]
    [StringLength(200)]
    public string EventKey { get; set; } = null!;

    [Column("payload", TypeName = "jsonb")]
    public string Payload { get; set; } = null!;

    [Column("published")]
    public bool Published { get; set; }

    [Column("published_at")]
    public DateTime? PublishedAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
