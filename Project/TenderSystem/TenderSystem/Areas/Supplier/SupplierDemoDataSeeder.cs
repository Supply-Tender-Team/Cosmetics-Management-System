using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Bids;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Data.Entities.Requests;
using TenderSystem.Data.Entities.Tenders;

namespace TenderSystem.Areas.Supplier;

/// <summary>Adds sample tenders, invitations and offers for the built-in supplier account in Development.</summary>
public sealed class SupplierDemoDataSeeder : IHostedService
{
    private const string SupplierProfileSeedClaim = "demo:supplier-onboarding-profile-seeded-v2";
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<SupplierDemoDataSeeder> _logger;

    public SupplierDemoDataSeeder(IServiceScopeFactory scopeFactory, IWebHostEnvironment environment, ILogger<SupplierDemoDataSeeder> logger)
    {
        _scopeFactory = scopeFactory;
        _environment = environment;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment()) return;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var supplierUser = await users.FindByEmailAsync("supplier@bottlepro.local");
            var requester = await users.FindByEmailAsync("requester@lumiere.local");
            var officer = await users.FindByEmailAsync("officer@lumiere.local");
            var supplier = supplierUser is null
                ? null
                : await db.SupplierProfiles.SingleOrDefaultAsync(x => x.UserId == supplierUser.Id, cancellationToken);
            var department = await db.Departments.SingleOrDefaultAsync(x => x.Code == "PKG", cancellationToken);

            // IdentitySeeder creates these development accounts. If they are disabled, leave the database alone.
            if (supplier is null || requester is null || officer is null || department is null) return;

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            // Prepare the Sara demo profile once, then preserve her saved onboarding step
            // across later application restarts.
            var profileSeeded = (await users.GetClaimsAsync(supplierUser!))
                .Any(claim => claim.Type == SupplierProfileSeedClaim);
            if (!profileSeeded)
            {
                supplierUser!.FullName = "Sara";
                supplierUser.OnboardingStatus = "NotStarted";
                supplier.CompanyName = "Sara Cosmetics Co.";
                supplier.Address = string.Empty;
                var userUpdate = await users.UpdateAsync(supplierUser);
                if (!userUpdate.Succeeded)
                    throw new InvalidOperationException("Could not prepare the demo supplier onboarding profile.");
                await db.SaveChangesAsync(cancellationToken);

                var claimResult = await users.AddClaimAsync(supplierUser, new Claim(SupplierProfileSeedClaim, "true"));
                if (!claimResult.Succeeded)
                    throw new InvalidOperationException("Could not mark the demo supplier profile as prepared.");
            }

            if (await db.Tenders.AnyAsync(x => x.TenderNumber == "TND-DEMO-01" || x.TenderNumber == "TND-DEMO-02" || x.TenderNumber == "TND-DEMO-03" || x.TenderNumber == "TND-DEMO-04", cancellationToken))
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            var today = DateTime.UtcNow.Date;
            var requisition = new PurchaseRequisition
            {
                RequestNumber = $"REQ-DEMO-{today:yyyyMMdd}",
                Title = "Packaging materials for the next production cycle",
                Reason = "Sample request used to demonstrate the supplier workspace.",
                Priority = "Medium",
                TargetDate = today.AddDays(60),
                Status = "Approved",
                DepartmentId = department.Id,
                RequestedByUserId = requester.Id,
                EstimatedBudget = 24000m,
                CreatedAt = DateTime.UtcNow,
                SubmittedAt = DateTime.UtcNow
            };
            db.PurchaseRequisitions.Add(requisition);
            await db.SaveChangesAsync(cancellationToken);

            var item = new PurchaseRequisitionItem
            {
                RequisitionId = requisition.Id,
                ItemName = "Amber glass cosmetic bottles",
                Quantity = 12000m,
                Unit = "Piece",
                Specifications = "30 ml bottle with black pump and protective cap"
            };
            db.PurchaseRequisitionItems.Add(item);
            await db.SaveChangesAsync(cancellationToken);

