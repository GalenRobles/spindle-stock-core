using Microsoft.AspNetCore.Mvc.RazorPages;
using AlmacenTaller.DataContext;
using AlmacenTaller.Models;

namespace AlmacenTaller.Pages;

public class IndexModel : PageModel
{
    private readonly AppDbContext _context;

    public IndexModel(AppDbContext context)
    {
        _context = context;
    }

    public List<Part> Piezas { get; set; } = new();

    public void OnGet()
    {
        Piezas = _context.Parts.ToList();
    }
}