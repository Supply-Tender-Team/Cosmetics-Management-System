using System.Data;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Evaluation.Services;
using TenderSystem.Data.Entities.Bids;
using TenderSystem.Data.Entities.Collaboration;
using TenderSystem.Data.Entities.Evaluation;
using TenderSystem.Data.Entities.Requests;
using TenderSystem.Data.Entities.Tenders;
using TenderSystem.Infrastructure;

namespace TenderSystem.Data.Seed;

public static partial class DemoDataSeeder
{
    private const string ReportPreviewNumber = "PREVIEW-REPORT-STYLE";

    // Explicit development preview only: normal startup never calls this method.
    public static async Task<int> SeedReportPreviewAsync(ApplicationDbContext db, EvaluationReportService reports, bool isDevelopment)
    {
        if (!isDevelopment) throw new InvalidOperationException("Report preview is Development only.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var existing = await db.Tenders.SingleOrDefaultAsync(t => t.TenderNumber == ReportPreviewNumber);
        if (existing != null) return existing.Id;
        var source = await db.Tenders.AsNoTracking().Include(t => t.CommitteeMembers)
            .Include(t => t.Requisition).ThenInclude(r => r.PurchaseRequisitionItems)
            .SingleAsync(t => t.TenderNumber == "DEMO-TND-002");
        if (source.CommitteeMembers.Select(m => m.UserId).Distinct().Count() != 4)
            throw new InvalidOperationException("Preview needs the four existing demo committee members.");
        var suppliers = await db.SupplierProfiles.Where(s => s.Status == Statuses.SupplierStatus.Approved && s.User.IsActive)
            .OrderBy(s => s.Id).Take(3).ToListAsync();
        if (suppliers.Count != 3) throw new InvalidOperationException("Preview needs three existing approved suppliers.");
        var now = DateTime.UtcNow;
        var request = new PurchaseRequisition
        {
            RequestNumber = ReportPreviewNumber, Title = "[PREVIEW ONLY] Report design review",
            Reason = "Temporary visual example, not a real purchase or committee decision.",
            Priority = Statuses.RequestPriority.Medium, TargetDate = now.AddDays(30),
            Status = Statuses.RequestStatus.ConvertedToTender, DepartmentId = source.Requisition.DepartmentId,
            RequestedByUserId = source.Requisition.RequestedByUserId, ReviewedByUserId = source.CreatedByUserId,
            EstimatedBudget = 5000, CreatedAt = now, SubmittedAt = now
        };
        foreach (var item in source.Requisition.PurchaseRequisitionItems)
            request.PurchaseRequisitionItems.Add(new PurchaseRequisitionItem
            { ItemName = item.ItemName, Quantity = item.Quantity, Unit = item.Unit, Specifications = item.Specifications });
        var tender = new Tender
        {
            TenderNumber = ReportPreviewNumber, Requisition = request,
            Title = "[PREVIEW ONLY] Completed packaging evaluation report",
            Description = "Synthetic completed evaluations for reviewing the report layout. All confirmations below are test fixtures, not real decisions.",
            Terms = "Preview only. Prices in JOD. Shipping included in totals.",
            Status = Statuses.TenderStatus.UnderEvaluation, PublishDate = now.AddDays(-7),
            SubmissionDeadline = now.AddDays(-1), EstimatedValue = 5000, CreatedByUserId = source.CreatedByUserId, CreatedAt = now
        };
        foreach (var criterion in CreateDemoEvaluationCriteria()) tender.EvaluationCriteria.Add(criterion);
        foreach (var member in source.CommitteeMembers)
            tender.CommitteeMembers.Add(new CommitteeMember { UserId = member.UserId, MemberRole = member.MemberRole });
        for (var i = 0; i < suppliers.Count; i++)
        {
            var bid = new Bid
            {
                SupplierProfileId = suppliers[i].Id, Status = Statuses.BidStatus.Compliant, Currency = "JOD",
                DeliveryDays = 7 + i * 3, ValidityDays = 30, PaymentTerms = "Payment within 30 days",
                TechnicalSummary = "[PREVIEW ONLY] Meets the requested packaging specifications.", SubmittedAt = now.AddDays(-2)
            };
            foreach (var item in request.PurchaseRequisitionItems)
                bid.BidItems.Add(new BidItem
                { RequisitionItem = item, Quantity = item.Quantity, UnitPrice = 1.5m + i * 0.25m,
                    ShippingCost = 25, OfferedSpecifications = item.Specifications });
            bid.TotalPrice = bid.BidItems.Sum(item => item.Quantity * item.UnitPrice + item.ShippingCost);
            bid.ComplianceChecks.Add(new ComplianceCheck
            { CheckedByUserId = source.CreatedByUserId, Result = Statuses.ComplianceResult.Compliant,
                Reason = "[PREVIEW ONLY] Synthetic compliance approval.", CheckedAt = now });
            var memberIndex = 0;
            foreach (var member in source.CommitteeMembers.OrderBy(m => m.MemberRole))
            {
                foreach (var criterion in tender.EvaluationCriteria.Where(c => c.Type == Statuses.CriterionType.Technical))
                    bid.TechnicalEvaluations.Add(new TechnicalEvaluation
                    { Criterion = criterion, EvaluatorUserId = member.UserId, Score = 4.5m - i * 0.5m - memberIndex * 0.25m,
                        Notes = "[PREVIEW] Specifications are supported by the sample offer; delivery and quality controls were considered.", EvaluatedAt = now });
                bid.FinancialEvaluations.Add(new FinancialEvaluation
                { EvaluatorUserId = member.UserId, Score = decimal.Round(20m * (suppliers.Count - i) / suppliers.Count, 2),
                    TotalCost = bid.TotalPrice, LogisticsCost = bid.BidItems.Sum(item => item.ShippingCost),
                    Notes = "[PREVIEW] Shipping is included; payment terms are 30 days.", EvaluatedAt = now, ReadyAt = now });
                memberIndex++;
            }
            tender.Bids.Add(bid);
        }
        db.Tenders.Add(tender);
        await db.SaveChangesAsync();
        foreach (var member in source.CommitteeMembers)
            db.Notifications.Add(new Notification
            { UserId = member.UserId, Type = "TenderEvaluation", Title = "[PREVIEW ONLY] Completed report",
                Message = tender.Description, Link = "/Sourcing/CommitteeBrief/Index/" + tender.Id, CreatedAt = now });
        await db.SaveChangesAsync();
        var preparedBy = source.CommitteeMembers.Single(m => m.MemberRole == Statuses.CommitteeRole.Technical).UserId;
        if (!await reports.TryCreateAsync(tender.Id, preparedBy, "/Sourcing/Evaluation?tenderId=" + tender.Id))
            throw new InvalidOperationException("The preview did not satisfy the real report completion checks.");
        await transaction.CommitAsync();
        return tender.Id;
    }

    public static async Task RemoveReportPreviewAsync(ApplicationDbContext db, bool isDevelopment)
    {
        if (!isDevelopment) throw new InvalidOperationException("Report preview is Development only.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var tender = await db.Tenders.AsNoTracking().SingleOrDefaultAsync(t => t.TenderNumber == ReportPreviewNumber);
        if (tender == null) return;
        if (await db.Tenders.AnyAsync(t => t.Id == tender.Id && (t.TenderApprovals.Any() || t.TopCommitteeVotes.Any()
            || t.Clarifications.Any() || t.AiBidAnalyses.Any() || t.Bids.Any(b => b.BidDocuments.Any()))))
            throw new InvalidOperationException("The preview has additional activity; review dependencies before cleanup.");
        var bidIds = await db.Bids.Where(b => b.TenderId == tender.Id).Select(b => b.Id).ToListAsync();
        await db.Notifications.Where(n => (n.Type == "TenderEvaluation" && n.Link == "/Sourcing/CommitteeBrief/Index/" + tender.Id)
            || (n.Type == "EvaluationReportReady" && (n.Link == "/Evaluation/Report/Index/" + tender.Id
                || n.Link == "/Sourcing/Tenders/Details/" + tender.Id + "?tab=ranking"
                || n.Link == "/Sourcing/Evaluation?tenderId=" + tender.Id))).ExecuteDeleteAsync();
        await db.EvaluationReports.Where(r => r.TenderId == tender.Id).ExecuteDeleteAsync();
        await db.TechnicalEvaluations.Where(e => bidIds.Contains(e.BidId)).ExecuteDeleteAsync();
        await db.FinancialEvaluations.Where(e => bidIds.Contains(e.BidId)).ExecuteDeleteAsync();
        await db.ComplianceChecks.Where(c => bidIds.Contains(c.BidId)).ExecuteDeleteAsync();
        await db.BidItems.Where(i => bidIds.Contains(i.BidId)).ExecuteDeleteAsync();
        await db.Bids.Where(b => b.TenderId == tender.Id).ExecuteDeleteAsync();
        await db.CommitteeMembers.Where(m => m.TenderId == tender.Id).ExecuteDeleteAsync();
        await db.EvaluationCriteria.Where(c => c.TenderId == tender.Id).ExecuteDeleteAsync();
        await db.Tenders.Where(t => t.Id == tender.Id).ExecuteDeleteAsync();
        await db.PurchaseRequisitionItems.Where(i => i.RequisitionId == tender.RequisitionId).ExecuteDeleteAsync();
        await db.PurchaseRequisitions.Where(r => r.Id == tender.RequisitionId && r.RequestNumber == ReportPreviewNumber).ExecuteDeleteAsync();
        // Retain audit history. No existing account, supplier or original tender is removed.
        await transaction.CommitAsync();
    }
}
