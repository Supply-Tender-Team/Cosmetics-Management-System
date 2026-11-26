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
    private const int MaximumQrContentLength = 500;

    [HttpGet]
    public IActionResult Verify(string? payload)
    {
        var scannedQrContent = (payload ?? string.Empty).Trim();
        if (scannedQrContent.Length > MaximumQrContentLength)
        {
            scannedQrContent = scannedQrContent.Substring(0, MaximumQrContentLength);
        }

        // Both markers must be present for this simulated result to match.
        var isMatchingDemoGuarantee = scannedQrContent.Contains(DemoReference, StringComparison.OrdinalIgnoreCase)
            && scannedQrContent.Contains("FICTIONAL", StringComparison.OrdinalIgnoreCase);

        return View("~/Areas/Supplier/Views/Offers/MockBank.cshtml", new DemoBankVerificationViewModel
        {
            ScannedPayload = scannedQrContent,
            IsKnownTestGuarantee = isMatchingDemoGuarantee,
            ReferenceNumber = isMatchingDemoGuarantee ? DemoReference : "No demo record matched",
            Beneficiary = isMatchingDemoGuarantee ? "MS.WAND Demo Supplier" : "Not verified",
            IssuingBank = isMatchingDemoGuarantee ? "Sample Jordan Bank" : "Not verified",
            Amount = isMatchingDemoGuarantee ? "500.00 JOD" : "Not verified",
            ExpiresAt = isMatchingDemoGuarantee ? "22 Nov 2027" : "Not verified"
        });
    }
}
