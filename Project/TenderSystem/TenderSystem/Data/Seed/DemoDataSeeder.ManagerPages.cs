using Microsoft.EntityFrameworkCore;
using System.Text;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Bids;
using TenderSystem.Data.Entities.Collaboration;
using TenderSystem.Data.Entities.Evaluation;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Data.Entities.Requests;
using TenderSystem.Data.Entities.Tenders;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Files;

namespace TenderSystem.Data.Seed;

public static partial class DemoDataSeeder
{
    private static async Task SeedManagerPageSamplesAsync(IServiceProvider services, ApplicationDbContext db)
    {
        var hasPendingSample = await db.Tenders.AnyAsync(tender => tender.TenderNumber == "DEMO-TND-MGR-001");
        var hasTopSample = await db.Tenders.AnyAsync(tender => tender.TenderNumber == "DEMO-TND-MGR-002");
        var hasPendingSampleThree = await db.Tenders.AnyAsync(tender => tender.TenderNumber == "DEMO-TND-MGR-003");
        var hasPendingSampleFour = await db.Tenders.AnyAsync(tender => tender.TenderNumber == "DEMO-TND-MGR-004");
        if (hasPendingSample && hasTopSample && hasPendingSampleThree && hasPendingSampleFour)
        {
            await SeedManagerBidDocumentsAsync(services, db);
            return;
        }

        var manager = await db.Users.SingleOrDefaultAsync(user => user.Email == "manager@lumiere.local");
        // The manager reviews the report; the 4th committee seat belongs to an Evaluation Committee account.
        var sourcingSeat = await db.Users.SingleOrDefaultAsync(user => user.Email == "demo-sourcing-representative@lumiere.local");
        var departmentSeat = await db.Users.SingleOrDefaultAsync(user => user.Email == "demo-department-manager@lumiere.local");
        var requester = await db.Users.SingleOrDefaultAsync(user => user.Email == "requester@lumiere.local");
        var officer = await db.Users.SingleOrDefaultAsync(user => user.Email == "officer@lumiere.local");
        var technicalEvaluator = await db.Users.SingleOrDefaultAsync(user => user.Email == "evaluator@lumiere.local");
        var financialEvaluator = await db.Users.SingleOrDefaultAsync(user => user.Email == "financial@demo.local");
        var supplierUsers = await db.Users
            .Where(user => user.Email == "supplier@bottlepro.local" || user.Email == "supplier2@demo.local")
            .ToListAsync();

        if (manager is null || sourcingSeat is null || departmentSeat is null || requester?.DepartmentId is null || officer is null
            || technicalEvaluator is null || financialEvaluator is null || supplierUsers.Count != 2)
            throw new InvalidOperationException("Manager page demo data requires the existing demo users and suppliers.");

        var suppliers = await db.SupplierProfiles
            .Where(supplier => supplierUsers.Select(user => user.Id).Contains(supplier.UserId))
            .ToListAsync();
        if (suppliers.Count != 2 || suppliers.Any(supplier => supplier.Status != Statuses.SupplierStatus.Approved))
            throw new InvalidOperationException("Manager page demo data requires two approved demo suppliers.");

        var supplierOne = suppliers.Single(supplier => supplier.UserId == supplierUsers.Single(user => user.Email == "supplier@bottlepro.local").Id);
        var supplierTwo = suppliers.Single(supplier => supplier.UserId == supplierUsers.Single(user => user.Email == "supplier2@demo.local").Id);
        var now = DateTime.UtcNow;

        await using var transaction = await db.Database.BeginTransactionAsync();
        var pendingTenders = new List<Tender>();

        if (!hasPendingSample)
        {
            var pendingTender = CreateManagerSampleTender(
                "DEMO-TND-MGR-001", "DEMO-PR-MGR-001", "Luxury cream jars and pump bottles",
                manager, sourcingSeat, departmentSeat, requester, officer, technicalEvaluator, financialEvaluator,
                supplierOne, supplierTwo, now, awaitingTop: false);
            db.Tenders.Add(pendingTender);
            pendingTenders.Add(pendingTender);
        }

        if (!hasTopSample)
        {
            var topTender = CreateManagerSampleTender(
                "DEMO-TND-MGR-002", "DEMO-PR-MGR-002", "Premium skincare packaging set",
                manager, sourcingSeat, departmentSeat, requester, officer, technicalEvaluator, financialEvaluator,
                supplierOne, supplierTwo, now, awaitingTop: true);
            db.Tenders.Add(topTender);
        }

        if (!hasPendingSampleThree)
        {
            var pendingTender = CreateManagerSampleTender(
                "DEMO-TND-MGR-003", "DEMO-PR-MGR-003", "Rose water serum bottles and cartons",
                manager, sourcingSeat, departmentSeat, requester, officer, technicalEvaluator, financialEvaluator,
                supplierOne, supplierTwo, now.AddMinutes(1), awaitingTop: false);
            db.Tenders.Add(pendingTender);
            pendingTenders.Add(pendingTender);
        }

        if (!hasPendingSampleFour)
        {
            var pendingTender = CreateManagerSampleTender(
                "DEMO-TND-MGR-004", "DEMO-PR-MGR-004", "Travel skincare tubes and labels",
                manager, sourcingSeat, departmentSeat, requester, officer, technicalEvaluator, financialEvaluator,
                supplierOne, supplierTwo, now.AddMinutes(2), awaitingTop: false);
            db.Tenders.Add(pendingTender);
            pendingTenders.Add(pendingTender);
        }

        await db.SaveChangesAsync();

        foreach (var pendingTender in pendingTenders)
        {
            db.Notifications.Add(new Notification
            {
                UserId = manager.Id,
                Type = "EvaluationReportForManager",
                Title = "Demo evaluation report ready",
                Message = $"A complete sample evaluation report for {pendingTender.TenderNumber} is ready for manager review.",
                Link = $"/Manager/Reports?id={pendingTender.Id}",
                IsRead = false,
                CreatedAt = pendingTender.CreatedAt
            });
            await db.SaveChangesAsync();
        }

        await transaction.CommitAsync();
        await SeedManagerBidDocumentsAsync(services, db);
    }

