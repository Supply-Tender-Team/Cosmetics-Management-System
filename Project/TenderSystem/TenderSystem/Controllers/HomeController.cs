using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Data;
using TenderSystem.Models;

namespace TenderSystem.Controllers;

// OWNER: Dev 3 (public pages)
public class HomeController : Controller
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

    public IActionResult About() => View();

    public IActionResult HowItWorks() => View();

    public IActionResult Contact() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
