using System.Security.Claims;
using TenderSystem.Areas.Shared.Models.Reports;
using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Reports;

public partial class ReportQueryService
{
    public List<ReportOptionViewModel> GetOptions(ClaimsPrincipal user)
    {
        var options = new List<ReportOptionViewModel>();
        if (user.IsInRole(Roles.Admin))
        {
            options.Add(new() { Key = "users", Title = "Users & roles", Group = "Administration", Description = "Accounts, roles, departments and last sign-in.", HasDepartment = true, DetailLabel = "Roles / last sign-in" });
            options.Add(new() { Key = "suppliers", Title = "Supplier directory", Group = "Administration", Description = "Companies and their approval status.", HasSupplier = true });
            options.Add(new() { Key = "departments", HasDate = false, Title = "Departments", Group = "Administration", Description = "Department status and account counts." });
            options.Add(new() { Key = "audit", Title = "Activity summary", Group = "Administration", Description = "Daily action counts, including recorded failed sign-ins.", DateLabel = "Day", DetailLabel = "Recorded actions" });
            options.Add(new() { Key = "sign-ins", Title = "Sign-in summary", Group = "Administration", Description = "Daily successful and failed attempts by sign-in method. Counts only.", DateLabel = "Day", DetailLabel = "Attempts" });
        }
        if (user.IsInRole(Roles.Requester))
            options.Add(new() { Key = "my-requests", Title = "My purchase requests", Group = "My work", Description = "Your requests and their linked tender status.", HasDepartment = true, HasAmount = true, HasCurrency = false, AmountLabel = "Estimated budget", HasTender = true, DetailLabel = "Priority / tender" });
        if (user.IsInRole(Roles.SourcingOfficer))
        {
            options.Add(new() { Key = "received-requests", Title = "Received requests", Group = "Sourcing", Description = "Submitted requests available to Sourcing. Private drafts are excluded.", HasDepartment = true, HasAmount = true, HasCurrency = false, AmountLabel = "Estimated budget", HasTender = true, DetailLabel = "Priority / tender" });
            options.Add(new() { Key = "tenders", Title = "Tender register", Group = "Sourcing", Description = "Published tenders, deadlines, invitations and submitted offer counts.", HasDepartment = true, HasTender = true, DateLabel = "Created", DetailLabel = "Deadline / participation" });
            options.Add(new() { Key = "offers", Title = "Offers & compliance", Group = "Sourcing", Description = "Submitted supplier offers and their latest compliance decision.", HasAmount = true, HasDepartment = true, HasSupplier = true, HasTender = true, DateLabel = "Submitted", DetailLabel = "Compliance" });
            options.Add(new() { Key = "evaluation", Title = "Evaluation progress", Group = "Sourcing", Description = "Ready confirmations and report delivery for each tender.", HasDepartment = true, HasTender = true, HasProgress = true, DetailLabel = "Results report" });
        }
        if (user.IsInRole(Roles.EvaluationCommittee))
            options.Add(new() { Key = "assigned-evaluation", Title = "My assigned evaluations", Group = "Evaluation", Description = "Only tenders where you are an assigned committee member.", HasDepartment = true, HasTender = true, HasProgress = true, DetailLabel = "Results report" });
        if (user.IsInRole(Roles.SourcingManager))
        {
            options.Add(new() { Key = "review-reports", Title = "Reports for my review", Group = "Manager", Description = "Reports forwarded to your review queue and their current outcomes.", HasDepartment = true, HasTender = true, DateLabel = "Submitted", DetailLabel = "Request / prepared by", RelatedUrl = "/Manager/Reports", RelatedLabel = "Open review queue" });
            options.Add(new() { Key = "manager-decisions", Title = "My review decisions", Group = "Manager", Description = "Your decisions and elapsed days since report submission.", HasDepartment = true, HasTender = true, DateLabel = "Decision date", DetailLabel = "Review duration" });
        }
        if (user.IsInRole(Roles.TopCommittee))
        {
            options.Add(new() { Key = "committee-decisions", Title = "My committee decisions", Group = "Top Committee", Description = "Your recorded final-review decisions.", HasDepartment = true, HasTender = true, DateLabel = "Decision date" });
            options.Add(new() { Key = "votes", Title = "My voting history", Group = "Top Committee", Description = "Your votes and comments on submitted tenders.", HasTender = true, DateLabel = "Voted" });
            options.Add(new() { Key = "awards", Title = "Awarded tenders", Group = "Top Committee", Description = "Awarded suppliers and recorded offer values. Currency is kept separate.", HasAmount = true, HasSupplier = true, HasDepartment = true, HasTender = true, RelatedUrl = "/Committee/Statistics", RelatedLabel = "Open bid history & statistics" });
        }
        if (user.IsInRole(Roles.Supplier))
        {
            options.Add(new() { Key = "my-offers", Title = "My offer history", Group = "Supplier", Description = "Only your company's offers, including your private drafts.", HasAmount = true, HasTender = true, DateLabel = "Submitted", DetailLabel = "Delivery / validity", RelatedUrl = "/Supplier/Offers", RelatedLabel = "Open my offers" });
            options.Add(new() { Key = "my-invitations", Title = "My invitations", Group = "Supplier", Description = "Only invitations addressed to your company.", HasTender = true, DateLabel = "Sent", DetailLabel = "Expiry / response" });
        }
        return options;
    }
}
