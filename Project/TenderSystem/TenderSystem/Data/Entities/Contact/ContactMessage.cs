using System.ComponentModel.DataAnnotations;

namespace TenderSystem.Data.Entities.Contact;

public class ContactMessage
{
    public int Id { get; set; }

    [Required, StringLength(150)]
    public string FullName { get; set; } = "";

    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = "";

    [Required, StringLength(200)]
    public string Subject { get; set; } = "";

    [Required, StringLength(4000)]
    public string Message { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsRead { get; set; }
}
