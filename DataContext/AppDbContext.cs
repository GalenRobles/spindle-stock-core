using Microsoft.EntityFrameworkCore;
using AlmacenTaller.Models;

namespace AlmacenTaller.DataContext;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Part> Parts => Set<Part>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Part>().ToTable("parts");
    }
}