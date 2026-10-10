using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AlmacenTaller.Models;

[Table("processed_events")]
[Index("ProcessedAt", Name = "idx_processed_events_date")]
public partial class ProcessedEvent
{
    [Key]
    [Column("event_id")]
    public Guid EventId { get; set; }

    [Column("event_type")]
    [StringLength(100)]
    public string EventType { get; set; } = null!;

    [Column("occurred_at")]
    public DateTime OccurredAt { get; set; }

    [Column("processed_at")]
    public DateTime ProcessedAt { get; set; }

    [Column("result")]
    [StringLength(20)]
    public string Result { get; set; } = null!;
}
