using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Data;
using TenderSystem.Models;

namespace TenderSystem.Controllers;

// OWNER: Nada (Home, About, How It Works). The Contact action lives in HomeController.Contact.cs (Abdallah).
public partial class HomeController : Controller
{
    private readonly ApplicationDbContext _db;

    public HomeController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        // Real statistics from the database (requirement: public home page shows real metrics)
        var model = new HomeViewModel
        {
            ActiveTenders = await _db.Tenders.CountAsync(t => t.Status == "Published" || t.Status == "Open"),
            ApprovedSuppliers = await _db.SupplierProfiles.CountAsync(p => p.Status == "Approved"),
            CompletedRequests = await _db.PurchaseRequisitions.CountAsync(r => r.Status == "Approved" || r.Status == "Converted to Tender"),
            AwardedTenders = await _db.Tenders.CountAsync(t => t.Status == "Awarded"),
        };
        return View(model);
    }

    [HttpGet]
    [HttpHead]
    public IActionResult WalkthroughFilm([FromServices] IWebHostEnvironment environment)
    {
        var film = environment.WebRootFileProvider.GetFileInfo("ms-wand-home/video/about-us.mp4");
        if (!film.Exists || film.PhysicalPath is null) return NotFound();
        return PhysicalFile(film.PhysicalPath, "video/mp4", enableRangeProcessing: true);
    }

    public IActionResult About() => View();

    public IActionResult HowItWorks() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
