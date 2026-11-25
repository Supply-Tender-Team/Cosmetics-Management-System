using System.Data;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Evaluation.Services;
using TenderSystem.Data.Entities.Bids;
using TenderSystem.Data.Entities.Collaboration;
using TenderSystem.Data.Entities.Evaluation;
using TenderSystem.Data.Entities.Requests;
using TenderSystem.Data.Entities.Tenders;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Audit;

namespace TenderSystem.Data.Seed;

public static partial class DemoDataSeeder
{
    private static List<EvaluationCriterion> CreateDemoEvaluationCriteria()
    {
        var names = new[] { "Match to requested specifications", "Relevant supplier experience", "Material suitability", "Quality and sample controls", "Delivery plan", "After-sales support" };
        var weights = new decimal[] { 24, 12, 8, 16, 12, 8 };
        var criteria = new List<EvaluationCriterion>();
        for (var i = 0; i < names.Length; i++)
            criteria.Add(new EvaluationCriterion { Name = names[i], Type = Statuses.CriterionType.Technical,
                WeightPercent = weights[i], MaxScore = 5, IsLocked = true });
        criteria.Add(new EvaluationCriterion { Name = "Commercial terms and total cost assessment", Type = Statuses.CriterionType.Financial,
            WeightPercent = 20, MaxScore = 5, IsLocked = true });
        return criteria;
    }

