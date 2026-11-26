using System.Data;
using DotNetEnv;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Data.Entities.Bids;
using TenderSystem.Data.Entities.Collaboration;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Data.Entities.Requests;
using TenderSystem.Data.Entities.Tenders;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Audit;

namespace TenderSystem.Data.Seed;

public static partial class DemoDataSeeder
{
    // A new run adds empty testing scenarios. Restarting never resets user work or audit history.
    public static async Task SeedTestingLabAsync(IServiceProvider services, bool isDevelopment)
    {
        if (!isDevelopment || !Env.GetBool("SEED_DEMO_DATA", false)) return;
        var run = Env.GetString("SEED_DEMO_RUN", "01").Trim();
        if (run.Length is < 1 or > 8 || run.Any(c => !char.IsAsciiLetterOrDigit(c)))
            throw new InvalidOperationException("SEED_DEMO_RUN must contain 1 to 8 letters or digits.");
        var db = services.GetRequiredService<ApplicationDbContext>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var audit = services.GetRequiredService<AuditService>();
        var prefix = "LAB-" + run + "-";
        var suffixes = new[] { "EVL", "DIRECT", "TIE", "FLOW", "SUP" };
        var requestSuffixes = new[] { "DRAFT", "SENT", "OK" };
        var runExists = await db.Tenders.CountAsync(t => suffixes.Select(s => prefix + s).Contains(t.TenderNumber)) == suffixes.Length
            && await db.PurchaseRequisitions.CountAsync(r => requestSuffixes.Select(s => prefix + "PR-" + s).Contains(r.RequestNumber)) == requestSuffixes.Length;
        var password = Env.GetString("SEED_TEST_PASSWORD", string.Empty);
        if (string.IsNullOrWhiteSpace(password)) throw new InvalidOperationException("Set SEED_TEST_PASSWORD before creating testing accounts.");
        var departments = await db.Departments.Where(d => d.Code == "PKG" || d.Code == "SRC" || d.Code == "QC" || d.Code == "FIN")
            .ToDictionaryAsync(d => d.Code, d => d.Id);
        if (departments.Count != 4) throw new InvalidOperationException("Run IdentitySeeder before the testing lab.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var department = await CreateLabAccountAsync(users, audit, "lab.department@demo.local", "LAB Department Reviewer", Roles.EvaluationCommittee, departments["PKG"], password);
        var officer = await CreateLabAccountAsync(users, audit, "lab.officer@demo.local", "LAB Sourcing Officer", Roles.SourcingOfficer, departments["SRC"], password);
        var technical = await CreateLabAccountAsync(users, audit, "lab.technical@demo.local", "LAB Technical Reviewer", Roles.EvaluationCommittee, departments["QC"], password);
        var financial = await CreateLabAccountAsync(users, audit, "lab.financial@demo.local", "LAB Financial Reviewer", Roles.EvaluationCommittee, departments["FIN"], password);
        // The LAB manager only REVIEWS (Sourcing Manager role). The 4th committee seat belongs to an Evaluation Committee account.
        await CreateLabAccountAsync(users, audit, "lab.manager@demo.local", "LAB Sourcing Reviewer", Roles.SourcingManager, departments["SRC"], password);
        var sourcing = await CreateLabAccountAsync(users, audit, "lab.sourcing@demo.local", "LAB Sourcing Representative", Roles.EvaluationCommittee, departments["SRC"], password);
        if (runExists) { await transaction.CommitAsync(); return; }
        var memberIds = new[] { department.Id, financial.Id, technical.Id, sourcing.Id };
        var seats = new[] { Statuses.CommitteeRole.Department, Statuses.CommitteeRole.Financial, Statuses.CommitteeRole.Technical, Statuses.CommitteeRole.SourcingManager };
        var suppliers = new List<SupplierProfile>();
        for (var i = 1; i <= 4; i++)
        {
            var user = await CreateLabAccountAsync(users, audit, $"lab.supplier{i}@demo.local", $"LAB Supplier {i}", Roles.Supplier, null, password);
            var supplier = await db.SupplierProfiles.SingleOrDefaultAsync(s => s.UserId == user.Id);
            if (supplier == null)
            {
                supplier = new SupplierProfile { UserId = user.Id, CompanyName = $"LAB Packaging {i}",
                    TaxNumber = $"LAB-TAX-{i}", CommercialRegNumber = $"LAB-REG-{i}", Country = "Jordan", City = "Amman",
                    Address = "Development test company", ContactPerson = user.FullName, ContactPhone = "0000000000",
                    Category = "Packaging", Status = Statuses.SupplierStatus.Approved };
                db.SupplierProfiles.Add(supplier);
                await db.SaveChangesAsync();
                await audit.LogForUserAsync(officer.Id, "DemoSupplierCreated", "SupplierProfile", supplier.Id.ToString(), "LAB development fixture; synthetic approved supplier.");
            }
            if (!user.IsActive || supplier.Status != Statuses.SupplierStatus.Approved)
                throw new InvalidOperationException("A LAB supplier was changed or suspended. Review it without resetting its account.");
            suppliers.Add(supplier);
        }
        // Do not exceed the Top Committee service's fixed four eligible users.
        var topRole = await db.Roles.SingleAsync(r => r.Name == Roles.TopCommittee);
        var nowOffset = DateTimeOffset.UtcNow;
        var topCount = await db.Users.CountAsync(u => u.IsActive && u.UserType == Statuses.UserTypes.Employee
            && (u.LockoutEnd == null || u.LockoutEnd <= nowOffset)
            && db.UserRoles.Any(r => r.UserId == u.Id && r.RoleId == topRole.Id)
            && !db.UserRoles.Any(r => r.UserId == u.Id && r.RoleId != topRole.Id));
        for (var i = 1; topCount < 4 && i <= 4; i++)
        {
            var email = $"lab.top{i}@demo.local";
            if (await users.FindByEmailAsync(email) != null) continue;
            await CreateLabAccountAsync(users, audit, email, $"LAB Top Committee {i}", Roles.TopCommittee, departments["FIN"], password);
            topCount++;
        }
        var now = DateTime.UtcNow;
        for (var stage = 0; stage < suffixes.Length; stage++)
        {
            var number = prefix + suffixes[stage];
            if (await db.Tenders.AnyAsync(t => t.TenderNumber == number)) continue;
            if (await db.PurchaseRequisitions.AnyAsync(r => r.RequestNumber == number))
                throw new InvalidOperationException("Incomplete LAB request: " + number + ". Review it before seeding again.");
            var request = CreateLabRequest(number, department, officer.Id, Statuses.RequestStatus.ConvertedToTender, now);
            var isEvaluation = stage < 3;
            var tender = new Tender { TenderNumber = number, Requisition = request,
                Title = suffixes[stage] switch { "EVL" => "[LAB] Empty evaluations - then Officer, Manager and Top Committee", "DIRECT" => "[LAB] Empty evaluations - Manager direct award", "TIE" => "[LAB] Four equal-price offers - voting tie practice", "FLOW" => "[LAB] Review compliance, then send to evaluation", _ => "[LAB] Supplier invitations - submit your own offers" },
                Description = "Fresh development test run " + run + ". No scores, Ready confirmations, recommendation votes or reports are prefilled.",
                Terms = "JOD prices include shipping. Payment within 30 days.", CreatedByUserId = officer.Id,
                Status = isEvaluation ? Statuses.TenderStatus.UnderEvaluation : Statuses.TenderStatus.Published,
                PublishDate = now.AddDays(-4), SubmissionDeadline = isEvaluation ? now.AddDays(-1) : now.AddDays(14),
                EstimatedValue = 5000, RequiresTopCommittee = true, CreatedAt = now };   // every demo tender goes to the Top Committee (the LAB "direct award" tender too)
            foreach (var criterion in CreateDemoEvaluationCriteria()) tender.EvaluationCriteria.Add(criterion);
            for (var m = 0; m < memberIds.Length; m++) tender.CommitteeMembers.Add(new CommitteeMember { UserId = memberIds[m], MemberRole = seats[m] });
            var count = stage == 2 || stage == 4 ? 4 : 3;
            for (var i = 0; i < count; i++)
            {
                tender.TenderSuppliers.Add(new TenderSupplier { SupplierProfileId = suppliers[i].Id, AddedAt = now.AddDays(-3),
                    ParticipationStatus = stage == 4 ? Statuses.ParticipationStatus.Selected : Statuses.ParticipationStatus.Participating });
                tender.Invitations.Add(new Invitation { SupplierProfileId = suppliers[i].Id, SentAt = now.AddDays(-3),
                    ExpiresAt = now.AddDays(20), Status = stage == 4 ? Statuses.InvitationStatus.Pending : Statuses.InvitationStatus.Accepted,
                    RespondedAt = stage == 4 ? null : now.AddDays(-2) });
                if (stage == 4) continue;
                var bid = new Bid { SupplierProfileId = suppliers[i].Id, SubmittedAt = now.AddDays(-2), Currency = "JOD",
                    Status = isEvaluation ? Statuses.BidStatus.Compliant : Statuses.BidStatus.Submitted,
                    DeliveryDays = 7 + i * 3, ValidityDays = 60, PaymentTerms = "Payment within 30 days.",
                    TechnicalSummary = "[LAB] Bottles and caps meeting the requested specifications." };
                foreach (var item in request.PurchaseRequisitionItems)
                    bid.BidItems.Add(new BidItem { RequisitionItem = item, Quantity = item.Quantity,
                        UnitPrice = (item.ItemName.StartsWith("Glass") ? 1.5m : 0.5m) + (stage == 2 ? 0 : i * 0.25m),
                        ShippingCost = 25, OfferedSpecifications = item.Specifications });
                bid.TotalPrice = bid.BidItems.Sum(i => i.Quantity * i.UnitPrice + i.ShippingCost);
                if (isEvaluation) bid.ComplianceChecks.Add(new ComplianceCheck { CheckedByUserId = officer.Id,
                    Result = Statuses.ComplianceResult.Compliant, Reason = "[LAB] Synthetic eligible offer for evaluation testing.", CheckedAt = now });
                tender.Bids.Add(bid);
            }
            db.Tenders.Add(tender);
            await db.SaveChangesAsync();
            if (isEvaluation)
                foreach (var memberId in memberIds)
                    db.Notifications.Add(new Notification { UserId = memberId, Type = "TenderEvaluation", Title = number + " ready for evaluation",
                        Message = tender.Title, Link = "/Sourcing/CommitteeBrief/Index/" + tender.Id, CreatedAt = now });
            await db.SaveChangesAsync();
            await audit.LogForUserAsync(officer.Id, "DemoTestingRunCreated", "Tender", tender.Id.ToString(), number + "; empty evaluations; synthetic development fixture.");
        }
        for (var i = 0; i < requestSuffixes.Length; i++)
        {
            var number = prefix + "PR-" + requestSuffixes[i];
            if (await db.PurchaseRequisitions.AnyAsync(r => r.RequestNumber == number)) continue;
            var status = i == 0 ? Statuses.RequestStatus.Draft : i == 1 ? Statuses.RequestStatus.Submitted : Statuses.RequestStatus.Approved;
            var request = CreateLabRequest(number, department, officer.Id, status, now);
            db.PurchaseRequisitions.Add(request);
            await db.SaveChangesAsync();
            await audit.LogForUserAsync(department.Id, "DemoRequestCreated", "PurchaseRequisition", request.Id.ToString(), number + "; development fixture.");
        }
        await transaction.CommitAsync();
    }

    private static async Task<ApplicationUser> CreateLabAccountAsync(UserManager<ApplicationUser> users, AuditService audit,
        string email, string name, string role, int? departmentId, string password)
    {
        var existing = await users.FindByEmailAsync(email);
        if (existing != null)
        {
            var roles = await users.GetRolesAsync(existing);
            // Upgrade only these named LAB fixtures; keep the same user IDs, work and passwords.
            if (email == "lab.department@demo.local" && role == Roles.EvaluationCommittee
                && roles.Count == 1 && roles[0] == Roles.Requester)
            {
                var added = await users.AddToRoleAsync(existing, Roles.EvaluationCommittee);
                var removed = await users.RemoveFromRoleAsync(existing, Roles.Requester);
                if (!added.Succeeded || !removed.Succeeded) throw new InvalidOperationException("Could not update the LAB department role.");
                await users.UpdateSecurityStampAsync(existing);
                await audit.LogForUserAsync(existing.Id, "DemoRolesUpdated", "ApplicationUser", existing.Id, "LAB department now uses the Evaluation Committee dashboard.");
                roles = await users.GetRolesAsync(existing);
            }
            // The Sourcing Manager never evaluates: an older LAB database may still give this account the Evaluation Committee role as well.
            if (email == "lab.manager@demo.local" && roles.Contains(Roles.SourcingManager) && roles.Contains(Roles.EvaluationCommittee))
            {
                if (!(await users.RemoveFromRoleAsync(existing, Roles.EvaluationCommittee)).Succeeded)
                    throw new InvalidOperationException("Could not remove the Evaluation Committee role from the LAB manager.");
                await users.UpdateSecurityStampAsync(existing);
                await audit.LogForUserAsync(existing.Id, "DemoRolesUpdated", "ApplicationUser", existing.Id, "The Sourcing Manager only reviews; the Evaluation Committee role was removed.");
                roles = await users.GetRolesAsync(existing);
            }
            var expectedRoles = new[] { role };
            if (!existing.IsActive || roles.Count != expectedRoles.Length || expectedRoles.Any(r => !roles.Contains(r)) || existing.DepartmentId != departmentId)
                throw new InvalidOperationException("Existing LAB account needs review: " + email);
            return existing;
        }
        var user = new ApplicationUser { Email = email, UserName = email, EmailConfirmed = true, FullName = name,
            DepartmentId = departmentId, JobTitle = name, IsActive = true,
            UserType = role == Roles.Supplier ? Statuses.UserTypes.Supplier : Statuses.UserTypes.Employee };
        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded) throw new InvalidOperationException("LAB account creation failed: " + string.Join("; ", created.Errors.Select(e => e.Description)));
        var assigned = await users.AddToRoleAsync(user, role);
        if (!assigned.Succeeded) throw new InvalidOperationException("LAB role assignment failed.");
        await audit.LogForUserAsync(user.Id, "DemoAccountCreated", "ApplicationUser", user.Id, "LAB development account; role=" + role);
        return user;
    }

