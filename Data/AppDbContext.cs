using Microsoft.EntityFrameworkCore;
using AlmacenTaller.Models;

namespace AlmacenTaller.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Pieza> Piezas => Set<Pieza>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // datos iniciales de ejemplo
        modelBuilder.Entity<Pieza>().HasData(
            new Pieza { Id = 1, Sku = "BAL-7008C", Nombre = "Rodamiento Cerámico Híbrido 7008-C", Categoria = "Husillos CNC", StockFisico = 8, Reservado = 6, StockMinimo = 4, TiempoReposicion = "3 semanas" },
            new Pieza { Id = 2, Sku = "SEL-VT45", Nombre = "Sello Laberíntico Viton 45mm", Categoria = "Sellado", StockFisico = 2, Reservado = 2, StockMinimo = 3, TiempoReposicion = "5 días" },
            new Pieza { Id = 3, Sku = "TH-PT100", Nombre = "Sensor Térmico PT100 Calibrado", Categoria = "Instrumentación", StockFisico = 1, Reservado = 3, StockMinimo = 2, TiempoReposicion = "12 días" }
        );
    }
}