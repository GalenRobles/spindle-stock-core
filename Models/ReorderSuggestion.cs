using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AlmacenTaller.Models;

[Table("reorder_suggestions")]
public partial class ReorderSuggestion
{
    [Key]
    [Column("suggestion_id")]
    public long SuggestionId { get; set; }

    [Column("part_id")]
    public long PartId { get; set; }

    [Column("suggested_quantity")]
    public int SuggestedQuantity { get; set; }

    [Column("status")]
    [StringLength(20)]
    public string Status { get; set; } = null!;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [ForeignKey("PartId")]
    [InverseProperty("ReorderSuggestion")]
    public virtual Parts Part { get; set; } = null!;

    [ForeignKey("SuggestionId")]
    [InverseProperty("Suggestions")]
    public virtual ICollection<WorkOrder> WorkOrders { get; set; } = new List<WorkOrder>();
}
