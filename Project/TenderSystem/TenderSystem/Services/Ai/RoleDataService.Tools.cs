using System.Text;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Data.Entities.Bids;
using TenderSystem.Data.Entities.Requests;
using TenderSystem.Data.Entities.Tenders;
using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Ai;

// AI-21: the structured lookups ("tools") the planner can choose (see AiLookupCatalog). Same rule as every lookup in this class:
// each query starts from the user's own scope INSIDE the query. The model only picks the tool and its filters; it never sees a query.
public sealed partial class RoleDataService
{
    /// <summary>Who is asking, as far as data scope goes. Seat = also sits on an evaluation committee (a Requester or Sourcing Manager can).</summary>
    private sealed record Who(string Role, string UserId, int? SupplierId, bool Seat);

    private async Task<Who> WhoAsync(string role, string userId, CancellationToken ct)
    {
        var pid = role == Roles.Supplier ? await SupplierIdAsync(userId, ct) : null;
        var seat = role == Roles.EvaluationCommittee || await _db.CommitteeMembers.AnyAsync(m => m.UserId == userId, ct);
        return new Who(role, userId, pid, seat);
    }

    /// <summary>True when this user also sits on an evaluation committee (so the committee lookups apply to them whatever their main role is).</summary>
    public async Task<bool> HasSeatAsync(string role, string userId, CancellationToken ct) =>
        role is Roles.Requester or Roles.SourcingManager && await _db.CommitteeMembers.AnyAsync(m => m.UserId == userId, ct);

    // ---------------------------------------------------------------- scope

    /// <summary>The tender statuses that reached the Sourcing Manager or the Top Committee (nothing for the other roles).</summary>
    private static string[] LevelStatuses(string role) => role switch
    {
        Roles.SourcingManager => [Statuses.TenderStatus.AwaitingManagerReview, Statuses.TenderStatus.AwaitingCommitteeDecision, Statuses.TenderStatus.Awarded],
        Roles.TopCommittee => [Statuses.TenderStatus.AwaitingCommitteeDecision, Statuses.TenderStatus.Awarded],
        _ => [],
    };

    private IQueryable<Tender> TendersFor(Who w)
    {
        var all = _db.Tenders.AsNoTracking();
        if (w.Role is Roles.SourcingOfficer or Roles.Admin) return all;

        var statuses = LevelStatuses(w.Role);
        var byStatus = statuses.Length > 0;
        var committee = w.Seat;
        var pid = w.SupplierId;
        var uid = w.UserId;
        if (!byStatus && !committee && pid == null) return all.Where(t => false);

        return all.Where(t => (byStatus && statuses.Contains(t.Status))
            || (committee && t.CommitteeMembers.Any(m => m.UserId == uid))
            || (pid != null && (t.Invitations.Any(i => i.SupplierProfileId == pid) || t.TenderSuppliers.Any(s => s.SupplierProfileId == pid))));
    }

    private IQueryable<Bid> BidsFor(Who w)
    {
        // a supplier sees ONLY their own offers; nobody else sees drafts
        if (w.Role == Roles.Supplier) return _db.Bids.AsNoTracking().Where(b => b.SupplierProfileId == w.SupplierId);
        var bids = _db.Bids.AsNoTracking().Where(b => b.Status != Statuses.BidStatus.Draft);
        if (w.Role is Roles.Admin or Roles.SourcingOfficer) return bids;
        var tenders = TendersFor(w);
        return bids.Where(b => tenders.Select(t => t.Id).Contains(b.TenderId));
    }

    private IQueryable<PurchaseRequisition> RequestsFor(Who w)
    {
        var all = _db.PurchaseRequisitions.AsNoTracking();
        return w.Role switch
        {
            Roles.Requester => all.Where(r => r.RequestedByUserId == w.UserId),
            Roles.SourcingOfficer or Roles.Admin => all.Where(r => r.Status != Statuses.RequestStatus.Draft),     // drafts are private to the requester
            _ => all.Where(r => false),
        };
    }

    // ---------------------------------------------------------------- run

