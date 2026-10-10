using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AlmacenTaller.Models;

[Table("purchase_receipts")]
[Index("PartNumberNorm", Name = "idx_receipts_part_norm")]
[Index("RequestNumber", Name = "idx_receipts_request")]
[Index("Status", Name = "idx_receipts_status")]
[Index("WorkOrderCode", Name = "idx_receipts_work_order")]
public partial class PurchaseReceipt
{
    [Key]
    [Column("line_id")]
    public long LineId { get; set; }

    [Column("purchase_request_id")]
    public long PurchaseRequestId { get; set; }

    [Column("request_number", TypeName = "character varying")]
    public string RequestNumber { get; set; } = null!;

    [Column("part_number", TypeName = "character varying")]
    public string PartNumber { get; set; } = null!;

    [Column("part_number_norm", TypeName = "character varying")]
    public string? PartNumberNorm { get; set; }

    [Column("description", TypeName = "character varying")]
    public string Description { get; set; } = null!;

    [Column("quantity")]
    public int Quantity { get; set; }

    [Column("unit_price")]
    [Precision(18, 6)]
    public decimal UnitPrice { get; set; }

    [Column("currency")]
    [StringLength(3)]
    public string Currency { get; set; } = null!;

    [Column("purpose")]
    [StringLength(30)]
    public string Purpose { get; set; } = null!;

    [Column("received_at")]
    public DateTime ReceivedAt { get; set; }

    [Column("work_order_code")]
    [StringLength(50)]
    public string? WorkOrderCode { get; set; }

    [Column("matched_part_id")]
    public long? MatchedPartId { get; set; }

    [Column("location_id")]
    public long? LocationId { get; set; }

    [Column("status")]
    [StringLength(25)]
    public string Status { get; set; } = null!;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [ForeignKey("LocationId")]
    [InverseProperty("PurchaseReceipts")]
    public virtual Location? Location { get; set; }

    [ForeignKey("MatchedPartId")]
    [InverseProperty("PurchaseReceipts")]
    public virtual Part? MatchedPart { get; set; }

    [InverseProperty("Line")]
    public virtual UnmatchedReceipt? UnmatchedReceipt { get; set; }
}
