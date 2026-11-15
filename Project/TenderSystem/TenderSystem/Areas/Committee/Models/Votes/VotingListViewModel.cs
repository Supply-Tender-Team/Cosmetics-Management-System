namespace TenderSystem.Areas.Committee.Models.Votes;

// OWNER: Abdallah (helping Dana with TOP-02).
/// <summary>
/// The Top Committee voting list (/Committee/Votes). It contains only counts, the member's own
/// "voted / not voted" flag and final outcomes: no individual votes, reasons, prices or other bids.
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

    /// <summary>Neutral status text: never reveals how anyone voted.</summary>
    public string StatusText =>
        PausedByClarification ? "Paused – clarification open"
        : VotesReceived >= 4 ? "All votes in – final decision pending"
        : "Voting in progress";
}

public class DecidedTenderItem
{
    public int TenderId { get; set; }
    public string TenderNumber { get; set; } = "";
    public string Title { get; set; } = "";
    public bool Awarded { get; set; }
    public DateTime DecidedAt { get; set; }
}