    /// <summary>Runs the tool the planner chose. Null when the role may not use it or it is not a known tool (the caller then shows the record card or the overview).</summary>
    public async Task<AiDataResult?> RunToolAsync(string role, string userId, AiToolCall call, string question, CancellationToken ct)
    {
        var who = await WhoAsync(role, userId, ct);
        if (!AiLookupCatalog.Allows(role, who.Seat && role is Roles.Requester or Roles.SourcingManager, call.Tool)) return null;

        return call.Tool switch
        {
            "requests" => await RequestsToolAsync(who, call, ct),
            "tenders" => await TendersToolAsync(who, call, ct),
            "offers" => await OffersToolAsync(who, call, ct),
            "counts" => await CountsToolAsync(who, call, ct),
            "deadlines" => await DeadlinesToolAsync(who, call, ct),
            "invitations" => await InvitationsToolAsync(who, call, ct),
            "evaluation_progress" => await ProgressToolAsync(who, call, ct),
            "votes" => await VotesToolAsync(who, call, ct),
            "audit" => await AuditToolAsync(call, ct),
            "users" => await UsersToolAsync(call, ct),
            "suppliers" => await SuppliersToolAsync(who, call, ct),
            "notifications" => await NotificationsToolAsync(who, call, ct),
            "contact_messages" => await ContactMessagesToolAsync(call, ct),
            "help" => await HelpToolAsync(role, userId, call, question, ct),
            _ => null,
        };
    }

    // ---------------------------------------------------------------- argument helpers

    private static int IntArg(AiToolCall c, string name, int fallback, int max)
    {
        var text = Normalize(c.Arg(name));
        var m = System.Text.RegularExpressions.Regex.Match(text, @"\d{1,4}");
        return m.Success && int.TryParse(m.Value, out var n) ? Math.Clamp(n, 1, max) : fallback;
    }

    private static int Limit(AiToolCall c) => IntArg(c, "limit", DefaultRows, MaxRows);

    private static bool BoolArg(AiToolCall c, string name) => Normalize(c.Arg(name)) is "true" or "yes" or "1";

    private static string Text(AiToolCall c, string name, int max = 60)
    {
        var t = c.Arg(name).ReplaceLineEndings(" ");
        return t.Length <= max ? t : t[..max];
    }

    /// <summary>The allowed status that the planner's word stands for ("under review" -> "Under Review"), or null when it matches none.</summary>
    private static string? MatchStatus(string? wanted, IEnumerable<string> allowed)
    {
        static string Key(string s) => new(s.ToLowerInvariant().Where(char.IsLetter).ToArray());
        var key = Key(wanted ?? "");
        if (key.Length < 3) return null;
        var list = allowed.ToList();
        return list.FirstOrDefault(a => Key(a) == key) ?? list.FirstOrDefault(a => Key(a).StartsWith(key) || key.StartsWith(Key(a)));
    }

    private static readonly string[] RequestStatuses = [Statuses.RequestStatus.Draft, Statuses.RequestStatus.Submitted, Statuses.RequestStatus.UnderReview, Statuses.RequestStatus.Approved,
        Statuses.RequestStatus.Rejected, Statuses.RequestStatus.Returned, Statuses.RequestStatus.ConvertedToTender];
    private static readonly string[] TenderStatuses = [Statuses.TenderStatus.Draft, Statuses.TenderStatus.Published, Statuses.TenderStatus.Open, Statuses.TenderStatus.Closed, Statuses.TenderStatus.UnderEvaluation,
        Statuses.TenderStatus.AwaitingManagerReview, Statuses.TenderStatus.AwaitingCommitteeDecision, Statuses.TenderStatus.Awarded, Statuses.TenderStatus.Cancelled, Statuses.TenderStatus.Rejected,
        Statuses.TenderStatus.RetenderRequired];
    private static readonly string[] BidStatuses = [Statuses.BidStatus.Draft, Statuses.BidStatus.Submitted, Statuses.BidStatus.UnderReview, Statuses.BidStatus.Compliant, Statuses.BidStatus.NonCompliant,
        Statuses.BidStatus.Accepted, Statuses.BidStatus.Rejected, Statuses.BidStatus.Withdrawn];
    private static readonly string[] SupplierStatuses = [Statuses.SupplierStatus.Pending, Statuses.SupplierStatus.Approved, Statuses.SupplierStatus.Rejected, Statuses.SupplierStatus.Suspended];
    private static readonly string[] InvitationStatuses = [Statuses.InvitationStatus.Pending, Statuses.InvitationStatus.Accepted, Statuses.InvitationStatus.Declined, Statuses.InvitationStatus.Expired];

    /// <summary>A line telling the model which filters were really applied, so it describes the result truthfully.</summary>
    private static string Applied(params string?[] parts)
    {
        var used = parts.Where(p => !string.IsNullOrEmpty(p)).ToList();
        return used.Count == 0 ? "Filters applied: none (all records the user may see, newest first)." : "Filters applied: " + string.Join("; ", used) + ".";
    }

    /// <summary>"Showing 10 of 24 matching records." so a list that was cut is never mistaken for the whole answer.</summary>
    private static string ShownOf(int shown, int total) => total > shown ? $" Showing {shown} of {total} matching records (the list is cut)." : $" {total} matching record(s).";

    // ---------------------------------------------------------------- tools

