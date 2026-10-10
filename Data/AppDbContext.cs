using Microsoft.EntityFrameworkCore;
using AlmacenTaller.Models;

namespace AlmacenTaller.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Part> Piezas => Set<Part>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<InventoryBalance> InventoryBalances => Set<InventoryBalance>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ==========================================================
        // Mapeo al esquema de TallerBase.sql (tablas y columnas snake_case)
        // ==========================================================
        modelBuilder.Entity<Part>(e =>
        {
            e.ToTable("parts");
            e.HasKey(p => p.PartId);
            e.Property(p => p.PartId).HasColumnName("part_id").ValueGeneratedNever(); // IDs vienen del sistema externo
            e.Property(p => p.Sku).HasColumnName("sku").HasMaxLength(150);            // NO unique, puede ser NULL
            e.Property(p => p.Name).HasColumnName("name").HasMaxLength(250).IsRequired();
            e.Property(p => p.Family).HasColumnName("family").HasMaxLength(100);
            e.Property(p => p.PartGroup).HasColumnName("part_group").HasMaxLength(100);
            e.Property(p => p.Subgroup).HasColumnName("subgroup").HasMaxLength(100);
            e.Property(p => p.Unit).HasColumnName("unit").HasMaxLength(30).IsRequired();
            e.Ignore(p => p.ImageUrl); // TEMPORAL: sin columna image_url por ahora; para activarla, quitar esta línea y usar HasColumnName("image_url")
            e.Property(p => p.Active).HasColumnName("active").HasDefaultValue(true);
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<Location>(e =>
        {
            e.ToTable("locations");
            e.HasKey(l => l.LocationId);
            e.Property(l => l.LocationId).HasColumnName("location_id").ValueGeneratedNever();
            e.Property(l => l.Code).HasColumnName("code").HasMaxLength(20).IsRequired();
            e.HasIndex(l => l.Code).IsUnique();
            e.Property(l => l.Name).HasColumnName("name").HasMaxLength(150).IsRequired();
            e.Property(l => l.IsWorkbench).HasColumnName("is_workbench").HasDefaultValue(false);
            e.Property(l => l.Active).HasColumnName("active").HasDefaultValue(true);
            e.Property(l => l.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<InventoryBalance>(e =>
        {
            e.ToTable("inventory_balances");
            e.HasKey(b => new { b.PartId, b.LocationId });               // clave compuesta (faltaba)
            e.Property(b => b.PartId).HasColumnName("part_id");
            e.Property(b => b.LocationId).HasColumnName("location_id");
            e.Property(b => b.OnHand).HasColumnName("on_hand").HasDefaultValue(0);
            e.Property(b => b.Reserved).HasColumnName("reserved").HasDefaultValue(0);
            e.Property(b => b.UpdatedAt).HasColumnName("updated_at");
            e.Ignore(b => b.Available);                                   // propiedad calculada, no es columna

            e.HasOne<Part>().WithMany().HasForeignKey(b => b.PartId);
            e.HasOne<Location>().WithMany().HasForeignKey(b => b.LocationId);
        });

        // ==========================================================
        // Datos iniciales (fecha FIJA: con UtcNow el modelo cambiaba en cada ejecución)
        // ==========================================================
        var seedDate = new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc);

        modelBuilder.Entity<Part>().HasData(
            new Part
            {
                PartId = 1, Sku = "BAL-7008C", Name = "Rodamiento Cerámico Híbrido 7008-C",
                Family = "Husillos CNC", PartGroup = "Mecánico", Subgroup = "Rodamientos",
                Unit = "pz", Active = true, CreatedAt = seedDate, UpdatedAt = seedDate
            },
            new Part
            {
                PartId = 2, Sku = "SEL-VT45", Name = "Sello Laberíntico Viton 45mm",
                Family = "Sellado", PartGroup = "Mecánico", Subgroup = "Sellos",
                Unit = "pz", Active = true, CreatedAt = seedDate, UpdatedAt = seedDate
            },
            new Part
            {
                PartId = 3, Sku = "TH-PT100", Name = "Sensor Térmico PT100 Calibrado",
                Family = "Instrumentación", PartGroup = "Eléctrico", Subgroup = "Sensores",
                Unit = "pz", Active = true, CreatedAt = seedDate, UpdatedAt = seedDate
            }
        );

        modelBuilder.Entity<Location>().HasData(
            new Location
            {
                LocationId = 100, Code = "ALM-PRINCIPAL", Name = "Almacén Central",
                IsWorkbench = false, Active = true, CreatedAt = seedDate
            }
        );

        modelBuilder.Entity<InventoryBalance>().HasData(
            new InventoryBalance { PartId = 1, LocationId = 100, OnHand = 8, Reserved = 6, UpdatedAt = seedDate },
            new InventoryBalance { PartId = 2, LocationId = 100, OnHand = 2, Reserved = 2, UpdatedAt = seedDate },
            new InventoryBalance { PartId = 3, LocationId = 100, OnHand = 1, Reserved = 3, UpdatedAt = seedDate }
        );
    }
}
