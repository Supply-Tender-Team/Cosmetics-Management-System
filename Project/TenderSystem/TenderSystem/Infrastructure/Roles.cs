using System.Security.Claims;

namespace TenderSystem.Infrastructure;

/// <summary>Role names. Use these constants, never raw strings: [Authorize(Roles = Roles.Admin)]</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Requester = "Requester";
    public const string SourcingOfficer = "SourcingOfficer";
    public const string EvaluationCommittee = "EvaluationCommittee";
    public const string SourcingManager = "SourcingManager";
    public const string TopCommittee = "TopCommittee";
    public const string Supplier = "Supplier";

    public static readonly string[] All =
        [Admin, Requester, SourcingOfficer, EvaluationCommittee, SourcingManager, TopCommittee, Supplier];

    /// <summary>Human-friendly role name for the header and messages.</summary>
    public static string LabelFor(string role) => role switch
    {
        Admin => "Admin",
        Requester => "Requesting Department",
        SourcingOfficer => "Sourcing Officer",
        EvaluationCommittee => "Evaluation Committee",
        SourcingManager => "Sourcing Manager",
        TopCommittee => "Top Committee",
        Supplier => "Supplier",
        _ => role,
    };

    public static string LabelFor(ClaimsPrincipal user) =>
        All.Where(user.IsInRole).Select(LabelFor).FirstOrDefault() ?? "Guest";
}

/// <summary>Where each role lands after login. Used by the navbar and by the login action.</summary>
public static class DashboardRouter
{
    public static string UrlFor(ClaimsPrincipal user) => UrlFor(Roles.All.Where(user.IsInRole));

    public static string UrlFor(IEnumerable<string> roles)
    {
        var set = roles.ToHashSet();
        if (set.Contains(Roles.Admin)) return "/Admin/Dashboard";
        if (set.Contains(Roles.Requester)) return "/Requester/Dashboard";
        if (set.Contains(Roles.SourcingOfficer)) return "/Sourcing/Dashboard";
        if (set.Contains(Roles.EvaluationCommittee)) return "/Evaluation/Dashboard";
        if (set.Contains(Roles.SourcingManager)) return "/Manager/Dashboard";
        if (set.Contains(Roles.TopCommittee)) return "/Committee/Dashboard";
        if (set.Contains(Roles.Supplier)) return "/Supplier/Dashboard";
        return "/";
    }
}