    private async Task<AiDataResult> RequestsToolAsync(Who w, AiToolCall c, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var query = RequestsFor(w);
        var applied = new List<string?>();

        var status = MatchStatus(c.Arg("status"), RequestStatuses);
        if (status != null) { query = query.Where(r => r.Status == status); applied.Add($"status = {status}"); }
        var text = Text(c, "text");
        if (text.Length > 0) { query = query.Where(r => r.Title.Contains(text)); applied.Add($"title contains \"{text}\""); }

        var finished = new[] { Statuses.RequestStatus.Approved, Statuses.RequestStatus.Rejected, Statuses.RequestStatus.ConvertedToTender, Statuses.RequestStatus.Draft };
        if (BoolArg(c, "overdue"))
        {
            query = query.Where(r => r.TargetDate < now && !finished.Contains(r.Status));
            applied.Add("needed-by date is in the past and the request is not finished");
        }
        else if (c.Args.ContainsKey("due_in_days"))
        {
            var days = IntArg(c, "due_in_days", 14, 365);
            query = query.Where(r => r.TargetDate >= now && r.TargetDate <= now.AddDays(days));
            applied.Add($"needed within {days} days");
        }

        var soonest = Normalize(c.Arg("sort")).StartsWith("soon") || c.Args.ContainsKey("due_in_days") || BoolArg(c, "overdue");
        var total = await query.CountAsync(ct);
        var ordered = soonest ? query.OrderBy(r => r.TargetDate) : query.OrderByDescending(r => r.CreatedAt);
        var rows = await ordered.Take(Limit(c))
            .Select(r => new { r.RequestNumber, r.Title, r.Status, r.Priority, r.TargetDate, r.EstimatedBudget })
            .ToListAsync(ct);

        return Make("Requests", new AiTable(soonest ? "Requests, soonest needed first" : "Requests, newest first", ["Number", "Title", "Status", "Priority", "Needed by", "Estimated budget"],
            rows.Select(r => new[] { r.RequestNumber, Cut(r.Title, 60), r.Status, r.Priority, Day(r.TargetDate), r.EstimatedBudget?.ToString("N2") ?? "-" }).ToList()), Applied([.. applied]) + ShownOf(rows.Count, total));
    }

    private async Task<AiDataResult> TendersToolAsync(Who w, AiToolCall c, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var query = TendersFor(w);
        var applied = new List<string?>();

        var status = MatchStatus(c.Arg("status"), TenderStatuses);
        if (status != null) { query = query.Where(t => t.Status == status); applied.Add($"status = {status}"); }
        var text = Text(c, "text");
        if (text.Length > 0) { query = query.Where(t => t.Title.Contains(text) || t.TenderNumber.Contains(text)); applied.Add($"title contains \"{text}\""); }
        if (c.Args.ContainsKey("closing_in_days"))
        {
            var days = IntArg(c, "closing_in_days", 7, 365);
            query = query.Where(t => t.SubmissionDeadline >= now && t.SubmissionDeadline <= now.AddDays(days));
            applied.Add($"submission deadline within {days} days (from now, UTC)");
        }
        if (BoolArg(c, "without_offers"))
        {
            query = query.Where(t => !t.Bids.Any(b => b.Status != Statuses.BidStatus.Draft));
            applied.Add("no submitted offers yet");
        }

        var total = await query.CountAsync(ct);
        var byDeadline = Normalize(c.Arg("sort")).StartsWith("dead") || c.Args.ContainsKey("closing_in_days");
        var ordered = byDeadline ? query.OrderBy(t => t.SubmissionDeadline) : query.OrderByDescending(t => t.CreatedAt);
        var rows = await ordered.Take(Limit(c))
            .Select(t => new
            {
                t.TenderNumber, t.Title, t.Status, t.SubmissionDeadline, Offers = t.Bids.Count(b => b.Status != Statuses.BidStatus.Draft),
                Report = t.EvaluationReports.OrderByDescending(r => r.CreatedAt)
                    .Select(r => new { r.Status, Recommended = (string?)r.RecommendedBid!.SupplierProfile.CompanyName }).FirstOrDefault(),
            })
            .ToListAsync(ct);

        // the Manager and the Top Committee decide on the committee's report, so they also see its status and the recommended supplier
        var title = byDeadline ? "Tenders, earliest deadline first" : "Tenders, newest first";
        var table = w.Role is Roles.SourcingManager or Roles.TopCommittee
            ? new AiTable(title, ["Tender", "Title", "Status", "Submission deadline", "Offers", "Report", "Recommended supplier"],
                rows.Select(r => new[] { r.TenderNumber, Cut(r.Title, 50), r.Status, Day(r.SubmissionDeadline), r.Offers.ToString(), r.Report?.Status ?? "No report yet", Cut(r.Report?.Recommended, 50) is { Length: > 0 } rec ? rec : "-" }).ToList())
            : new AiTable(title, ["Tender", "Title", "Status", "Submission deadline", "Offers"],
                rows.Select(r => new[] { r.TenderNumber, Cut(r.Title, 60), r.Status, Day(r.SubmissionDeadline), r.Offers.ToString() }).ToList());
        return Make("Tenders", table, Applied([.. applied]) + ShownOf(rows.Count, total) + $" Today (UTC): {Day(now)}.");
    }

