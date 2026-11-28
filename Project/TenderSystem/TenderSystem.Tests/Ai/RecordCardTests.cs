using Microsoft.EntityFrameworkCore;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Bids;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Data.Entities.Requests;
using TenderSystem.Data.Entities.Tenders;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Ai;

namespace TenderSystem.Tests.Ai;

/// <summary>
/// "Who may see what" for record cards. Uses an in-memory database with a small story:
///   Request pr-1 (owner: Rana) became tender tnd-1, which is Awarded to Alpha. Beta also offered. Eva sat on its committee.
///   Request pr-2 is a draft of another requester. Tender tnd-2 is open, nobody is on its committee.
/// The numbers are lowercase because the in-memory database compares text case-sensitively and the question is lowercased
/// (SQL Server, which the real site uses, ignores case).
/// </summary>
public class RecordCardTests
{
    private const string NotFound = "No request or tender with that number was found";

    internal static (RoleDataService Service, ApplicationDbContext Db) Create()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new ApplicationDbContext(options);
        Seed(db);
        return (new RoleDataService(db, null!, null!), db);
    }

    private static void Seed(ApplicationDbContext db)
    {
        db.Departments.Add(new Department { Id = 1, Name = "Packaging", Code = "PKG" });
        foreach (var id in new[] { "rana", "rami", "eva", "eli", "alpha-user", "beta-user", "oscar" })
            db.Users.Add(new ApplicationUser { Id = id, UserName = id, FullName = id });

        db.PurchaseRequisitions.AddRange(
            new PurchaseRequisition { Id = 1, RequestNumber = "pr-1", Title = "Glass boxes", Status = Statuses.RequestStatus.ConvertedToTender, DepartmentId = 1, RequestedByUserId = "rana", Priority = "High", TargetDate = DateTime.UtcNow.AddDays(30) },
            new PurchaseRequisition { Id = 2, RequestNumber = "pr-2", Title = "Secret draft", Status = Statuses.RequestStatus.Draft, DepartmentId = 1, RequestedByUserId = "rami", Priority = "Low", TargetDate = DateTime.UtcNow.AddDays(30) },
            new PurchaseRequisition { Id = 3, RequestNumber = "pr-3", Title = "Labels", Status = Statuses.RequestStatus.ConvertedToTender, DepartmentId = 1, RequestedByUserId = "rami", Priority = "Low", TargetDate = DateTime.UtcNow.AddDays(30) });

        db.SupplierProfiles.AddRange(
            new SupplierProfile { Id = 1, UserId = "alpha-user", CompanyName = "Alpha Co", Status = Statuses.SupplierStatus.Approved },
            new SupplierProfile { Id = 2, UserId = "beta-user", CompanyName = "Beta Co", Status = Statuses.SupplierStatus.Approved });

        db.Tenders.AddRange(
            new Tender { Id = 1, TenderNumber = "tnd-1", RequisitionId = 1, Title = "Glass boxes tender", Status = Statuses.TenderStatus.Awarded, SubmissionDeadline = DateTime.UtcNow.AddDays(-60), AwardedSupplierId = 1, CreatedByUserId = "oscar" },
            new Tender { Id = 2, TenderNumber = "tnd-2", RequisitionId = 3, Title = "Labels tender", Status = Statuses.TenderStatus.Open, SubmissionDeadline = DateTime.UtcNow.AddDays(10), CreatedByUserId = "oscar" });

        db.CommitteeMembers.Add(new CommitteeMember { Id = 1, TenderId = 1, UserId = "eva", MemberRole = "Member" });
        db.Invitations.AddRange(
            new Invitation { Id = 1, TenderId = 1, SupplierProfileId = 1, Status = Statuses.InvitationStatus.Accepted },
            new Invitation { Id = 2, TenderId = 1, SupplierProfileId = 2, Status = Statuses.InvitationStatus.Accepted });
        db.Bids.AddRange(
            new Bid { Id = 1, TenderId = 1, SupplierProfileId = 1, Status = Statuses.BidStatus.Accepted, TotalPrice = 1111, Currency = "USD" },
            new Bid { Id = 2, TenderId = 1, SupplierProfileId = 2, Status = Statuses.BidStatus.Compliant, TotalPrice = 999, Currency = "USD" });
        db.SaveChanges();
    }

    /// <summary>Everything the model would be given for this question: the facts text and the table rows.</summary>
    private static async Task<string> AskAsync(string role, string userId, string question)
    {
        var (service, _) = Create();
        var data = await service.LoadAsync(role, userId, question, CancellationToken.None);
        return data.Facts + "\n" + data.Table?.Title + "\n" + string.Join("\n", data.Table?.Rows.Select(r => string.Join(" | ", r)) ?? []);
    }

    private static void Has(string expected, string text) => Assert.Contains(expected, text, StringComparison.OrdinalIgnoreCase);
    private static void Lacks(string unexpected, string text) => Assert.DoesNotContain(unexpected, text, StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- the links people asked for

    [Fact]
    public async Task The_officer_sees_the_tender_made_from_a_request()
    {
        var text = await AskAsync(Roles.SourcingOfficer, "oscar", "pr-1");
        Has("tnd-1", text);
        Has("Awarded", text);
    }

    [Fact]
    public async Task The_officer_sees_the_request_a_tender_came_from_and_who_won()
    {
        var text = await AskAsync(Roles.SourcingOfficer, "oscar", "tnd-1");
        Has("Made from request pr-1", text);
        Has("Awarded to: Alpha Co", text);
        Has("Beta Co", text);                     // the offers table
    }

    // ---------------------------------------------------------------- nobody gets more than before

    [Fact]
    public async Task A_requester_never_sees_the_tender_of_their_own_request()
    {
        var text = await AskAsync(Roles.Requester, "rana", "pr-1");
        Has("Glass boxes", text);
        Lacks("tnd-1", text);
    }

    [Fact]
    public async Task A_requester_cannot_open_a_tender_or_someone_elses_request()
    {
        Has(NotFound, await AskAsync(Roles.Requester, "rana", "tnd-1"));
        Has(NotFound, await AskAsync(Roles.Requester, "rana", "pr-3"));
    }

    [Fact]
    public async Task A_draft_request_is_private_to_its_owner()
    {
        Has(NotFound, await AskAsync(Roles.SourcingOfficer, "oscar", "pr-2"));
        Has("Secret draft", await AskAsync(Roles.Requester, "rami", "pr-2"));
    }

    [Fact]
    public async Task A_committee_member_can_open_an_old_finished_tender_they_sat_on()
    {
        var text = await AskAsync(Roles.EvaluationCommittee, "eva", "tnd-1");
        Has("Glass boxes tender", text);
        Has("Alpha Co", text);                    // the offers
        Lacks("Awarded to", text);                // not part of what the committee member sees
        Lacks("pr-1", text);                      // nor the request behind it
    }

    [Fact]
    public async Task A_committee_member_cannot_open_a_tender_they_were_not_on()
    {
        Has(NotFound, await AskAsync(Roles.EvaluationCommittee, "eli", "tnd-1"));
        Has(NotFound, await AskAsync(Roles.EvaluationCommittee, "eva", "tnd-2"));
    }

    [Fact]
    public async Task A_supplier_sees_only_their_own_part_of_a_tender_they_were_invited_to()
    {
        var text = await AskAsync(Roles.Supplier, "alpha-user", "tnd-1");
        Has("My invitation: Accepted", text);
        Has("My offer: Accepted", text);
        Lacks("Beta", text);
        Lacks("999", text);
        Lacks("pr-1", text);
    }

    [Fact]
    public async Task A_supplier_cannot_open_a_tender_they_were_not_invited_to_or_a_request()
    {
        Has(NotFound, await AskAsync(Roles.Supplier, "beta-user", "tnd-2"));
        Has(NotFound, await AskAsync(Roles.Supplier, "alpha-user", "pr-1"));
    }

    [Fact]
    public async Task The_manager_sees_only_tenders_that_reached_their_level()
    {
        Has("Awarded to: Alpha Co", await AskAsync(Roles.SourcingManager, "oscar", "tnd-1"));
        Has(NotFound, await AskAsync(Roles.SourcingManager, "oscar", "tnd-2"));     // still open
    }

    [Fact]
    public async Task The_admin_has_no_record_cards()
    {
        var (service, _) = Create();
        var data = await service.LoadAsync(Roles.Admin, "oscar", "tnd-1", CancellationToken.None);
        Assert.True(data.IsSnapshot);
        Lacks("Glass boxes", data.Facts);
    }

    [Fact]
    public async Task A_requester_who_sits_on_a_committee_also_gets_that_tender_but_not_other_requests()
    {
        var (service, db) = Create();
        db.CommitteeMembers.Add(new CommitteeMember { Id = 9, TenderId = 2, UserId = "rana", MemberRole = "Member" });
        db.SaveChanges();

        var tender = await service.LoadAsync(Roles.Requester, "rana", "tnd-2", CancellationToken.None);
        Assert.Equal("RecordCard", tender.Lookup);
        Has("Labels tender", tender.Facts + tender.Table?.Title);

        var request = await service.LoadAsync(Roles.Requester, "rana", "pr-3", CancellationToken.None);
        Has(NotFound, request.Facts);
    }
}
