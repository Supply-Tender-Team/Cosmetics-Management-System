using DotNetEnv;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Data.Entities.Bids;
using TenderSystem.Data.Entities.Evaluation;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Data.Entities.Requests;
using TenderSystem.Data.Entities.Tenders;
using TenderSystem.Infrastructure;

namespace TenderSystem.Data.Seed;

public static partial class DemoDataSeeder
{
    // Preserve legacy audit fixtures and add missing current team scenarios.
    public static async Task SeedAsync(IServiceProvider services, bool isDevelopment)
    {
        if (!isDevelopment || !Env.GetBool("SEED_DEMO_DATA", false)) return;
        var db = services.GetRequiredService<ApplicationDbContext>();
        var scenarioNumbers = new[] { "DEMO-FLOW-001", "DEMO-EVL-001", "DEMO-REPORT-001" };
        if (await db.PurchaseRequisitions.AnyAsync(r => r.RequestNumber == "DEMO-PR-001")
            && await db.Tenders.CountAsync(t => scenarioNumbers.Contains(t.TenderNumber)) == scenarioNumbers.Length)
        {
            await SeedManagerPageSamplesAsync(services, db);
            await SeedTestingLabAsync(services, isDevelopment);
            return;
        }
        await SeedOriginalAsync(services, isDevelopment);
        await SeedTeamScenariosAsync(services);
        await SeedManagerPageSamplesAsync(services, db);
        await SeedTestingLabAsync(services, isDevelopment);
    }

    // Run AFTER IdentitySeeder. No data is changed unless both switches permit it.
    private static async Task SeedOriginalAsync(IServiceProvider services, bool isDevelopment)
    {
        if (!isDevelopment || !Env.GetBool("SEED_DEMO_DATA", false)) return;

        var db = services.GetRequiredService<ApplicationDbContext>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var originalExists = await db.PurchaseRequisitions.AnyAsync(r => r.RequestNumber == "DEMO-PR-001");

        var requester = await users.FindByEmailAsync("requester@lumiere.local");
        var officer = await users.FindByEmailAsync("officer@lumiere.local");
        var evaluator = await users.FindByEmailAsync("evaluator@lumiere.local");
        var manager = await users.FindByEmailAsync("manager@lumiere.local");
        // The Sourcing Manager only REVIEWS the finished report; it never evaluates. The 4th committee seat belongs to an Evaluation Committee account.
        var sourcingSeat = await users.FindByEmailAsync("demo-sourcing-representative@lumiere.local");
        // The Department seat belongs to an Evaluation Committee account too (a Requester never sits on a committee: the Requester is the one who asked for the purchase).
        var departmentSeat = await users.FindByEmailAsync("demo-department-manager@lumiere.local");
        var supplierUser = await users.FindByEmailAsync("supplier@bottlepro.local");
        if (requester?.DepartmentId == null || officer == null || evaluator == null ||
            manager == null || sourcingSeat == null || departmentSeat == null || supplierUser == null)
            throw new InvalidOperationException("Demo data requires IdentitySeeder test accounts. Enable SEED_TEST_USERS first.");

        var firstSupplier = await db.SupplierProfiles.SingleOrDefaultAsync(s => s.UserId == supplierUser.Id);
        if (firstSupplier?.Status != Statuses.SupplierStatus.Approved)
            throw new InvalidOperationException("Demo data requires the approved BottlePro test supplier.");

        var password = Env.GetString("SEED_TEST_PASSWORD", string.Empty);
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Set SEED_TEST_PASSWORD in your local environment before seeding demo suppliers.");

        var now = DateTime.UtcNow;
        // The complete demo is atomic, including the extra Identity accounts.
        await using var transaction = await db.Database.BeginTransactionAsync();
        var financialUser = await users.FindByEmailAsync("financial@demo.local");
        if (financialUser == null)
        {
            financialUser = new ApplicationUser
            {
                UserName = "financial@demo.local", Email = "financial@demo.local", EmailConfirmed = true,
                FullName = "Demo Financial Evaluator", DepartmentId = evaluator.DepartmentId,
                JobTitle = "Financial Evaluator", IsActive = true
            };
            var created = await users.CreateAsync(financialUser, password);
            if (!created.Succeeded)
                throw new InvalidOperationException("Could not create the demo financial evaluator.");
            var assigned = await users.AddToRoleAsync(financialUser, Roles.EvaluationCommittee);
            if (!assigned.Succeeded)
                throw new InvalidOperationException("Could not assign the demo evaluator role.");
        }
        else if (!financialUser.IsActive || !await users.IsInRoleAsync(financialUser, Roles.EvaluationCommittee))
            throw new InvalidOperationException("The demo financial evaluator account cannot be used.");

        var suppliers = new List<SupplierProfile> { firstSupplier };
        for (var i = 2; i <= 3; i++)
        {
            var email = $"supplier{i}@demo.local";
            var user = await users.FindByEmailAsync(email);
            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = email, Email = email, EmailConfirmed = true,
                    FullName = $"Demo Supplier {i}", UserType = Statuses.UserTypes.Supplier, IsActive = true
                };
                var created = await users.CreateAsync(user, password);
                if (!created.Succeeded)
                    throw new InvalidOperationException("Demo supplier creation failed: " +
                        string.Join("; ", created.Errors.Select(e => e.Description)));
                var assigned = await users.AddToRoleAsync(user, Roles.Supplier);
                if (!assigned.Succeeded)
                    throw new InvalidOperationException("Could not assign the demo supplier role.");
            }
            else if (!await users.IsInRoleAsync(user, Roles.Supplier))
                throw new InvalidOperationException("A demo supplier email is already used by another role.");

