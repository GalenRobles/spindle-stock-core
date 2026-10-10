using Microsoft.EntityFrameworkCore;
using AlmacenTaller.Models; // Asegúrate de que este using exista

namespace AlmacenTaller.DataContext
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        // Agrega todos estos DbSets. Son los que Entity Framework usa para hacer los SELECT, INSERT, etc.
        public DbSet<Part> Parts { get; set; }
        public DbSet<Location> Locations { get; set; }
        public DbSet<InventoryBalance> InventoryBalances { get; set; }
        public DbSet<Reservation> Reservations { get; set; }
        public DbSet<Shortage> Shortages { get; set; }
        public DbSet<OutboxEvent> OutboxEvents { get; set; }
        public DbSet<ProcessedEvent> ProcessedEvents { get; set; }
        public DbSet<InventoryMovement> InventoryMovements { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Tu código de configuración y HasData que ya hicimos va aquí...

            // TIP VITAL para InventoryBalance porque tiene llave primaria compuesta:
            modelBuilder.Entity<InventoryBalance>()
                .HasKey(ib => new { ib.PartId, ib.LocationId });
        }
    }
}