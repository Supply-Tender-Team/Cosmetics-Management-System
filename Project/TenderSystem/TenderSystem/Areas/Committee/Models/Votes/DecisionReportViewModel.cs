using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Committee.Models.Votes;

// OWNER: Abdallah (helping Dana with TOP-02).
/// <summary>
/// The final Top Committee decision report. Only committee members can open it, and only after the
/// decision is recorded. It shows the recommended (awarded or rejected) offer only, never other bids.
/// </summary>
public class DecisionReportViewModel
{
    public int TenderId { get; set; }

    /// <summary>False when the signed-in user is not a committee member for this tender.</summary>
    public bool CanView { get; set; }

    public string TenderNumber { get; set; } = "";
    public string TenderTitle { get; set; } = "";
    public string TenderStatus { get; set; } = "";

    public string Outcome { get; set; } = "";          // Approved or Rejected
    public DateTime DecidedAt { get; set; }
    public string Summary { get; set; } = "";

    public string? RecommendedSupplier { get; set; }
    public decimal? RecommendedPrice { get; set; }
    public string? RecommendedCurrency { get; set; }

    public List<DecisionReportVote> Votes { get; set; } = [];

    public int ApproveCount => Votes.Count(v => v.Vote == Statuses.VoteType.Approve);
    public int RejectCount => Votes.Count(v => v.Vote == Statuses.VoteType.Reject);
}

public class DecisionReportVote
{
    public string MemberName { get; set; } = "";
    public string Vote { get; set; } = "";
    public string? Reason { get; set; }
    public DateTime VotedAt { get; set; }
}
