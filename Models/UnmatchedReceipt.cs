using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AlmacenTaller.Models;

[Table("unmatched_receipts")]
[Index("Status", "CreatedAt", Name = "idx_unmatched_open")]
[Index("LineId", Name = "unmatched_receipts_line_id_key", IsUnique = true)]
public partial class UnmatchedReceipt
{
    [Key]
    [Column("unmatched_receipt_id")]
    public long UnmatchedReceiptId { get; set; }

    [Column("line_id")]
    public long LineId { get; set; }

    [Column("part_number", TypeName = "character varying")]
    public string PartNumber { get; set; } = null!;

    [Column("candidate_part_ids")]
    public List<long> CandidatePartIds { get; set; } = null!;

    [Column("reason")]
    [StringLength(30)]
    public string Reason { get; set; } = null!;

    [Column("status")]
    [StringLength(20)]
    public string Status { get; set; } = null!;

    [Column("resolved_part_id")]
    public long? ResolvedPartId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("resolved_at")]
    public DateTime? ResolvedAt { get; set; }

    [ForeignKey("LineId")]
    [InverseProperty("UnmatchedReceipt")]
    public virtual PurchaseReceipt Line { get; set; } = null!;

    [ForeignKey("ResolvedPartId")]
    [InverseProperty("UnmatchedReceipts")]
    public virtual Parts? ResolvedPart { get; set; }
}
