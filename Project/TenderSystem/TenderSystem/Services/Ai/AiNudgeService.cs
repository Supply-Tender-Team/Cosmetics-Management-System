using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TenderSystem.Data;
using TenderSystem.Infrastructure;
using TenderSystem.Infrastructure.Options;

namespace TenderSystem.Services.Ai;

/// <param name="Text">what the little cloud says</param>
/// <param name="Ask">the question the chat runs when the cloud is clicked (null when the cloud opens a page instead)</param>
/// <param name="Link">a page to open when the cloud is clicked (null when it runs a question)</param>
public sealed record AiNudge(string Text, string? Ask, string? Link);

/// <summary>
/// AI-12: the cloud above the round button only speaks when there is something REAL to say. Each item is a COUNT of the signed-in user's own
/// work (offers waiting for their compliance review, a report waiting for them, invitations they have not answered...). There is no model
/// call and nothing is sent anywhere: it only counts rows the user already sees on their own pages. Results are cached for 60 seconds per user,
/// and nothing at all is returned when AI_CHAT_DATA=false (the assistant must not read the database).
/// </summary>
public sealed class AiNudgeService
{
    private const int MaxItems = 3;
    private static readonly string[] EvaluationSeats = [Statuses.CommitteeRole.Department, Statuses.CommitteeRole.Financial, Statuses.CommitteeRole.Technical];
    private static readonly string[] EvaluationStages = [Statuses.TenderStatus.Published, Statuses.TenderStatus.Open, Statuses.TenderStatus.Closed, Statuses.TenderStatus.UnderEvaluation];

    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly AiChatOptions _options;

    public AiNudgeService(ApplicationDbContext db, IMemoryCache cache, IOptions<AiChatOptions> options)
    {
        _db = db;
        _cache = cache;
        _options = options.Value;
    }

    public bool Enabled => _options.Data && _options.Provider is "ollama" or "openai";

    public async Task<IReadOnlyList<AiNudge>> GetAsync(string role, string userId, CancellationToken ct)
    {
        if (!Enabled || string.IsNullOrEmpty(role) || string.IsNullOrEmpty(userId)) return [];
        var key = $"ai-nudges:{userId}:{role}";
        if (_cache.TryGetValue(key, out List<AiNudge>? cached) && cached != null) return cached;

        var items = await BuildAsync(role, userId, ct);
        _cache.Set(key, items, TimeSpan.FromSeconds(60));
        return items;
    }

