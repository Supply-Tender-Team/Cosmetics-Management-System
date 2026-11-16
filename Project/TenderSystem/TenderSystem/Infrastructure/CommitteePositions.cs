namespace TenderSystem.Infrastructure;

/// <summary>
/// The four evaluation committee positions. The Title is only a label. The Code is what is stored in CommitteeMembers.MemberRole.
/// All four positions are held by Evaluation Committee accounts (never a Requester, Sourcing Officer or Sourcing Manager).
/// The first one is the Department Manager of the request's department; the other three are fixed by the Admin.
/// NOTE: the Department Financial Officer keeps the old seat code "SourcingManager" so the other areas keep compiling.
/// The real Sourcing Manager is not a committee member.
/// </summary>
public static class CommitteePositions
{
    /// <summary>Display order.</summary>
    public static readonly (string Code, string Title)[] All =
    [
        (Statuses.CommitteeRole.Department, "Department Manager"),
        (Statuses.CommitteeRole.SourcingManager, "Department Financial Officer"),
        (Statuses.CommitteeRole.Technical, "Sourcing Department Representative"),
        (Statuses.CommitteeRole.Financial, "Financial Manager")
    ];

    /// <summary>The three positions the Admin controls (every position except the Department Manager), in display order.</summary>
    public static readonly string[] AdminCodes = All.Skip(1).Select(position => position.Code).ToArray();

    public static string TitleFor(string code) => All.FirstOrDefault(position => position.Code == code).Title ?? code;
}
