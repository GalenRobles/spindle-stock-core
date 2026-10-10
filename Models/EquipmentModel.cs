using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AlmacenTaller.Models;

[Table("equipment_models")]
public partial class EquipmentModel
{
    [Key]
    [Column("model_id")]
    public long ModelId { get; set; }

    [Column("name", TypeName = "character varying")]
    public string Name { get; set; } = null!;

    [Column("brand", TypeName = "character varying")]
    public string Brand { get; set; } = null!;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [InverseProperty("Model")]
    public virtual ICollection<BomLine> BomLines { get; set; } = new List<BomLine>();
}