    private async Task<List<AiNudge>> BuildAsync(string role, string userId, CancellationToken ct)
    {
        var items = new List<AiNudge>();
        void Add(int count, string one, string many, string? ask, string? link = null)
        {
            if (count > 0) items.Add(new AiNudge(count == 1 ? one : many.Replace("{n}", count.ToString()), ask, link));
        }
        var now = DateTime.UtcNow;

        switch (role)
        {
            case Roles.Admin:
                Add(await _db.SupplierProfiles.CountAsync(s => s.Status == Statuses.SupplierStatus.Pending, ct),
                    "1 supplier is waiting for your approval.", "{n} suppliers are waiting for your approval.", "Which suppliers are waiting for approval?");
                Add(await _db.ContactMessages.CountAsync(m => !m.IsRead, ct),
                    "1 contact message is unread.", "{n} contact messages are unread.", null, "/Admin/ContactMessages");
                break;

            case Roles.Requester:
                Add(await _db.PurchaseRequisitions.CountAsync(r => r.RequestedByUserId == userId && r.Status == Statuses.RequestStatus.Returned, ct),
                    "1 of your requests was returned to you.", "{n} of your requests were returned to you.", "Show my returned requests");
                break;

            case Roles.SourcingOfficer:
                Add(await _db.Bids.CountAsync(b => b.Tender.CreatedByUserId == userId && b.SubmittedAt != null
                        && (b.Status == Statuses.BidStatus.Submitted || b.Status == Statuses.BidStatus.UnderReview), ct),
                    "1 offer is waiting for your compliance review.", "{n} offers are waiting for your compliance review.", "Check the compliance of the waiting offers");
                Add(await _db.PurchaseRequisitions.CountAsync(r => r.Status == Statuses.RequestStatus.Submitted || r.Status == Statuses.RequestStatus.UnderReview, ct),
                    "1 purchase request is waiting for your review.", "{n} purchase requests are waiting for your review.", "Show the requests waiting for sourcing review");
                Add(await _db.Tenders.CountAsync(t => t.CreatedByUserId == userId && t.Status == Statuses.TenderStatus.UnderEvaluation
                        && t.EvaluationReports.Any(r => r.SubmittedAt != null && r.Status == Statuses.ReportStatus.RecommendationPending), ct),
                    "1 evaluation report is ready to send to the manager.", "{n} evaluation reports are ready to send to the manager.", null, "/Sourcing/Evaluation");
                break;

            case Roles.EvaluationCommittee:
                // the brief was sent to this member (the same rule the Evaluation pages use) and they have not confirmed Ready yet
                Add(await _db.CommitteeMembers.CountAsync(m => m.UserId == userId && EvaluationSeats.Contains(m.MemberRole)
                        && EvaluationStages.Contains(m.Tender.Status)
                        && !_db.EvaluationCommitteeSubmissions.Any(s => s.TenderId == m.TenderId && s.EvaluatorUserId == userId)
                        && _db.Notifications.Any(n => n.UserId == userId && n.Type == "TenderEvaluation"
                            && n.Link == "/Sourcing/CommitteeBrief/Index/" + m.TenderId), ct),
                    "1 tender is waiting for your evaluation.", "{n} tenders are waiting for your evaluation.", "Compare the offers of my tender");
                break;

            case Roles.SourcingManager:
                Add(await _db.Tenders.CountAsync(t => t.Status == Statuses.TenderStatus.AwaitingManagerReview
                        && t.CommitteeMembers.Any(m => m.UserId == userId && m.MemberRole == Statuses.CommitteeRole.SourcingManager), ct),
                    "1 report is waiting for your review.", "{n} reports are waiting for your review.", "Summarize the report waiting for me");
                break;

            case Roles.TopCommittee:
                Add(await _db.Tenders.CountAsync(t => t.Status == Statuses.TenderStatus.AwaitingCommitteeDecision && t.RequiresTopCommittee
                        && !_db.TopCommitteeVotes.Any(v => v.TenderId == t.Id && v.MemberUserId == userId), ct),
                    "1 tender is waiting for your vote.", "{n} tenders are waiting for your vote.", "Analyze the tender package waiting for me");
                break;

            case Roles.Supplier:
                var supplierId = await _db.SupplierProfiles.Where(s => s.UserId == userId).Select(s => (int?)s.Id).FirstOrDefaultAsync(ct);
                if (supplierId != null)
                {
                    Add(await _db.Invitations.CountAsync(i => i.SupplierProfileId == supplierId && i.Status == Statuses.InvitationStatus.Pending && i.ExpiresAt > now, ct),
                        "1 tender invitation is waiting for your answer.", "{n} tender invitations are waiting for your answer.", "Where are my tender invitations?");
                    Add(await _db.Bids.CountAsync(b => b.SupplierProfileId == supplierId && b.Status == Statuses.BidStatus.Draft && b.Tender.SubmissionDeadline > now, ct),
                        "1 of your offers is still a draft.", "{n} of your offers are still drafts.", "Check if my offer is complete");
                }
                break;
        }

        // everybody: a conversation that waits for an answer
        Add(await _db.Clarifications.CountAsync(c => c.ReceiverUserId == userId && c.Status == Statuses.ClarificationStatus.Pending, ct),
            "1 clarification is waiting for your answer.", "{n} clarifications are waiting for your answer.", null, "/Shared/Clarifications");

        return items.Take(MaxItems).ToList();
    }
}
