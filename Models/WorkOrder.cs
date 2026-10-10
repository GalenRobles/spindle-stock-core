using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AlmacenTaller.Models;

[Table("work_orders")]
[Index("IsDeleted", "Code", Name = "idx_work_orders_active")]
[Index("Stage", Name = "idx_work_orders_stage")]
[Index("Code", Name = "work_orders_code_key", IsUnique = true)]
public partial class WorkOrder
{
    [Key]
    [Column("work_order_id")]
    public long WorkOrderId { get; set; }

    [Column("code")]
    [StringLength(50)]
    public string Code { get; set; } = null!;

    [Column("model_id")]
    public long ModelId { get; set; }

    [Column("department", TypeName = "character varying")]
    public string Department { get; set; } = null!;

    [Column("stage", TypeName = "character varying")]
    public string Stage { get; set; } = null!;

    [Column("received_at")]
    public DateTime ReceivedAt { get; set; }

    [Column("is_deleted")]
    public bool IsDeleted { get; set; }

    [Column("deleted_at")]
    public DateTime? DeletedAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [InverseProperty("WorkOrder")]
    public virtual ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();

    [InverseProperty("WorkOrder")]
    public virtual ICollection<Shortage> Shortages { get; set; } = new List<Shortage>();

    [InverseProperty("WorkOrder")]
    public virtual ICollection<WorkOrderNeed> WorkOrderNeeds { get; set; } = new List<WorkOrderNeed>();

    [ForeignKey("WorkOrderId")]
    [InverseProperty("WorkOrders")]
    public virtual ICollection<ReorderSuggestion> Suggestions { get; set; } = new List<ReorderSuggestion>();
}