            var tenderA = NewTender("TND-DEMO-01", "Amber glass bottles", requisition.Id, officer.Id, today.AddDays(21));
            var tenderB = NewTender("TND-DEMO-02", "Cosmetic packaging set", requisition.Id, officer.Id, today.AddDays(35));
            var tenderC = NewTender("TND-DEMO-03", "Printed skincare jars", requisition.Id, officer.Id, today.AddDays(42));
            var tenderD = NewTender("TND-DEMO-04", "Sample jars for a new product line", requisition.Id, officer.Id, today.AddDays(50));
            db.Tenders.AddRange(tenderA, tenderB, tenderC, tenderD);
            await db.SaveChangesAsync(cancellationToken);

            db.Invitations.AddRange(
                new Invitation { TenderId = tenderA.Id, SupplierProfileId = supplier.Id, Status = "Pending", SentAt = DateTime.UtcNow.AddDays(-2), ExpiresAt = DateTime.UtcNow.AddDays(12) },
                new Invitation { TenderId = tenderB.Id, SupplierProfileId = supplier.Id, Status = "Accepted", SentAt = DateTime.UtcNow.AddDays(-10), ExpiresAt = DateTime.UtcNow.AddDays(20), RespondedAt = DateTime.UtcNow.AddDays(-9) },
                new Invitation { TenderId = tenderC.Id, SupplierProfileId = supplier.Id, Status = "Accepted", SentAt = DateTime.UtcNow.AddDays(-25), ExpiresAt = DateTime.UtcNow.AddDays(5), RespondedAt = DateTime.UtcNow.AddDays(-24) },
                new Invitation { TenderId = tenderD.Id, SupplierProfileId = supplier.Id, Status = "Accepted", SentAt = DateTime.UtcNow.AddDays(-1), ExpiresAt = DateTime.UtcNow.AddDays(15), RespondedAt = DateTime.UtcNow.AddHours(-12) });
            db.Bids.AddRange(
                new Bid { TenderId = tenderB.Id, SupplierProfileId = supplier.Id, Status = "Under Review", TotalPrice = 18400m, Currency = "USD", DeliveryDays = 28, ValidityDays = 60, PaymentTerms = "30 days after delivery", TechnicalSummary = "Meets the requested material and packaging specifications.", SubmittedAt = DateTime.UtcNow.AddDays(-5), BidItems = [new BidItem { RequisitionItemId = item.Id, UnitPrice = 1.50m, Quantity = 12000m, TaxAmount = 400m, ShippingCost = 0m, OfferedSpecifications = item.Specifications }] },
                new Bid { TenderId = tenderC.Id, SupplierProfileId = supplier.Id, Status = "Accepted", TotalPrice = 19200m, Currency = "USD", DeliveryDays = 30, ValidityDays = 45, PaymentTerms = "30 days after delivery", TechnicalSummary = "Sample accepted offer for the dashboard.", SubmittedAt = DateTime.UtcNow.AddDays(-20), BidItems = [new BidItem { RequisitionItemId = item.Id, UnitPrice = 1.55m, Quantity = 12000m, TaxAmount = 600m, ShippingCost = 0m, OfferedSpecifications = item.Specifications }] },
                new Bid { TenderId = tenderD.Id, SupplierProfileId = supplier.Id, Status = "Draft", TotalPrice = 0m, Currency = "USD", DeliveryDays = 0, ValidityDays = 30, PaymentTerms = "To be confirmed", TechnicalSummary = "Draft sample. Upload a file here to test the shared file storage." });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation("Supplier demo data was added for {Email}.", supplierUser!.Email);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Supplier demo data could not be added. The application will continue to start.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static Tender NewTender(string number, string title, int requisitionId, string officerId, DateTime deadline) => new()
    {
        TenderNumber = number,
        RequisitionId = requisitionId,
        Title = title,
        Description = "Sample tender for local supplier dashboard testing.",
        Terms = "Submit an offer before the stated deadline.",
        Status = "Published",
        PublishDate = DateTime.UtcNow.AddDays(-8),
        SubmissionDeadline = deadline,
        EstimatedValue = 24000m,
        RequiresTopCommittee = false,
        CreatedByUserId = officerId,
        CreatedAt = DateTime.UtcNow
    };
}
