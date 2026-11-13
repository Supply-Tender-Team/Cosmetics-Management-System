using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Data.Entities.Contact;
using TenderSystem.ViewModels.Contact;

namespace TenderSystem.Controllers;

// OWNER: Abdallah (Contact Us)
public partial class HomeController
{
    [AllowAnonymous]
    [HttpGet]
    public IActionResult Contact() => View(new ContactFormViewModel());

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Contact(ContactFormViewModel model)
    {
        model.FullName = model.FullName?.Trim() ?? "";
        model.Email = model.Email?.Trim() ?? "";
        model.Subject = model.Subject?.Trim() ?? "";
        model.Message = model.Message?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(model.FullName)) ModelState.AddModelError(nameof(model.FullName), "Enter your name.");
        if (string.IsNullOrWhiteSpace(model.Subject)) ModelState.AddModelError(nameof(model.Subject), "Enter a subject.");
        if (string.IsNullOrWhiteSpace(model.Message)) ModelState.AddModelError(nameof(model.Message), "Enter a message.");
        if (!string.IsNullOrEmpty(model.Website)) ModelState.AddModelError("", "The message could not be submitted.");

        if (!ModelState.IsValid) return View(model);

        _db.ContactMessages.Add(new ContactMessage
        {
            FullName = model.FullName,
            Email = model.Email,
            Subject = model.Subject,
            Message = model.Message,
            CreatedAt = DateTime.UtcNow
        });

        try
        {
            await _db.SaveChangesAsync();
            TempData["ContactSuccess"] = "Your message was received. Thank you for contacting us.";
            return RedirectToAction(nameof(Contact));
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError("", "We could not save your message. Please try again later.");
            return View(model);
        }
    }
}
