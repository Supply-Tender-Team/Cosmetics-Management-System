namespace TenderSystem.Areas.Committee.Models.Votes;

// OWNER: Abdallah (helping Dana with TOP-02).
/// <summary>
/// The Top Committee voting list (/Committee/Votes). Current eligible members see recorded
/// vote choices, Reject/Retender reasons and final outcomes, but no prices or other bids.
/// </summary>
public class VotingListViewModel
{
    public int EligibleMembers { get; set; }
    public bool CommitteeComplete => EligibleMembers == 4;

    public List<AwaitingTenderItem> Awaiting { get; set; } = [];
    public List<DecidedTenderItem> Decided { get; set; } = [];
}

public class AwaitingTenderItem
{
    public int TenderId { get; set; }
    public string TenderNumber { get; set; } = "";
    public string Title { get; set; } = "";
    public int VotesReceived { get; set; }
    public List<MemberProgressViewModel> MemberProgress { get; set; } = [];
    public bool IVoted { get; set; }
    public bool PausedByClarification { get; set; }

    /// <summary>The Manager-approved report names a valid bid of this tender (same rule as the vote page).</summary>
    public bool ReportReady { get; set; }

    /// <summary>Neutral status text: never reveals how anyone voted.</summary>
    public string StatusText =>
        PausedByClarification ? "Paused – clarification open"
        : !ReportReady ? "Waiting for the Sourcing Manager"
        : VotesReceived >= 4 ? "All votes in – final decision pending"
        : "Voting in progress";
}

public class DecidedTenderItem
{
    public int TenderId { get; set; }
    public string TenderNumber { get; set; } = "";
    public string Title { get; set; } = "";
    public string Outcome { get; set; } = "";
    public DateTime DecidedAt { get; set; }
}
