namespace TenderSystem.Areas.Committee.Models;

public class TopCommitteeBidHistoryOfferViewModel
{
    public string SupplierName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal TotalPrice { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTime? SubmittedAt { get; set; }
    public bool IsAwarded { get; set; }
}
