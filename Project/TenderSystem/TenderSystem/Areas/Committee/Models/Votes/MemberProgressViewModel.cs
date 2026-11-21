namespace TenderSystem.Areas.Committee.Models.Votes;

// OWNER: Abdallah. Voting progress, recorded choice and reason for eligible Top Committee members only.
public class MemberProgressViewModel
{
    public string DisplayName { get; set; } = "";
    public bool IsCurrentUser { get; set; }
    public bool HasVoted { get; set; }
    public string? Vote { get; set; }

    /// <summary>The submitted reason (Reject or Retender only); null for Approve or when none was given.</summary>
    public string? Reason { get; set; }
}
