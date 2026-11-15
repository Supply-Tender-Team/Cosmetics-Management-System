namespace TenderSystem.Areas.Committee.Models.Votes;

// OWNER: Abdallah (helping Dana with TOP-02).
/// <summary>
/// What a committee member may see about the voting on one tender.
/// It never contains other members' vote choices or reasons.
/// </summary>
public class VotingStatusViewModel
{
    public int TenderId { get; set; }
    public string TenderNumber { get; set; } = "";
    public string TenderTitle { get; set; } = "";

    public string? RecommendedSupplier { get; set; }
    public decimal? RecommendedPrice { get; set; }
    public string? RecommendedCurrency { get; set; }

    public int EligibleMembers { get; set; }
    public bool IsMember { get; set; }
    public int VotesReceived { get; set; }
    public List<MemberProgressViewModel> MemberProgress { get; set; } = [];

    public string? MyVote { get; set; }
    public string? MyComment { get; set; }
    public DateTime? MyVotedAt { get; set; }

    /// <summary>Why this member cannot vote now; null means the vote form can be shown.</summary>
    public string? BlockReason { get; set; }

    /// <summary>True when the final Top Committee decision has been recorded.</summary>
    public bool IsFinalized { get; set; }

    /// <summary>Approved or Rejected (only when IsFinalized).</summary>
    public string? Outcome { get; set; }

    /// <summary>All four votes are in but no decision is saved: why (for example an ineligible supplier).</summary>
    public string? DecisionBlockedReason { get; set; }

    /// <summary>Show the "Retry final decision" button (members only).</summary>
    public bool CanRetryFinalize { get; set; }

    public bool CanVote => BlockReason is null;
    public bool AllVotesReceived => VotesReceived >= 4;
}

/// <summary>The vote page: the status plus the (empty or re-shown) form.</summary>
public class CastVotePageViewModel
{
    public VotingStatusViewModel Status { get; set; } = new();
    public CastVoteViewModel Form { get; set; } = new();
}
