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

    public IList<Pieza> Piezas { get; set; } = default!;

    public async Task OnGetAsync()
    {
        // Traemos todas las piezas desde PostgreSQL ordenadas por ID
        Piezas = await _context.Piezas.OrderBy(p => p.Id).ToListAsync();
    }
}