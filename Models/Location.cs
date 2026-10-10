using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AlmacenTaller.Models;

[Table("locations")]
[Index("Code", Name = "locations_code_key", IsUnique = true)]
public partial class Location
{
    [Key]
    [Column("location_id")]
    public long LocationId { get; set; }

    [Column("code")]
    [StringLength(20)]
    public string Code { get; set; } = null!;

    [Column("name", TypeName = "character varying")]
    public string Name { get; set; } = null!;

    [Column("is_workbench")]
    public bool IsWorkbench { get; set; }

    [Column("active")]
    public bool Active { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [InverseProperty("Location")]
    public virtual ICollection<InventoryBalance> InventoryBalances { get; set; } = new List<InventoryBalance>();

    [InverseProperty("Location")]
    public virtual ICollection<InventoryCount> InventoryCounts { get; set; } = new List<InventoryCount>();

    [InverseProperty("Location")]
    public virtual ICollection<InventoryMovement> InventoryMovements { get; set; } = new List<InventoryMovement>();

    [InverseProperty("Location")]
    public virtual ICollection<PurchaseReceipt> PurchaseReceipts { get; set; } = new List<PurchaseReceipt>();

    [InverseProperty("Location")]
    public virtual ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
}
