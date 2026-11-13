namespace TenderSystem.Areas.Admin.Models;

public class SupplierRowViewModel
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string Status { get; set; } = "";
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
}