            var supplier = await db.SupplierProfiles.SingleOrDefaultAsync(s => s.UserId == user.Id);
            if (supplier == null)
            {
                supplier = new SupplierProfile
                {
                    User = user, CompanyName = $"Demo Packaging {i}",
                    TaxNumber = $"DEMO-TAX-00{i}", CommercialRegNumber = $"DEMO-CR-00{i}",
                    Country = "Jordan", City = "Amman", Address = "Demo industrial area",
                    ContactPerson = $"Demo Contact {i}", ContactPhone = $"+96279000000{i}",
                    Category = "Packaging", Status = Statuses.SupplierStatus.Approved, CreatedAt = now
                };
                db.SupplierProfiles.Add(supplier);
            }
            if (supplier.Status != Statuses.SupplierStatus.Approved || !user.IsActive)
                throw new InvalidOperationException("An existing demo supplier is inactive or not approved.");
            suppliers.Add(supplier);
        }

        if (originalExists)
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return;
        }

        // Reuse the shared status constants so demo data matches the application.
        var requests = new List<PurchaseRequisition>();
        var requestStatuses = new[] { Statuses.RequestStatus.ConvertedToTender, Statuses.RequestStatus.ConvertedToTender, Statuses.RequestStatus.Submitted, Statuses.RequestStatus.Approved, Statuses.RequestStatus.Draft };
        for (var i = 0; i < requestStatuses.Length; i++)
        {
            var request = new PurchaseRequisition
            {
                RequestNumber = $"DEMO-PR-{i + 1:000}", Title = $"Demo packaging supplies {i + 1}",
                Reason = "Sample data for development and team testing.", Priority = Statuses.RequestPriority.Medium,
                TargetDate = now.Date.AddDays(45), Status = requestStatuses[i],
                DepartmentId = requester.DepartmentId.Value, RequestedByUserId = requester.Id,
                ReviewedByUserId = i == 0 || i == 1 || i == 3 ? officer.Id : null,
                EstimatedBudget = 5000, CreatedAt = now.AddDays(-10),
                SubmittedAt = i == 4 ? null : now.AddDays(-9)
            };
            request.PurchaseRequisitionItems.Add(new PurchaseRequisitionItem
            {
                ItemName = "Glass bottles 100 ml", Quantity = 1000, Unit = "Piece",
                Specifications = "Clear glass, compatible with standard pump caps."
            });
            request.PurchaseRequisitionItems.Add(new PurchaseRequisitionItem
            {
                ItemName = "Pump caps", Quantity = 1000, Unit = "Piece",
                Specifications = "Leak resistant pump caps for 100 ml bottles."
            });
            db.PurchaseRequisitions.Add(request);
            requests.Add(request);
        }

        for (var stage = 0; stage < 2; stage++)
        {
            var evaluating = stage == 1;
            var request = requests[stage];
            var tender = new Tender
            {
                TenderNumber = $"DEMO-TND-{stage + 1:000}", Requisition = request,
                Title = evaluating ? "Demo packaging - evaluation" : "Demo packaging - published",
                Description = "Supply glass bottles and pump caps for cosmetics production.",
                Terms = "Prices in JOD. Deliver to company warehouse.",
                Status = evaluating ? Statuses.TenderStatus.UnderEvaluation : Statuses.TenderStatus.Published,
                PublishDate = now.AddDays(-7), SubmissionDeadline = evaluating ? now.AddDays(-1) : now.AddDays(14),
                EstimatedValue = 5000, CreatedByUserId = officer.Id, CreatedAt = now.AddDays(-8)
            };
            var quality = new EvaluationCriterion
            {
                Name = "Technical quality", Type = Statuses.CriterionType.Technical, WeightPercent = 60, MaxScore = 100, IsLocked = true
            };
            tender.EvaluationCriteria.Add(quality);
            tender.EvaluationCriteria.Add(new EvaluationCriterion
            {
                Name = "Total cost", Type = Statuses.CriterionType.Financial, WeightPercent = 40, MaxScore = 100, IsLocked = true
            });
            tender.CommitteeMembers.Add(new CommitteeMember { UserId = sourcingSeat.Id, MemberRole = Statuses.CommitteeRole.SourcingManager });
            tender.CommitteeMembers.Add(new CommitteeMember { UserId = departmentSeat.Id, MemberRole = Statuses.CommitteeRole.Department });
            tender.CommitteeMembers.Add(new CommitteeMember { UserId = evaluator.Id, MemberRole = Statuses.CommitteeRole.Technical });

            tender.CommitteeMembers.Add(new CommitteeMember { UserId = financialUser.Id, MemberRole = Statuses.CommitteeRole.Financial });

            for (var i = 0; i < suppliers.Count; i++)
            {
                var participating = evaluating || i < 2;
                tender.TenderSuppliers.Add(new TenderSupplier
                {
                    SupplierProfile = suppliers[i], AddedAt = now.AddDays(-7),
                    ParticipationStatus = participating ? Statuses.ParticipationStatus.Participating : Statuses.ParticipationStatus.Selected
                });
                tender.Invitations.Add(new Invitation
                {
                    SupplierProfile = suppliers[i], Status = participating ? Statuses.InvitationStatus.Accepted : Statuses.InvitationStatus.Pending,
                    SentAt = now.AddDays(-7), ExpiresAt = tender.SubmissionDeadline,
                    RespondedAt = participating ? now.AddDays(-6) : null
                });
                if (!participating) continue;
                var bid = new Bid
                {
                    SupplierProfile = suppliers[i], Status = evaluating ? Statuses.BidStatus.Compliant : Statuses.BidStatus.Submitted,
                    Currency = "JOD", DeliveryDays = 7 + i * 3, ValidityDays = 60,
                    PaymentTerms = "Payment within 30 days of delivery.",
                    TechnicalSummary = "Demo offer meeting the bottle and cap specifications.",
                    SubmittedAt = now.AddDays(-3)
                };
                foreach (var item in request.PurchaseRequisitionItems)
                    bid.BidItems.Add(new BidItem
                    {
                        RequisitionItem = item, Quantity = item.Quantity,
                        UnitPrice = (item.ItemName.StartsWith("Glass") ? 1.50m : 0.50m) + i * 0.10m,
                        TaxAmount = 0, ShippingCost = 25,
                        OfferedSpecifications = item.Specifications
                    });
                bid.TotalPrice = bid.BidItems.Sum(item => item.UnitPrice * item.Quantity +
                    item.TaxAmount + item.ShippingCost + (item.InstallationCost ?? 0));
                if (evaluating)
                {
                    bid.ComplianceChecks.Add(new ComplianceCheck
                    {
                        CheckedByUserId = officer.Id, Result = Statuses.ComplianceResult.Compliant,
                        Reason = "Demo mandatory requirements checked.", CheckedAt = now.AddHours(-12)
                    });
                    bid.TechnicalEvaluations.Add(new TechnicalEvaluation
                    {
                        Criterion = quality, EvaluatorUserId = evaluator.Id, Score = 90 - i * 5,
                        Notes = "Demo technical assessment.", EvaluatedAt = now.AddHours(-6)
                    });
                    bid.FinancialEvaluations.Add(new FinancialEvaluation
                    {
                        EvaluatorUserId = financialUser.Id, Score = 95 - i * 5,
                        TotalCost = bid.TotalPrice, TaxesAndFees = 0, LogisticsCost = 50,
                        Notes = "Demo cost includes shipping.", EvaluatedAt = now.AddHours(-5)
                    });
                }
                tender.Bids.Add(bid);
            }
            db.Tenders.Add(tender);
        }

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
}