    private static PurchaseRequisition CreateLabRequest(string number, ApplicationUser requester, string officerId, string status, DateTime now)
    {
        var request = new PurchaseRequisition { RequestNumber = number, Title = "[LAB] Bottles and caps - " + number,
            Reason = "Synthetic purchasing request for team testing.", Priority = Statuses.RequestPriority.Medium,
            TargetDate = now.AddDays(45), Status = status, DepartmentId = requester.DepartmentId!.Value,
            RequestedByUserId = requester.Id, ReviewedByUserId = status == Statuses.RequestStatus.Approved || status == Statuses.RequestStatus.ConvertedToTender ? officerId : null,
            EstimatedBudget = 5000, CreatedAt = now.AddDays(-6), SubmittedAt = status == Statuses.RequestStatus.Draft ? null : now.AddDays(-5) };
        request.PurchaseRequisitionItems.Add(new PurchaseRequisitionItem { ItemName = "Glass bottles 100 ml", Quantity = 1000, Unit = "Piece", Specifications = "Clear glass bottles, compatible with pump caps." });
        request.PurchaseRequisitionItems.Add(new PurchaseRequisitionItem { ItemName = "Pump caps", Quantity = 1000, Unit = "Piece", Specifications = "Leak resistant caps for 100 ml bottles." });
        return request;
    }
}
