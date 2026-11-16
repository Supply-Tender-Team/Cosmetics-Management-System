using Microsoft.EntityFrameworkCore;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Bids;
using TenderSystem.Data.Entities.Requests;
using TenderSystem.Data.Entities.Tenders;

namespace TenderSystem.Areas.Sourcing.Controllers;

internal static class SourcingActivityOrder
{
    public static IQueryable<Tender> LatestActivityFirst(this IQueryable<Tender> tenders, ApplicationDbContext db)
    {
        var activity = db.Tenders.Select(t => new { t.Id, At = t.PublishDate ?? t.CreatedAt })
            .Concat(db.TenderApprovals.Select(a => new { Id = a.TenderId, At = a.DecidedAt }));
        var latest = activity.GroupBy(a => a.Id).Select(g => new { Id = g.Key, At = g.Max(a => a.At) });

        var rows = from tender in tenders
                   join current in latest on tender.Id equals current.Id
                   select new
                   {
                       Tender = tender,
                       current.At,
                       AuditAt = db.AuditLogs.Where(log =>
                               (log.EntityName == "Tender" && log.EntityId == tender.Id.ToString()
                                   && (log.Action == "Created" || log.Action == "Updated" || log.Action == "Published"
                                       || log.Action == "ReTendered" || log.Action == "TenderAwarded" || log.Action == "TenderRejected"))
                               || (log.EntityName == "EvaluationReport" && log.Action == "SubmittedToManagerReview"
                                   && tender.EvaluationReports.Any(report => report.Id.ToString() == log.EntityId)))
                           .Max(log => (DateTime?)log.CreatedAt)
                   };

        return rows.OrderByDescending(row => row.AuditAt > row.At ? row.AuditAt : row.At)
            .ThenByDescending(row => row.Tender.Id).Select(row => row.Tender);
    }

    public static IQueryable<PurchaseRequisition> LatestActivityFirst(this IQueryable<PurchaseRequisition> requests, ApplicationDbContext db)
    {
        var rows = from request in requests
                   select new
                   {
                       Request = request,
                       At = request.SubmittedAt ?? request.CreatedAt,
                       AuditAt = db.AuditLogs.Where(log => log.EntityName == "PurchaseRequisition"
                               && log.EntityId == request.Id.ToString())
                           .Max(log => (DateTime?)log.CreatedAt)
                   };

        return rows.OrderByDescending(row => row.AuditAt > row.At ? row.AuditAt : row.At)
            .ThenByDescending(row => row.Request.Id).Select(row => row.Request);
    }

    public static IQueryable<Bid> OrderByActivity(this IQueryable<Bid> bids, ApplicationDbContext db, bool descending = true)
    {
        var activity = db.Bids.Select(b => new { b.Id, At = b.SubmittedAt })
            .Concat(db.ComplianceChecks.Select(c => new { Id = c.BidId, At = (DateTime?)c.CheckedAt }))
            .Concat(db.TechnicalEvaluations.Select(e => new { Id = e.BidId, At = (DateTime?)e.EvaluatedAt }))
            .Concat(db.FinancialEvaluations.Select(e => new { Id = e.BidId, At = (DateTime?)e.EvaluatedAt }));
        var latest = activity.GroupBy(a => a.Id).Select(g => new { Id = g.Key, At = g.Max(a => a.At) });
        var rows = from bid in bids
                   join current in latest on bid.Id equals current.Id
                   select new { Bid = bid, current.At };

        var ordered = descending ? rows.OrderByDescending(row => row.At) : rows.OrderBy(row => row.At);
        return ordered.ThenByDescending(row => row.Bid.Id).Select(row => row.Bid);
    }
}

