using AlmacenTaller.DataContext;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AlmacenTaller.Pages;

public class IndexModel : PageModel
{
    private readonly AppDbContext _context;

    public IndexModel(AppDbContext context)
    {
        _context = context;
    }

    public List<PiezaVista> Piezas { get; set; } = new();

    public async Task OnGetAsync()
    {
        var piezas = await _context.Parts
            .AsNoTracking()
            .Include(p => p.InventoryBalances)
            .Include(p => p.PartPolicy)
            .OrderBy(p => p.PartId)
            .ToListAsync();

        Piezas = piezas.Select(p => new PiezaVista
        {
            Id = p.PartId,
            Sku = p.Sku ?? "—",
            Nombre = p.Name,
            Categoria = p.Family ?? p.PartGroup ?? "Sin categoría",
            StockFisico = p.InventoryBalances.Sum(b => (long)b.OnHand),
            Reservado = p.InventoryBalances.Sum(b => (long)b.Reserved),
            StockMinimo = p.PartPolicy?.MinQty ?? 0,
            TiempoReposicion = p.PartPolicy?.LeadTimeDays is int dias
                ? $"{dias} días"
                : "Sin definir"
        }).ToList();
    }
}

public class PiezaVista
{
    public long Id { get; set; }
    public string Sku { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Categoria { get; set; } = "";
    public long StockFisico { get; set; }
    public long Reservado { get; set; }
    public int StockMinimo { get; set; }
    public string TiempoReposicion { get; set; } = "";

    public long Disponible => StockFisico - Reservado;
}