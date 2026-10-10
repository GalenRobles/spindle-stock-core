using AlmacenTaller.DataContext;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AlmacenTaller.Pages;

public class IndexModel : PageModel
{
    private readonly AppDbContext _context;
    public IndexModel(AppDbContext context) => _context = context;
    public List<PiezaVista> Piezas { get; set; } = new();
    public List<FaltanteVista> Faltantes { get; set; } = new();
    public List<CompraVista> Compras { get; set; } = new();

    public async Task OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;
        var piezas = await _context.Parts.AsNoTracking()
            .Include(p => p.InventoryBalances).ThenInclude(b => b.Location)
            .Include(p => p.PartPolicy).AsSplitQuery()
            .OrderBy(p => p.PartId).ToListAsync(ct);

        Piezas = piezas.Select(p => new PiezaVista
        {
            Id = p.PartId, Sku = p.Sku ?? "—", Nombre = p.Name,
            Categoria = p.Family ?? p.PartGroup ?? "Sin categoría",
            StockFisico = p.InventoryBalances.Sum(b => (long)b.OnHand),
            Reservado = p.InventoryBalances.Sum(b => (long)b.Reserved),
            StockMinimo = p.PartPolicy?.MinQty ?? 0,
            TiempoReposicion = p.PartPolicy?.LeadTimeDays is int dias
                ? $"{dias} días" : "Sin definir",
            Ubicacion = string.Join(", ", p.InventoryBalances
                .Select(b => b.Location.Code).Distinct().OrderBy(c => c))
        }).ToList();

        Faltantes = await _context.Shortages.AsNoTracking()
            .Where(s => s.Status == "open" && !s.WorkOrder.IsDeleted)
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.ShortageId)
            .Select(s => new FaltanteVista
            {
                Nombre = s.Name, Orden = s.WorkOrder.Code,
                Cantidad = s.MissingQuantity.HasValue
                    ? s.MissingQuantity.Value - s.CoveredQuantity : (int?)null,
                Creado = s.CreatedAt
            }).ToListAsync(ct);

        var suggestions = await _context.ReorderSuggestions.AsNoTracking()
            .Where(s => s.Status == "active")
            .Include(s => s.WorkOrders).AsSplitQuery()
            .OrderByDescending(s => s.UpdatedAt).ThenByDescending(s => s.SuggestionId)
            .ToListAsync(ct);
        var catalogo = Piezas.ToDictionary(p => p.Id);
        Compras = suggestions.Select(s => new CompraVista
        {
            Nombre = catalogo.TryGetValue(s.PartId, out var p)
                ? p.Nombre : $"Pieza {s.PartId}",
            Cantidad = s.SuggestedQuantity,
            Reposicion = catalogo.TryGetValue(s.PartId, out var policy)
                ? policy.TiempoReposicion : "Sin definir",
            Ordenes = string.Join(", ", s.WorkOrders
                .Where(w => !w.IsDeleted).Select(w => w.Code).OrderBy(c => c))
        }).ToList();
    }
}

public class PiezaVista
{
    public long Id { get; set; }
    public string Sku { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Categoria { get; set; } = "";
    public string Ubicacion { get; set; } = "";
    public long StockFisico { get; set; }
    public long Reservado { get; set; }
    public int StockMinimo { get; set; }
    public string TiempoReposicion { get; set; } = "";
    public long Disponible => StockFisico - Reservado;
}
public class FaltanteVista
{
    public string Nombre { get; set; } = "";
    public string Orden { get; set; } = "";
    public int? Cantidad { get; set; }
    public DateTime Creado { get; set; }
}
public class CompraVista
{
    public string Nombre { get; set; } = "";
    public int Cantidad { get; set; }
    public string Reposicion { get; set; } = "";
    public string Ordenes { get; set; } = "";
}
