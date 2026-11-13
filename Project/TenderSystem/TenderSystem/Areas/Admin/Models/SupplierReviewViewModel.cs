namespace TenderSystem.Areas.Admin.Models;

public class SupplierReviewViewModel
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = "";
    public string Email { get; set; } = "";
    public string TaxNumber { get; set; } = "";
    public string CommercialRegNumber { get; set; } = "";
    public string Category { get; set; } = "";
    public string Country { get; set; } = "";
    public string City { get; set; } = "";
    public string Address { get; set; } = "";
    public string ContactPerson { get; set; } = "";
    public string ContactPhone { get; set; } = "";
    public string? Website { get; set; }
    public string Status { get; set; } = "";
    public string? StatusReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
}
