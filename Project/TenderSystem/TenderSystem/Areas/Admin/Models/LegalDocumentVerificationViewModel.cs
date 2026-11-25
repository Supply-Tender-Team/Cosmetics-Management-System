using System.ComponentModel.DataAnnotations;

namespace TenderSystem.Areas.Admin.Models;

public class LegalDocumentVerificationViewModel
{
    public int SupplierId { get; set; }
    public int DocumentId { get; set; }
    public string CompanyName { get; set; } = "";
    public string DocumentType { get; set; } = "";
    public DateTime? ExpiryDate { get; set; }
    public bool CanView { get; set; }
    public int? CurrentVerificationId { get; set; }
    public List<LegalDocumentVerificationHistoryViewModel> History { get; set; } = [];

    [Required]
    public string Status { get; set; } = "";
    [Required]
    public string Method { get; set; } = "";
    [StringLength(1000)]
    public string? Reason { get; set; }
    [Range(typeof(bool), "true", "true", ErrorMessage = "Confirm the decision before submitting.")]
    public bool Confirmed { get; set; }
}

public class LegalDocumentVerificationHistoryViewModel
{
    public int Id { get; set; }
    public string Status { get; set; } = "";
    public string Method { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Reviewer { get; set; } = "";
    public DateTime VerifiedAt { get; set; }
}
