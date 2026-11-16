namespace TenderSystem.Areas.Committee.Models.Votes;

// OWNER: Abdallah. Public voting progress only; no vote choice or reason.
public class MemberProgressViewModel
{
    public string DisplayName { get; set; } = "";
    public bool IsCurrentUser { get; set; }
    public bool HasVoted { get; set; }
}
