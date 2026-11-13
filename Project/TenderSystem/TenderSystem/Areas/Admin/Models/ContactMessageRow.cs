namespace TenderSystem.Areas.Admin.Models;

public class ContactMessageRow
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Subject { get; set; } = "";

    /// <summary>The first characters of the message, shown under the subject in the inbox.</summary>
    public string Preview { get; set; } = "";

    public DateTime CreatedAt { get; set; }
    public bool IsRead { get; set; }
}
