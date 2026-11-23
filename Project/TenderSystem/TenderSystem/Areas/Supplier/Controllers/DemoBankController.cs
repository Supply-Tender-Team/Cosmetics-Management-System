using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Areas.Supplier.Models;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Supplier.Controllers;

[Area("Supplier")]
[Authorize(Roles = Roles.Supplier)]
public sealed class DemoBankController : Controller
{
    private const string DemoReference = "OCR-TEST-2026-01";

    [HttpGet]
    public IActionResult Verify(string? payload)
    {
        var scannedPayload = (payload ?? string.Empty).Trim();
        if (scannedPayload.Length > 500) scannedPayload = scannedPayload[..500];

        var isKnownTestGuarantee = scannedPayload.Contains(DemoReference, StringComparison.OrdinalIgnoreCase)
            && scannedPayload.Contains("FICTIONAL", StringComparison.OrdinalIgnoreCase);

        return View("~/Areas/Supplier/Views/Offers/MockBank.cshtml", new DemoBankVerificationViewModel
        {
            ScannedPayload = scannedPayload,
            IsKnownTestGuarantee = isKnownTestGuarantee,
            ReferenceNumber = isKnownTestGuarantee ? DemoReference : "No demo record matched",
            Beneficiary = isKnownTestGuarantee ? "MS.WAND Demo Supplier" : "Not verified",
            IssuingBank = isKnownTestGuarantee ? "Sample Jordan Bank" : "Not verified",
            Amount = isKnownTestGuarantee ? "500.00 JOD" : "Not verified",
            ExpiresAt = isKnownTestGuarantee ? "22 Nov 2027" : "Not verified"
        });
    }
}
