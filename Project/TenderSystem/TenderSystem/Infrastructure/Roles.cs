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
}

/// <summary>Where each role lands after login. Used by the navbar and by the login action.</summary>
public static class DashboardRouter
{
    public static string UrlFor(ClaimsPrincipal user)
    {
        if (user.IsInRole(Roles.Admin)) return "/Admin/Dashboard";
        if (user.IsInRole(Roles.Requester)) return "/Requester/Dashboard";
        if (user.IsInRole(Roles.SourcingOfficer)) return "/Sourcing/Dashboard";
        if (user.IsInRole(Roles.EvaluationCommittee)) return "/Evaluation/Dashboard";
        if (user.IsInRole(Roles.SourcingManager)) return "/Manager/Dashboard";
        if (user.IsInRole(Roles.TopCommittee)) return "/Committee/Dashboard";
        if (user.IsInRole(Roles.Supplier)) return "/Supplier/Dashboard";
        return "/";
    }
}
