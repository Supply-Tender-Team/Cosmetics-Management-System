using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Committee.Models;
using TenderSystem.Data;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Committee.Controllers;

[Area("Committee")]
[Authorize(Roles = Roles.TopCommittee)]
public class HistoryController : Controller
{
    // OWNER: Dana

    private readonly ApplicationDbContext _db;

    public HistoryController(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index(string? search, string? outcome)
    {
        var history = _db.Tenders
            .AsNoTracking()
            .Where(tender => tender.RequiresTopCommittee
                && (tender.Status == Statuses.TenderStatus.Awarded
                    || tender.Status == Statuses.TenderStatus.Rejected
                    || tender.Status == Statuses.TenderStatus.RetenderRequired));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            history = history.Where(tender => tender.TenderNumber.Contains(term)
                || tender.Title.Contains(term)
                || tender.Requisition.RequestNumber.Contains(term)
                || tender.Requisition.Title.Contains(term)
                || tender.Bids.Any(bid => bid.SupplierProfile.CompanyName.Contains(term)));
        }

        if (outcome == Statuses.TenderStatus.Awarded
            || outcome == Statuses.TenderStatus.Rejected || outcome == Statuses.TenderStatus.RetenderRequired)
            history = history.Where(tender => tender.Status == outcome);
        else
            outcome = string.Empty;

        var eligibleTenders = _db.Tenders
            .AsNoTracking()
            .Where(tender => tender.RequiresTopCommittee
                && (tender.Status == Statuses.TenderStatus.Awarded
                    || tender.Status == Statuses.TenderStatus.Rejected
                    || tender.Status == Statuses.TenderStatus.RetenderRequired));

        var tenderEntities = await history
            .Include(tender => tender.Requisition).ThenInclude(request => request.Department)
            .Include(tender => tender.AwardedSupplier)
            .Include(tender => tender.Bids).ThenInclude(bid => bid.SupplierProfile)
            .Include(tender => tender.TenderApprovals).ThenInclude(approval => approval.DecidedByUser)
            .Include(tender => tender.TenderApprovals).ThenInclude(approval => approval.SelectedBid)
                .ThenInclude(bid => bid!.SupplierProfile)
            .Include(tender => tender.TopCommitteeVotes).ThenInclude(vote => vote.MemberUser)
            .Include(tender => tender.TopCommitteeVotes).ThenInclude(vote => vote.SelectedBid)
                .ThenInclude(bid => bid!.SupplierProfile)
            .AsSplitQuery()
            .OrderByDescending(tender => tender.TenderApprovals
                .Select(approval => (DateTime?)approval.DecidedAt)
                .Max() ?? tender.CreatedAt)
            .ToListAsync();

        var tenderRows = tenderEntities.Select(tender =>
        {
            var decisions = tender.TenderApprovals
                .Select(approval => new TopCommitteeBidHistoryDecisionViewModel
                {
                    Stage = approval.Level == Statuses.ApprovalLevel.TopCommittee ? "Top Committee" : "Sourcing Manager",
                    Decision = approval.Decision,
                    DecidedBy = approval.DecidedByUser.FullName,
                    SelectedSupplier = approval.SelectedBid?.SupplierProfile.CompanyName,
                    Comment = approval.Comment,
                    DecidedAt = approval.DecidedAt
                })
                .Concat(tender.TopCommitteeVotes.Select(vote => new TopCommitteeBidHistoryDecisionViewModel
                {
                    Stage = "Committee vote",
                    Decision = vote.Vote,
                    DecidedBy = vote.MemberUser.FullName,
                    SelectedSupplier = vote.SelectedBid?.SupplierProfile.CompanyName,
                    Comment = vote.Comment ?? string.Empty,
                    DecidedAt = vote.VotedAt
                }))
                .OrderByDescending(decision => decision.DecidedAt)
                .ToList();

            return new TopCommitteeBidHistoryTenderViewModel
            {
                TenderId = tender.Id,
                TenderNumber = tender.TenderNumber,
                TenderTitle = tender.Title,
                TenderStatus = tender.Status,
                RequestNumber = tender.Requisition.RequestNumber,
                RequestTitle = tender.Requisition.Title,
                DepartmentName = tender.Requisition.Department.Name,
                AwardedSupplier = tender.AwardedSupplier?.CompanyName,
                CompletedAt = decisions.FirstOrDefault()?.DecidedAt ?? tender.CreatedAt,
                Bids = tender.Bids
                    .Where(bid => bid.SubmittedAt != null)
                    .OrderBy(bid => bid.TotalPrice)
                    .Select(bid => new TopCommitteeBidHistoryOfferViewModel
                    {
                        SupplierName = bid.SupplierProfile.CompanyName,
                        Status = bid.Status,
                        TotalPrice = bid.TotalPrice,
                        Currency = bid.Currency,
                        SubmittedAt = bid.SubmittedAt,
                        IsAwarded = tender.AwardedSupplierId == bid.SupplierProfileId
                    })
                    .ToList(),
                Decisions = decisions
            };
        }).ToList();

        var model = new TopCommitteeBidHistoryViewModel
        {
            Search = search?.Trim() ?? string.Empty,
            Outcome = outcome,
            CompletedTenderCount = await eligibleTenders.CountAsync(),
            AwardedTenderCount = await eligibleTenders.CountAsync(tender => tender.Status == Statuses.TenderStatus.Awarded),
            RejectedTenderCount = await eligibleTenders.CountAsync(tender => tender.Status == Statuses.TenderStatus.Rejected),
            RetenderTenderCount = await eligibleTenders.CountAsync(tender => tender.Status == Statuses.TenderStatus.RetenderRequired),
            SubmittedBidCount = await eligibleTenders.SelectMany(tender => tender.Bids)
                .CountAsync(bid => bid.SubmittedAt != null),
            Tenders = tenderRows
        };

        return View(model);
    }
}