    private async Task<AiDataResult> OffersToolAsync(Who w, AiToolCall c, CancellationToken ct)
    {
        var query = BidsFor(w);
        var applied = new List<string?>();

        var number = Text(c, "tender_number", 40);
        if (number.Length > 0)
        {
            var wanted = RecordNumber(number) ?? Normalize(number);
            query = query.Where(b => b.Tender.TenderNumber == wanted);
            applied.Add($"tender = {wanted.ToUpperInvariant()}");
        }
        var status = MatchStatus(c.Arg("status"), BidStatuses);
        if (status != null) { query = query.Where(b => b.Status == status); applied.Add($"status = {status}"); }

        var total = await query.CountAsync(ct);
        var sort = Normalize(c.Arg("sort"));
        var ordered = sort.StartsWith("price") ? query.OrderBy(b => b.TotalPrice)
            : sort.StartsWith("deliv") ? query.OrderBy(b => b.DeliveryDays)
            : query.OrderByDescending(b => b.SubmittedAt ?? DateTime.MinValue);
        var rows = await ordered.Take(Limit(c))
            .Select(b => new { b.Tender.TenderNumber, Supplier = b.SupplierProfile.CompanyName, b.Status, b.TotalPrice, b.Currency, b.DeliveryDays, b.SubmittedAt })
            .ToListAsync(ct);

        var showSupplier = w.Role != Roles.Supplier;
        var table = showSupplier
            ? new AiTable("Offers", ["Tender", "Supplier", "Status", "Total price", "Delivery (days)", "Submitted"],
                rows.Select(r => new[] { r.TenderNumber, Cut(r.Supplier, 50), r.Status, Money(r.TotalPrice, r.Currency), r.DeliveryDays.ToString(), Day(r.SubmittedAt) }).ToList())
            : new AiTable("Your offers", ["Tender", "Status", "Total price", "Delivery (days)", "Submitted"],
                rows.Select(r => new[] { r.TenderNumber, r.Status, Money(r.TotalPrice, r.Currency), r.DeliveryDays.ToString(), Day(r.SubmittedAt) }).ToList());
        var order = sort.StartsWith("price") ? "lowest price first" : sort.StartsWith("deliv") ? "shortest delivery first" : "newest first";
        return Make("Offers", table, Applied([.. applied]) + ShownOf(rows.Count, total) + $" Order: {order}.");
    }

