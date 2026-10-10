using AlmacenTaller.Data;
using AlmacenTaller.Models;
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

    public IList<PiezaInventarioVM> Piezas { get; set; } = new List<PiezaInventarioVM>();

    // El esquema (TallerBase.sql) no tiene stock mínimo ni tiempo de reposición.
    // Se conservan aquí los valores de demo que traía el seed original, por PartId.
    private static readonly Dictionary<long, (int Minimo, string Reposicion)> DatosDemo = new()
    {
        [1] = (4, "3 semanas"),
        [2] = (3, "5 días"),
        [3] = (2, "12 días"),
    };

    public async Task OnGetAsync()
    {
        var piezas = await _context.Piezas
            .AsNoTracking()
            .OrderBy(p => p.PartId)
            .ToListAsync();

        var saldos = await _context.InventoryBalances
            .AsNoTracking()
            .ToListAsync();

        var saldosPorPieza = saldos
            .GroupBy(s => s.PartId)
            .ToDictionary(g => g.Key, g => (OnHand: g.Sum(x => x.OnHand), Reserved: g.Sum(x => x.Reserved)));

        Piezas = piezas.Select(p =>
        {
            saldosPorPieza.TryGetValue(p.PartId, out var saldo);
            DatosDemo.TryGetValue(p.PartId, out var demo);

            return new PiezaInventarioVM
            {
                PartId = p.PartId,
                Sku = p.Sku ?? string.Empty,
                Nombre = p.Name,
                Categoria = p.Family ?? string.Empty,
                StockFisico = saldo.OnHand,
                Reservado = saldo.Reserved,
                StockMinimo = demo.Minimo,
                TiempoReposicion = demo.Reposicion ?? "N/D"
            };
        }).ToList();
    }
}