    // Add missing scenarios only. Never reset work, confirmations, reports or audit history.
    private static async Task SeedTeamScenariosAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var audit = services.GetRequiredService<AuditService>();
        var reports = services.GetRequiredService<EvaluationReportService>();
        var requester = await db.Users.SingleAsync(u => u.Email == "requester@lumiere.local");
        var officer = await db.Users.SingleAsync(u => u.Email == "officer@lumiere.local");
        var evaluator = await db.Users.SingleAsync(u => u.Email == "evaluator@lumiere.local");
        var financial = await db.Users.SingleAsync(u => u.Email == "financial@demo.local");
        // The 4th committee seat belongs to an Evaluation Committee account: the Sourcing Manager only reviews the finished report.
        var sourcingSeat = await db.Users.SingleAsync(u => u.Email == "demo-sourcing-representative@lumiere.local");
        var supplierEmails = new[] { "supplier@bottlepro.local", "supplier2@demo.local", "supplier3@demo.local" };
        var suppliers = await db.SupplierProfiles.Include(s => s.User)
            .Where(s => supplierEmails.Contains(s.User.Email!)).OrderBy(s => s.User.Email).ToListAsync();
        if (requester.DepartmentId == null || suppliers.Count != 3
            || suppliers.Any(s => s.Status != Statuses.SupplierStatus.Approved || !s.User.IsActive))
            throw new InvalidOperationException("Current demo scenarios require the active demo requester and three approved demo suppliers.");
        var memberIds = new[] { requester.Id, financial.Id, evaluator.Id, sourcingSeat.Id };
        var seats = new[] { Statuses.CommitteeRole.Department, Statuses.CommitteeRole.Financial,
            Statuses.CommitteeRole.Technical, Statuses.CommitteeRole.SourcingManager };
        if (memberIds.Distinct().Count() != 4)
            throw new InvalidOperationException("Demo evaluation requires four different committee accounts.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var numbers = new[] { "DEMO-FLOW-001", "DEMO-EVL-001", "DEMO-REPORT-001" };
        for (var stage = 0; stage < numbers.Length; stage++)
        {
            var number = numbers[stage];
            if (await db.Tenders.AnyAsync(t => t.TenderNumber == number)) continue;
            if (await db.PurchaseRequisitions.AnyAsync(r => r.RequestNumber == number))
                throw new InvalidOperationException("An incomplete demo request already uses " + number + ". Review it without deleting existing work.");
            var now = DateTime.UtcNow;
            var request = new PurchaseRequisition
            {
                RequestNumber = number, Title = "[DEMO] Packaging test " + number,
                Reason = "Development team fixture, not a real purchasing decision.",
                Priority = Statuses.RequestPriority.Medium, TargetDate = now.AddDays(45),
                Status = Statuses.RequestStatus.ConvertedToTender, DepartmentId = requester.DepartmentId.Value,
                RequestedByUserId = requester.Id, ReviewedByUserId = officer.Id,
                EstimatedBudget = 5000, CreatedAt = now.AddDays(-8), SubmittedAt = now.AddDays(-7)
            };
            request.PurchaseRequisitionItems.Add(new PurchaseRequisitionItem
            { ItemName = "Glass bottles 100 ml", Quantity = 1000, Unit = "Piece", Specifications = "Clear glass bottles with compatible pump caps." });
            request.PurchaseRequisitionItems.Add(new PurchaseRequisitionItem
            { ItemName = "Pump caps", Quantity = 1000, Unit = "Piece", Specifications = "Leak resistant caps for 100 ml bottles." });
            var tender = new Tender
            {
                TenderNumber = number, Requisition = request,
                Title = stage == 0 ? "[DEMO] Review compliance and send to committee" : stage == 1
                    ? "[DEMO] Evaluate and confirm Ready - four members" : "[DEMO] Completed committee report - Officer review",
                Description = "Development fixture: glass bottles and pump caps. Existing demo scenarios are never reset at startup.",
                Terms = "Prices in JOD, including shipping. Payment within 30 days.",
                Status = stage == 0 ? Statuses.TenderStatus.Published : Statuses.TenderStatus.UnderEvaluation,
                PublishDate = now.AddDays(-6), SubmissionDeadline = stage == 0 ? now.AddDays(14) : now.AddDays(-1),
                EstimatedValue = 5000, CreatedByUserId = officer.Id, CreatedAt = now
            };
            foreach (var criterion in CreateDemoEvaluationCriteria()) tender.EvaluationCriteria.Add(criterion);
            for (var m = 0; m < memberIds.Length; m++)
                tender.CommitteeMembers.Add(new CommitteeMember { UserId = memberIds[m], MemberRole = seats[m] });
            for (var i = 0; i < suppliers.Count; i++)
            {
                tender.TenderSuppliers.Add(new TenderSupplier { SupplierProfileId = suppliers[i].Id,
                    AddedAt = now.AddDays(-5), ParticipationStatus = Statuses.ParticipationStatus.Participating });
                tender.Invitations.Add(new Invitation { SupplierProfileId = suppliers[i].Id,
                    Status = Statuses.InvitationStatus.Accepted, SentAt = now.AddDays(-5),
                    RespondedAt = now.AddDays(-4), ExpiresAt = tender.SubmissionDeadline });
                var bid = new Bid
                {
                    SupplierProfileId = suppliers[i].Id, Status = stage == 0 ? Statuses.BidStatus.Submitted : Statuses.BidStatus.Compliant,
                    Currency = "JOD", DeliveryDays = 7 + i * 3, ValidityDays = 60,
                    PaymentTerms = "Payment within 30 days of delivery.",
                    TechnicalSummary = "[DEMO] Packaging offer with bottles, caps and delivery plan.", SubmittedAt = now.AddDays(-2)
                };
                foreach (var item in request.PurchaseRequisitionItems)
                    bid.BidItems.Add(new BidItem { RequisitionItem = item, Quantity = item.Quantity,
                        UnitPrice = (item.ItemName.StartsWith("Glass") ? 1.5m : 0.5m) + i * 0.25m,
                        ShippingCost = 25, OfferedSpecifications = item.Specifications });
                bid.TotalPrice = bid.BidItems.Sum(item => item.Quantity * item.UnitPrice + item.ShippingCost);
                if (stage > 0)
                    bid.ComplianceChecks.Add(new ComplianceCheck { CheckedByUserId = officer.Id,
                        Result = Statuses.ComplianceResult.Compliant, Reason = "[DEMO] Mandatory specifications reviewed.", CheckedAt = now });
                // The editable scenario starts empty; only the completed-report fixture has synthetic confirmations.
                if (stage == 2)
                    for (var m = 0; m < memberIds.Length; m++)
                    {
                        foreach (var criterion in tender.EvaluationCriteria)
                            bid.TechnicalEvaluations.Add(new TechnicalEvaluation { Criterion = criterion,
                                EvaluatorUserId = memberIds[m], Score = 4.5m - i * 0.5m - m * 0.25m,
                                Notes = "[DEMO] Specifications and delivery plan reviewed.", EvaluatedAt = now });
                        bid.FinancialEvaluations.Add(new FinancialEvaluation { EvaluatorUserId = memberIds[m],
                            Score = decimal.Round((4.5m - i * 0.5m - m * 0.25m) / 5 * 20, 2),
                            TotalCost = bid.TotalPrice, LogisticsCost = 50, Notes = "[DEMO] Shipping included in supplier total.",
                            ReadyAt = now, EvaluatedAt = now, CompletedAt = now });
                    }
                tender.Bids.Add(bid);
            }
            db.Tenders.Add(tender);
            await db.SaveChangesAsync();
            if (stage > 0)
                foreach (var memberId in memberIds)
                    db.Notifications.Add(new Notification { UserId = memberId, Type = "TenderEvaluation",
                        Title = "[DEMO] " + number + " assigned to committee", Message = tender.Title,
                        Link = "/Sourcing/CommitteeBrief/Index/" + tender.Id, CreatedAt = now });
            await db.SaveChangesAsync();
            if (stage == 2) await SeedCompletedRecommendationsAsync(db, tender.Id, now);
            if (stage == 2 && !await reports.TryCreateAsync(tender.Id, evaluator.Id, "/Sourcing/Evaluation?tenderId=" + tender.Id, "/Sourcing/CommitteeBrief/Index/" + tender.Id))
                throw new InvalidOperationException("Completed demo report did not satisfy the real Ready checks.");
            await audit.LogForUserAsync(officer.Id, "DemoScenarioCreated", "Tender", tender.Id.ToString(),
                "Development fixture " + number + "; synthetic sample data, not a user evaluation.");
        }
        await transaction.CommitAsync();
    }
    // Only for a newly-created, explicitly completed development fixture; never called for live evaluations.
    private static async Task SeedCompletedRecommendationsAsync(ApplicationDbContext db, int tenderId, DateTime now)
    {
        var memberIds = await db.CommitteeMembers.Where(m => m.TenderId == tenderId).Select(m => m.UserId).ToListAsync();
        var bidId = await db.Bids.Where(b => b.TenderId == tenderId).OrderBy(b => b.TotalPrice).Select(b => b.Id).FirstAsync();
        foreach (var memberId in memberIds)
            db.EvaluationCommitteeSubmissions.Add(new EvaluationCommitteeSubmission
            { TenderId = tenderId, EvaluatorUserId = memberId, ReadyAt = now, RecommendedBidId = bidId, VotedAt = now });
        await db.SaveChangesAsync();
    }
}
