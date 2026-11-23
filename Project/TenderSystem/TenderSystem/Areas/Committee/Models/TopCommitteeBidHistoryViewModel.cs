namespace TenderSystem.Areas.Committee.Models;

public class TopCommitteeBidHistoryViewModel
{
    public string Search { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public int CompletedTenderCount { get; set; }
    public int AwardedTenderCount { get; set; }
    public int RejectedTenderCount { get; set; }
    public int RetenderTenderCount { get; set; }
    public int SubmittedBidCount { get; set; }
    public List<TopCommitteeBidHistoryTenderViewModel> Tenders { get; set; } = [];
}
