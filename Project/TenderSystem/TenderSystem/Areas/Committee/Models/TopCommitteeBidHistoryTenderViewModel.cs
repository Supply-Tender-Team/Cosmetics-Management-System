namespace TenderSystem.Areas.Committee.Models;

public class TopCommitteeBidHistoryTenderViewModel
{
    public int TenderId { get; set; }
    public string TenderNumber { get; set; } = string.Empty;
    public string TenderTitle { get; set; } = string.Empty;
    public string TenderStatus { get; set; } = string.Empty;
    public string RequestNumber { get; set; } = string.Empty;
    public string RequestTitle { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string? AwardedSupplier { get; set; }
    public DateTime CompletedAt { get; set; }
    public List<TopCommitteeBidHistoryOfferViewModel> Bids { get; set; } = [];
    public List<TopCommitteeBidHistoryDecisionViewModel> Decisions { get; set; } = [];
}