    private async Task<AiDataResult> CountsToolAsync(Who w, AiToolCall c, CancellationToken ct)
    {
        var subject = Normalize(c.Arg("subject"));
        var groupBy = Normalize(c.Arg("group_by"));
        List<(string Key, int Count)> groups;
        string title;

        switch (subject)
        {
            case "requests" when w.Role is Roles.Requester or Roles.SourcingOfficer or Roles.Admin:
                groups = (await RequestsFor(w).GroupBy(r => r.Status).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct)).Select(g => (g.Key, g.N)).ToList();
                title = w.Role == Roles.Requester ? "Your requests by status" : "Requests by status (drafts not counted)";
                break;
            case "tenders":
                groups = (await TendersFor(w).GroupBy(t => t.Status).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct)).Select(g => (g.Key, g.N)).ToList();
                title = "Tenders you can see, by status";
                break;
            case "offers":
                var offerQuery = BidsFor(w);
                var onlyTender = Text(c, "tender_number", 40);
                if (onlyTender.Length > 0)
                {
                    var wantedTender = RecordNumber(onlyTender) ?? Normalize(onlyTender);
                    offerQuery = offerQuery.Where(b => b.Tender.TenderNumber == wantedTender);
                }
                groups = (await offerQuery.GroupBy(b => b.Status).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct)).Select(g => (g.Key, g.N)).ToList();
                title = (w.Role == Roles.Supplier ? "Your offers by status" : "Offers you can see, by status") + (onlyTender.Length > 0 ? $" (tender {(RecordNumber(onlyTender) ?? Normalize(onlyTender)).ToUpperInvariant()})" : "");
                break;
            case "invitations" when w.Role == Roles.Supplier:
                groups = (await _db.Invitations.AsNoTracking().Where(i => i.SupplierProfileId == w.SupplierId).GroupBy(i => i.Status).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct)).Select(g => (g.Key, g.N)).ToList();
                title = "Your invitations by status";
                break;
            case "suppliers" when w.Role is Roles.Admin or Roles.SourcingOfficer:
                if (groupBy.StartsWith("cat"))
                {
                    groups = (await _db.SupplierProfiles.AsNoTracking().GroupBy(s => s.Category).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct)).Select(g => (g.Key, g.N)).ToList();
                    title = "Suppliers by category";
                }
                else
                {
                    groups = (await _db.SupplierProfiles.AsNoTracking().GroupBy(s => s.Status).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct)).Select(g => (g.Key, g.N)).ToList();
                    title = "Suppliers by status";
                }
                break;
            case "users" when w.Role == Roles.Admin:
                groups = (await (from ur in _db.UserRoles join r in _db.Roles on ur.RoleId equals r.Id group r by r.Name into g select new { Key = g.Key, N = g.Count() }).ToListAsync(ct))
                    .Select(g => (Roles.LabelFor(g.Key ?? ""), g.N)).ToList();
                title = "Users by role";
                break;
            default:
                return Make("Counts", null, "That count is not available for this user. Say so, and do not guess a number.");
        }

        var total = groups.Sum(g => g.Count);
        return Make("Counts", new AiTable(title, ["Group", "Count"], groups.OrderByDescending(g => g.Count).Select(g => new[] { g.Key, g.Count.ToString() }).ToList()),
            $"Total: {total}. These numbers were counted by the system; use them as they are.");
    }

    private async Task<AiDataResult> DeadlinesToolAsync(Who w, AiToolCall c, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var days = IntArg(c, "days", 14, 365);
        var until = now.AddDays(days);

        if (w.Role == Roles.Supplier)
        {
            var rows = await _db.Invitations.AsNoTracking()
                .Where(i => i.SupplierProfileId == w.SupplierId && i.Tender.SubmissionDeadline >= now && i.Tender.SubmissionDeadline <= until
                    && i.Status != Statuses.InvitationStatus.Declined && i.Status != Statuses.InvitationStatus.Expired)
                .OrderBy(i => i.Tender.SubmissionDeadline).Take(Limit(c))
                .Select(i => new
                {
                    i.Tender.TenderNumber, i.Tender.Title, i.Tender.SubmissionDeadline, Invitation = i.Status,
                    MyOffer = i.Tender.Bids.Where(b => b.SupplierProfileId == w.SupplierId).Select(b => b.Status).FirstOrDefault(),
                }).ToListAsync(ct);
            return Make("Deadlines", new AiTable($"Your tender deadlines in the next {days} days", ["Tender", "Title", "Submission deadline", "My invitation", "My offer"],
                rows.Select(r => new[] { r.TenderNumber, Cut(r.Title, 60), Time(r.SubmissionDeadline), r.Invitation, r.MyOffer ?? "none yet" }).ToList()), $"Now (UTC): {Time(now)}. Earliest first.");
        }

        if (w.Role == Roles.Requester && !w.Seat)
        {
            return await RequestsToolAsync(w, new AiToolCall("requests", new() { ["due_in_days"] = days.ToString(), ["limit"] = Limit(c).ToString() }), ct);
        }

        var tenders = await TendersFor(w)
            .Where(t => t.SubmissionDeadline >= now && t.SubmissionDeadline <= until)
            .OrderBy(t => t.SubmissionDeadline).Take(Limit(c))
            .Select(t => new { t.TenderNumber, t.Title, t.Status, t.SubmissionDeadline, Offers = t.Bids.Count(b => b.Status != Statuses.BidStatus.Draft) })
            .ToListAsync(ct);
        return Make("Deadlines", new AiTable($"Tender deadlines in the next {days} days", ["Tender", "Title", "Status", "Submission deadline", "Offers"],
            tenders.Select(r => new[] { r.TenderNumber, Cut(r.Title, 60), r.Status, Time(r.SubmissionDeadline), r.Offers.ToString() }).ToList()), $"Now (UTC): {Time(now)}. Earliest first.");
    }

    private async Task<AiDataResult> InvitationsToolAsync(Who w, AiToolCall c, CancellationToken ct)
    {
        var query = _db.Invitations.AsNoTracking().Where(i => i.SupplierProfileId == w.SupplierId);
        var applied = new List<string?>();
        var status = MatchStatus(c.Arg("status"), InvitationStatuses);
        if (status != null) { query = query.Where(i => i.Status == status); applied.Add($"status = {status}"); }

        var rows = await query.OrderByDescending(i => i.SentAt).Take(Limit(c))
            .Select(i => new { i.Tender.TenderNumber, i.Tender.Title, i.Status, i.Tender.SubmissionDeadline, i.ExpiresAt })
            .ToListAsync(ct);
        return Make("MyInvitations", new AiTable("Your tender invitations", ["Tender", "Title", "Status", "Submission deadline", "Invitation expires"],
            rows.Select(r => new[] { r.TenderNumber, Cut(r.Title, 60), r.Status, Day(r.SubmissionDeadline), Day(r.ExpiresAt) }).ToList()), Applied([.. applied]));
    }

    private async Task<AiDataResult> ProgressToolAsync(Who w, AiToolCall c, CancellationToken ct)
    {
        var uid = w.UserId;
        var query = _db.Tenders.AsNoTracking().Where(t => t.CommitteeMembers.Any(m => m.UserId == uid));      // only tenders where this user sits on the committee
        var number = Text(c, "tender_number", 40);
        if (number.Length > 0)
        {
            var wanted = RecordNumber(number) ?? Normalize(number);
            query = query.Where(t => t.TenderNumber == wanted);
        }

        var rows = await query.OrderByDescending(t => t.CreatedAt).Take(Limit(c))
            .Select(t => new
            {
                t.TenderNumber, t.Title, t.Status,
                Offers = t.Bids.Count(b => b.Status != Statuses.BidStatus.Draft),
                Criteria = t.EvaluationCriteria.Count(),
                TechnicalScored = _db.TechnicalEvaluations.Where(e => e.EvaluatorUserId == uid && e.Bid.TenderId == t.Id && e.Score != null).Select(e => e.BidId).Distinct().Count(),
                FinancialDone = _db.FinancialEvaluations.Where(e => e.EvaluatorUserId == uid && e.Bid.TenderId == t.Id && e.Score != null).Select(e => e.BidId).Distinct().Count(),
            }).ToListAsync(ct);

        return Make("EvaluationProgress", new AiTable("Your own scoring progress", ["Tender", "Title", "Status", "Offers", "Offers with my technical scores", "Offers with my financial score"],
                rows.Select(r => new[] { r.TenderNumber, Cut(r.Title, 50), r.Status, r.Offers.ToString(), r.TechnicalScored.ToString(), r.FinancialDone.ToString() }).ToList()),
            "These are this user's OWN scores only. Other members' scores are never shown. An offer counts as scored when at least one score was saved for it.");
    }

    private async Task<AiDataResult> VotesToolAsync(Who w, AiToolCall c, CancellationToken ct)
    {
        var query = _db.Tenders.AsNoTracking().Where(t => t.Status == Statuses.TenderStatus.AwaitingCommitteeDecision);
        var number = Text(c, "tender_number", 40);
        if (number.Length > 0)
        {
            var wanted = RecordNumber(number) ?? Normalize(number);
            query = query.Where(t => t.TenderNumber == wanted);
        }

        var rows = await query.OrderByDescending(t => t.CreatedAt).Take(Limit(c))
            .Select(t => new
            {
                t.TenderNumber, t.Title,
                Approve = t.TopCommitteeVotes.Count(v => v.Vote == Statuses.VoteType.Approve),
                Reject = t.TopCommitteeVotes.Count(v => v.Vote == Statuses.VoteType.Reject),
                MoreInfo = t.TopCommitteeVotes.Count(v => v.Vote == Statuses.VoteType.NeedMoreInfo),
                Retender = t.TopCommitteeVotes.Count(v => v.Vote == Statuses.VoteType.Retender),
            }).ToListAsync(ct);

        return Make("Votes", new AiTable("Votes cast on tenders waiting for the decision", ["Tender", "Title", "Approve", "Reject", "Need more info", "Retender"],
                rows.Select(r => new[] { r.TenderNumber, Cut(r.Title, 50), r.Approve.ToString(), r.Reject.ToString(), r.MoreInfo.ToString(), r.Retender.ToString() }).ToList()),
            "Counts only. Who voted what is not shown.");
    }

    private async Task<AiDataResult> AuditToolAsync(AiToolCall c, CancellationToken ct)
    {
        var query = _db.AuditLogs.AsNoTracking();
        var applied = new List<string?>();
        var action = Text(c, "action_text");
        if (action.Length > 0) { query = query.Where(a => a.Action.Contains(action) || a.EntityName.Contains(action)); applied.Add($"action or record contains \"{action}\""); }
        var who = Text(c, "user_text");
        if (who.Length > 0) { query = query.Where(a => a.User != null && a.User.FullName.Contains(who)); applied.Add($"user name contains \"{who}\""); }
        if (c.Args.ContainsKey("days"))
        {
            var days = IntArg(c, "days", 7, 365);
            var since = DateTime.UtcNow.AddDays(-days);
            query = query.Where(a => a.CreatedAt >= since);
            applied.Add($"last {days} days");
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).Take(Limit(c))
            .Select(a => new { a.CreatedAt, User = a.User != null ? a.User.FullName : "system", a.Action, a.EntityName, a.EntityId, a.NewValues })
            .ToListAsync(ct);
        return Make("AuditLogs", new AiTable("Audit log records, newest first", ["Time (UTC)", "User", "Action", "Record", "Details"],
            rows.Select(r => new[] { Time(r.CreatedAt), Cut(r.User, 40), r.Action, $"{r.EntityName} {Cut(r.EntityId, 20)}", Cut(r.NewValues, 80) }).ToList()), Applied([.. applied]) + ShownOf(rows.Count, total));
    }

    private async Task<AiDataResult> UsersToolAsync(AiToolCall c, CancellationToken ct)
    {
        var query = _db.Users.AsNoTracking();
        var applied = new List<string?>();
        var roleName = Roles.All.FirstOrDefault(r => Normalize(r) == Normalize(c.Arg("role")).Replace(" ", ""));
        if (roleName != null)
        {
            query = query.Where(u => _db.UserRoles.Any(ur => ur.UserId == u.Id && _db.Roles.Any(r => r.Id == ur.RoleId && r.Name == roleName)));
            applied.Add($"role = {roleName}");
        }
        var text = Text(c, "text");
        if (text.Length > 0) { query = query.Where(u => u.FullName.Contains(text)); applied.Add($"name contains \"{text}\""); }
        if (c.Args.ContainsKey("created_in_days"))
        {
            var since = DateTime.UtcNow.AddDays(-IntArg(c, "created_in_days", 7, 365));
            query = query.Where(u => u.CreatedAt >= since);
            applied.Add($"created in the last {IntArg(c, "created_in_days", 7, 365)} days");
        }
        var loginDays = c.Args.ContainsKey("logged_in_in_days");
        if (loginDays)
        {
            var since = DateTime.UtcNow.AddDays(-IntArg(c, "logged_in_in_days", 7, 365));
            query = query.Where(u => u.LastLoginAt >= since);
            applied.Add($"signed in during the last {IntArg(c, "logged_in_in_days", 7, 365)} days");
        }

        var total = await query.CountAsync(ct);
        var ordered = loginDays ? query.OrderByDescending(u => u.LastLoginAt) : query.OrderByDescending(u => u.CreatedAt);
        var rows = await ordered.Take(Limit(c)).Select(u => new { u.FullName, u.UserType, u.IsActive, u.CreatedAt, u.LastLoginAt }).ToListAsync(ct);   // never e-mails, passwords or biometrics
        return Make("Users", new AiTable(loginDays ? "Users, latest sign-in first" : "Users, newest first", ["Name", "Type", "Active", "Created", "Last sign-in (UTC)"],
            rows.Select(r => new[] { Cut(r.FullName, 40), r.UserType, r.IsActive ? "Yes" : "No", Day(r.CreatedAt), Time(r.LastLoginAt) }).ToList()), Applied([.. applied]) + ShownOf(rows.Count, total));
    }

    private async Task<AiDataResult> SuppliersToolAsync(Who w, AiToolCall c, CancellationToken ct)
    {
        var query = _db.SupplierProfiles.AsNoTracking();
        var applied = new List<string?>();
        // a Sourcing Officer only needs the approved ones (to invite them); the Admin sees every status
        var status = w.Role == Roles.Admin ? MatchStatus(c.Arg("status"), SupplierStatuses) : Statuses.SupplierStatus.Approved;
        if (status != null) { query = query.Where(s => s.Status == status); applied.Add($"status = {status}"); }
        var category = Text(c, "category");
        if (category.Length > 0) { query = query.Where(s => s.Category.Contains(category)); applied.Add($"category contains \"{category}\""); }
        var text = Text(c, "text");
        if (text.Length > 0) { query = query.Where(s => s.CompanyName.Contains(text)); applied.Add($"name contains \"{text}\""); }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(s => s.CompanyName).Take(Limit(c)).Select(s => new { s.CompanyName, s.Category, s.Country, s.City, s.Status, s.CreatedAt }).ToListAsync(ct);
        return Make("Suppliers", new AiTable("Suppliers", ["Company", "Category", "Country", "City", "Status", "Registered"],
            rows.Select(r => new[] { Cut(r.CompanyName, 60), r.Category, r.Country, r.City, r.Status, Day(r.CreatedAt) }).ToList()), Applied([.. applied]) + ShownOf(rows.Count, total));
    }

    // ---------------------------------------------------------------- the record the user is looking at (AI-21)

    /// <summary>
    /// "TND-001 - Title" when this user may see the request or tender with this number, otherwise null. Checked with the SAME scope as every lookup,
    /// so a made-up address in the browser can never reveal a record (or its number) the user may not see.
    /// </summary>
    public async Task<string?> VisibleRecordLabelAsync(string role, string userId, string number, CancellationToken ct)
    {
        var who = await WhoAsync(role, userId, ct);
        var tender = await TendersFor(who).Where(t => t.TenderNumber == number).Select(t => new { t.TenderNumber, t.Title }).FirstOrDefaultAsync(ct);
        if (tender != null) return $"Tender {tender.TenderNumber}: {Cut(tender.Title, 60)}";
        var request = await RequestsFor(who).Where(r => r.RequestNumber == number).Select(r => new { r.RequestNumber, r.Title }).FirstOrDefaultAsync(ct);
        return request != null ? $"Request {request.RequestNumber}: {Cut(request.Title, 60)}" : null;
    }

    /// <summary>The number of the record a page of the system is about: from an address such as /Sourcing/Tenders/Details/12 or /Sourcing/Compliance?tenderId=12. Only a NUMBER comes out, and it is checked by <see cref="VisibleRecordLabelAsync"/> before it is used.</summary>
    public async Task<string?> RecordNumberOfPageAsync(string? pageUrl, CancellationToken ct)
    {
        var target = AiPageContext.Parse(pageUrl);
        if (target is null) return null;
        var id = target.Id;
        return target.Kind switch
        {
            AiPageKind.Tender => await _db.Tenders.AsNoTracking().Where(t => t.Id == id).Select(t => t.TenderNumber).FirstOrDefaultAsync(ct),
            AiPageKind.Request => await _db.PurchaseRequisitions.AsNoTracking().Where(r => r.Id == id).Select(r => r.RequestNumber).FirstOrDefaultAsync(ct),
            AiPageKind.Report => await _db.EvaluationReports.AsNoTracking().Where(r => r.Id == id).Select(r => r.Tender.TenderNumber).FirstOrDefaultAsync(ct),
            AiPageKind.Bid => await _db.Bids.AsNoTracking().Where(b => b.Id == id).Select(b => b.Tender.TenderNumber).FirstOrDefaultAsync(ct),
            _ => null,
        };
    }

    /// <summary>The overview only (no lookup), for questions the planner says need no data.</summary>
    public Task<AiDataResult> OverviewAsync(string role, string userId, CancellationToken ct) => SnapshotAsync(role, userId, ct);

    /// <summary>The user's OWN notifications only (UserId == me), whatever the role.</summary>
    private async Task<AiDataResult> NotificationsToolAsync(Who w, AiToolCall c, CancellationToken ct)
    {
        var query = _db.Notifications.AsNoTracking().Where(x => x.UserId == w.UserId);
        var applied = new List<string?>();
        if (BoolArg(c, "unread")) { query = query.Where(x => !x.IsRead); applied.Add("unread only"); }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CreatedAt).Take(Limit(c))
            .Select(x => new { x.CreatedAt, x.Title, x.Message, x.IsRead })
            .ToListAsync(ct);

        var table = new AiTable("Your latest notifications", ["Time (UTC)", "Title", "Status"],
            rows.Select(r => new[] { Time(r.CreatedAt), Cut(r.Title, 80), r.IsRead ? "Read" : "Unread" }).ToList());
        var messages = string.Join("\n", rows.Select(r => $"- {Cut(r.Title, 80)}: {Cut(r.Message, 120)}"));
        return Make("Notifications", table, Applied([.. applied]) + ShownOf(rows.Count, total) + (messages.Length > 0 ? "\nMessages:\n" + messages : ""));
    }

    /// <summary>Admin only (the catalog gives this tool to no other role): messages from the public Contact Us form.</summary>
    private async Task<AiDataResult> ContactMessagesToolAsync(AiToolCall c, CancellationToken ct)
    {
        var query = _db.ContactMessages.AsNoTracking();
        var applied = new List<string?>();
        if (BoolArg(c, "unread")) { query = query.Where(m => !m.IsRead); applied.Add("unread only"); }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(m => m.IsRead).ThenByDescending(m => m.CreatedAt).Take(Limit(c))
            .Select(m => new { m.CreatedAt, m.FullName, m.Subject, m.IsRead })
            .ToListAsync(ct);

        return Make("ContactMessages", new AiTable("Contact messages (unread first)", ["Received (UTC)", "From", "Subject", "Status"],
            rows.Select(r => new[] { Time(r.CreatedAt), Cut(r.FullName, 40), Cut(r.Subject, 60), r.IsRead ? "Read" : "Unread" }).ToList()),
            Applied([.. applied]) + ShownOf(rows.Count, total));
    }
}