    /// <summary>
    /// Adds one real, valid PDF to each demo offer so Manager and Top Committee
    /// document-preview/download flows can be tested without a supplier upload.
    /// The check by file name keeps this seed idempotent across app restarts.
    /// </summary>
    private static async Task SeedManagerBidDocumentsAsync(IServiceProvider services, ApplicationDbContext db)
    {
        var tenders = await db.Tenders
            .Where(tender => tender.TenderNumber.StartsWith("DEMO-TND-MGR-"))
            .Include(tender => tender.Bids)
                .ThenInclude(bid => bid.BidDocuments)
            .ToListAsync();

        var bids = tenders.SelectMany(tender => tender.Bids).ToList();
        var bidsWithoutDemoDocument = bids
            .Where(bid => !bid.BidDocuments.Any(document => document.FileName.StartsWith("demo-offer-", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (bidsWithoutDemoDocument.Count == 0) return;

        var files = services.GetRequiredService<IFileStorageService>();
        foreach (var bid in bidsWithoutDemoDocument)
        {
            var tenderNumber = tenders.First(tender => tender.Bids.Any(item => item.Id == bid.Id)).TenderNumber;
            var fileName = $"demo-offer-{tenderNumber}-{bid.Id}.pdf";
            await using var content = new MemoryStream(CreateDemoOfferPdf(tenderNumber));
            var stored = await files.SaveAsync(content, fileName, "application/pdf");

            db.BidDocuments.Add(new BidDocument
            {
                BidId = bid.Id,
                DocType = "Offer document",
                FileName = fileName,
                FilePath = stored.RelativePath,
                ContentType = stored.ContentType,
                SizeBytes = stored.SizeBytes,
                UploadedAt = DateTime.UtcNow.AddDays(-2)
            });
        }

        await db.SaveChangesAsync();
    }

    private static byte[] CreateDemoOfferPdf(string tenderNumber)
    {
        var streamText = $"BT /F1 18 Tf 72 720 Td (Demo offer attachment) Tj 0 -32 Td /F1 12 Tf (Tender: {tenderNumber}) Tj 0 -24 Td (Seeded PDF for secure preview and watermark testing.) Tj ET";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {streamText.Length} >>\nstream\n{streamText}\nendstream"
        };

        using var output = new MemoryStream();
        using var writer = new StreamWriter(output, new UTF8Encoding(false), leaveOpen: true);
        writer.Write("%PDF-1.4\n%\u00e2\u00e3\u00cf\u00d3\n");
        writer.Flush();
        var offsets = new List<long> { 0 };
        foreach (var pdfObject in objects)
        {
            offsets.Add(output.Position);
            writer.Write($"{offsets.Count - 1} 0 obj\n{pdfObject}\nendobj\n");
            writer.Flush();
        }

        var xrefOffset = output.Position;
        writer.Write($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        for (var index = 1; index < offsets.Count; index++)
            writer.Write($"{offsets[index]:D10} 00000 n \n");
        writer.Write($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        writer.Flush();
        return output.ToArray();
    }

    private static Tender CreateManagerSampleTender(
        string tenderNumber,
        string requestNumber,
        string title,
        ApplicationUser manager,
        ApplicationUser sourcingSeat,
        ApplicationUser departmentSeat,
        ApplicationUser requester,
        ApplicationUser officer,
        ApplicationUser technicalEvaluator,
        ApplicationUser financialEvaluator,
        SupplierProfile supplierOne,
        SupplierProfile supplierTwo,
        DateTime now,
        bool awaitingTop)
    {
        var request = new PurchaseRequisition
        {
            RequestNumber = requestNumber,
            Title = title,
            Reason = "Development-only sample for reviewing the manager evaluation workflow.",
            Priority = Statuses.RequestPriority.High,
            TargetDate = now.Date.AddDays(40),
            Status = Statuses.RequestStatus.ConvertedToTender,
            DepartmentId = requester.DepartmentId!.Value,
            RequestedByUserId = requester.Id,
            ReviewedByUserId = officer.Id,
            ReviewNote = "Demo request approved for procurement.",
            EstimatedBudget = 12000m,
            CreatedAt = now.AddDays(-12),
            SubmittedAt = now.AddDays(-11)
        };
        request.PurchaseRequisitionItems.Add(new PurchaseRequisitionItem
        {
            ItemName = "Glass cream jar, 50 ml", Quantity = 800, Unit = "Piece",
            Specifications = "Clear glass, smooth rim, compatible with the specified screw cap."
        });
        request.PurchaseRequisitionItems.Add(new PurchaseRequisitionItem
        {
            ItemName = "Lotion pump bottle, 100 ml", Quantity = 800, Unit = "Piece",
            Specifications = "Amber PET bottle with leak-resistant lotion pump."
        });

        var tender = new Tender
        {
            TenderNumber = tenderNumber,
            Requisition = request,
            Title = title,
            Description = "Sample tender with two evaluated supplier offers and a complete committee result.",
            Terms = "Prices are in JOD. Delivery is to the Amman warehouse.",
            Status = awaitingTop ? Statuses.TenderStatus.AwaitingCommitteeDecision : Statuses.TenderStatus.AwaitingManagerReview,
            RequiresTopCommittee = true,
            PublishDate = now.AddDays(-9),
            SubmissionDeadline = now.AddDays(-2),
            EstimatedValue = 12000m,
            CreatedByUserId = officer.Id,
            CreatedAt = now.AddDays(-10)
        };

        var technicalCriteria = new List<EvaluationCriterion>();
        var criterionNames = new[]
        {
            "Relevant cosmetics packaging experience",
            "Product specifications and quantity compliance",
            "Sample approval and quality control",
            "Traceability and corrective actions",
            "Production capacity and delivery reliability"
        };
        foreach (var name in criterionNames)
        {
            var criterion = new EvaluationCriterion
            {
                Name = name, Type = Statuses.CriterionType.Technical,
                WeightPercent = 16m, MaxScore = 5, IsLocked = true
            };
            tender.EvaluationCriteria.Add(criterion);
            technicalCriteria.Add(criterion);
        }
        tender.EvaluationCriteria.Add(new EvaluationCriterion
        {
            Name = "Eligible price ranking", Type = Statuses.CriterionType.Financial,
            WeightPercent = 20m, MaxScore = 20, IsLocked = true
        });

        var committee = new[]
        {
            (User: sourcingSeat, Role: Statuses.CommitteeRole.SourcingManager),
            (User: departmentSeat, Role: Statuses.CommitteeRole.Department),
            (User: financialEvaluator, Role: Statuses.CommitteeRole.Financial),
            (User: technicalEvaluator, Role: Statuses.CommitteeRole.Technical)
        };
        foreach (var member in committee)
            tender.CommitteeMembers.Add(new CommitteeMember { User = member.User, MemberRole = member.Role });

        var offers = new[] { supplierOne, supplierTwo };
        var bids = new List<Bid>();
        for (var offerIndex = 0; offerIndex < offers.Length; offerIndex++)
        {
            var supplier = offers[offerIndex];
            tender.TenderSuppliers.Add(new TenderSupplier
            {
                SupplierProfile = supplier,
                AddedAt = now.AddDays(-8),
                ParticipationStatus = Statuses.ParticipationStatus.Participating
            });
            tender.Invitations.Add(new Invitation
            {
                SupplierProfile = supplier,
                Status = Statuses.InvitationStatus.Accepted,
                SentAt = now.AddDays(-8), ExpiresAt = tender.SubmissionDeadline,
                RespondedAt = now.AddDays(-7)
            });

            var bid = new Bid
            {
                SupplierProfile = supplier,
                Status = Statuses.BidStatus.Compliant,
                Currency = "JOD",
                DeliveryDays = offerIndex == 0 ? 12 : 16,
                ValidityDays = 60,
                PaymentTerms = "Net 30 days after accepted delivery.",
                TechnicalSummary = offerIndex == 0
                    ? "Premium glass and pump packaging with documented batch controls."
                    : "Cost-effective packaging with a longer delivery lead time.",
                SubmittedAt = now.AddDays(-5)
            };

            var itemIndex = 0;
            foreach (var requestItem in request.PurchaseRequisitionItems)
            {
                var unitPrice = itemIndex == 0
                    ? offerIndex == 0 ? 8.50m : 8.90m
                    : offerIndex == 0 ? 2.25m : 2.45m;
                bid.BidItems.Add(new BidItem
                {
                    RequisitionItem = requestItem,
                    Quantity = requestItem.Quantity,
                    UnitPrice = unitPrice,
                    TaxAmount = 0,
                    ShippingCost = 50m,
                    OfferedSpecifications = requestItem.Specifications
                });
                itemIndex++;
            }
            bid.TotalPrice = bid.BidItems.Sum(item => item.UnitPrice * item.Quantity
                + item.TaxAmount + item.ShippingCost + (item.InstallationCost ?? 0m));
            bid.ComplianceChecks.Add(new ComplianceCheck
            {
                CheckedByUserId = officer.Id,
                Result = Statuses.ComplianceResult.Compliant,
                Reason = "Mandatory packaging, sample and delivery requirements were checked.",
                CheckedAt = now.AddDays(-4)
            });

            for (var memberIndex = 0; memberIndex < committee.Length; memberIndex++)
            {
                var member = committee[memberIndex];
                for (var criterionIndex = 0; criterionIndex < technicalCriteria.Count; criterionIndex++)
                {
                    var score = 4.35m - offerIndex * 0.35m - criterionIndex * 0.04m + memberIndex * 0.02m;
                    bid.TechnicalEvaluations.Add(new TechnicalEvaluation
                    {
                        Criterion = technicalCriteria[criterionIndex],
                        EvaluatorUserId = member.User.Id,
                        Score = decimal.Round(score, 2),
                        Notes = criterionIndex == 0
                            ? $"{member.User.FullName}: relevant examples and supporting details reviewed."
                            : $"{member.User.FullName}: evidence and delivery details reviewed.",
                        EvaluatedAt = now.AddHours(-20 + memberIndex)
                    });
                }
                bid.FinancialEvaluations.Add(new FinancialEvaluation
                {
                    EvaluatorUserId = member.User.Id,
                    Score = offerIndex == 0 ? 20m : 18.98m,
                    TotalCost = bid.TotalPrice,
                    TaxesAndFees = 0m,
                    LogisticsCost = 100m,
                    Notes = offerIndex == 0
                        ? "Comparable total includes item pricing and delivery."
                        : "Higher total price; delivery remains within the requested window.",
                    EvaluatedAt = now.AddHours(-18 + memberIndex),
                    ReadyAt = now.AddHours(-16 + memberIndex)
                });
            }

            tender.Bids.Add(bid);
            bids.Add(bid);
        }

        var recommendedBid = bids[0];
        var report = new EvaluationReport
        {
            Tender = tender,
            Status = awaitingTop ? Statuses.ReportStatus.ManagerApproved : Statuses.ReportStatus.RecommendationPending,
            RecommendedBid = recommendedBid,
            PreparedByUserId = technicalEvaluator.Id,
            CreatedAt = now.AddHours(-3),
            SubmittedAt = now.AddHours(-3),
            TechnicalSummary = $"{tenderNumber}: four committee members completed five locked technical criteria for each of two compliant offers. Average technical scores: Offer 1 68.8/80; Offer 2 63.2/80.",
            FinancialSummary = $"Offer 1: {bids[0].TotalPrice:N2} JOD, financial score 20/20. Offer 2: {bids[1].TotalPrice:N2} JOD, financial score 18.98/20. Both offers include itemized costs and delivery terms.",
            Justification = awaitingTop
                ? "The Sourcing Manager reviewed the committee report and selected the highest-ranked eligible offer for Top Committee review."
                : "The committee results are complete. The Sourcing Manager should review the evidence and forward the committee-selected offer or return the report for correction."
        };
        tender.EvaluationReports.Add(report);

        if (awaitingTop)
        {
            tender.TenderApprovals.Add(new TenderApproval
            {
                Tender = tender,
                Report = report,
                DecidedByUserId = manager.Id,
                Level = Statuses.ApprovalLevel.Manager,
                Decision = Statuses.ApprovalDecision.Approved,
                SelectedBid = bids[0],
                Comment = "The leading compliant offer matches the requested specifications and delivery plan.",
                DecidedAt = now.AddHours(-1)
            });
        }

        return tender;
    }
}
