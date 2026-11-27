using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Ai;

/// <summary>What the planner chose: a tool name ("classic" and "none" are special) and its text arguments.</summary>
public sealed record AiToolCall(string Tool, Dictionary<string, string> Args)
{
    public const string Classic = "classic";
    public const string None = "none";

    public string Arg(string name) => Args.TryGetValue(name, out var v) ? v.Trim() : "";
}

/// <summary>One lookup the planner may choose. Doc is shown to the model; Roles says who may use it (the server checks it again before running).</summary>
public sealed record AiToolDef(string Name, string Doc);

/// <summary>
/// The list of read-only lookups ("tools") per role (AI-21). The model only CHOOSES a tool and its filters; the server runs it, and every tool
/// starts from the user's own scope inside the query (see RoleDataService.Tools.cs). A tool a role does not have is refused even if the model asks for it.
/// </summary>
public static class AiLookupCatalog
{
    private static readonly AiToolDef Requests = new("requests",
        "requests(status, text, due_in_days, overdue, sort, limit): purchase requests. status is one of Draft, Submitted, Under Review, Approved, Rejected, Returned, Converted to Tender. text searches the title. due_in_days lists requests needed within that many days; overdue=true lists those past their needed-by date and not finished. sort is newest or soonest.");
    private static readonly AiToolDef Tenders = new("tenders",
        "tenders(status, text, closing_in_days, without_offers, sort, limit): tenders the user can see. status is one of Draft, Published, Open, Closed, Under Evaluation, Awaiting Manager Review, Awaiting Committee Decision, Awarded, Cancelled, Rejected, Retender Required. closing_in_days lists tenders whose submission deadline is within that many days; without_offers=true lists tenders with no submitted offers. sort is newest or deadline.");
    private static readonly AiToolDef Offers = new("offers",
        "offers(tender_number, status, sort, limit): offers (bids). status is one of Draft, Submitted, Under Review, Compliant, Non-Compliant, Accepted, Rejected, Withdrawn. sort is price, delivery or newest. tender_number limits to one tender.");
    private static readonly AiToolDef Counts = new("counts",
        "counts(subject, group_by, tender_number): how many. subject is requests, tenders, offers, suppliers, users or invitations; group_by is status (default), role (users) or category (suppliers). tender_number limits offers to ONE tender (use it for \"how many offers does this tender have\").");
    private static readonly AiToolDef Deadlines = new("deadlines",
        "deadlines(days): what is due soon, within days (default 14): tender submission deadlines, or invitation deadlines you have not answered, or requests needed soon.");
    private static readonly AiToolDef Invitations = new("invitations",
        "invitations(status, limit): the tender invitations of this supplier. status is Pending, Accepted, Declined or Expired.");
    private static readonly AiToolDef Progress = new("evaluation_progress",
        "evaluation_progress(tender_number): how far THIS user is with scoring the offers of the tenders where they sit on the committee (their own scores only).");
    private static readonly AiToolDef Votes = new("votes",
        "votes(tender_number): how many committee votes were cast on tenders waiting for the final decision, by vote type. Counts only, never who voted what.");
    private static readonly AiToolDef Audit = new("audit",
        "audit(action_text, user_text, days, limit): audit log records. action_text filters the action (for example Login, Approved), user_text the user's name, days limits to the last days.");
    private static readonly AiToolDef Users = new("users",
        "users(role, text, created_in_days, logged_in_in_days, limit): system users. role is Admin, Requester, SourcingOfficer, EvaluationCommittee, SourcingManager, TopCommittee or Supplier.");
    private static readonly AiToolDef Suppliers = new("suppliers",
        "suppliers(status, category, text, limit): supplier companies. status is Pending, Approved, Rejected or Suspended.");

    public static IReadOnlyList<AiToolDef> For(string role, bool seat)
    {
        var tools = new List<AiToolDef>();
        switch (role)
        {
            case Roles.Admin: tools.AddRange([Counts, Audit, Users, Suppliers]); break;
            case Roles.Requester: tools.AddRange([Requests, Counts, Deadlines]); break;
            case Roles.SourcingOfficer: tools.AddRange([Requests, Tenders, Offers, Counts, Deadlines, Suppliers]); break;
            case Roles.EvaluationCommittee: tools.AddRange([Tenders, Offers, Counts, Deadlines, Progress]); break;
            case Roles.SourcingManager: tools.AddRange([Tenders, Offers, Counts]); break;
            case Roles.TopCommittee: tools.AddRange([Tenders, Offers, Counts, Votes]); break;
            case Roles.Supplier: tools.AddRange([Tenders, Offers, Invitations, Counts, Deadlines]); break;
        }
        // a Requester or Sourcing Manager who also sits on an evaluation committee gets the committee tools too
        if (seat && role is Roles.Requester or Roles.SourcingManager)
            foreach (var t in new[] { Tenders, Offers, Deadlines, Progress })
                if (tools.All(x => x.Name != t.Name)) tools.Add(t);
        return tools;
    }

    public static bool Allows(string role, bool seat, string tool) => For(role, seat).Any(t => t.Name == tool);

    /// <summary>A few examples per role: small local models follow examples much better than rules.</summary>
    public static string Examples(string role) => role switch
    {
        Roles.Admin => """
            "How many tenders are there?" -> {"tool":"counts","args":{"subject":"tenders"}}
            "How many suppliers are approved and how many pending?" -> {"tool":"counts","args":{"subject":"suppliers"}}
            "Who logged in recently?" -> {"tool":"audit","args":{"action_text":"login","limit":"10"}}
            "Which users were created this week?" -> {"tool":"users","args":{"created_in_days":"7"}}
            "Show the last 5 actions" -> {"tool":"audit","args":{"limit":"5"}}
            "Which suppliers are waiting for approval?" -> {"tool":"suppliers","args":{"status":"Pending"}}
            """,
        Roles.Requester => """
            "Which of my requests are waiting for approval?" -> {"tool":"requests","args":{"status":"Submitted"}}
            "Which request is needed first?" -> {"tool":"requests","args":{"sort":"soonest","limit":"5"}}
            "Do I have late requests?" -> {"tool":"requests","args":{"overdue":"true"}}
            "How many requests do I have?" -> {"tool":"counts","args":{"subject":"requests"}}
            "What is the status of PR-001?" -> {"tool":"classic","args":{"number":"PR-001"}}
            """,
        Roles.SourcingOfficer => """
            "Which tenders close this week?" -> {"tool":"tenders","args":{"closing_in_days":"7"}}
            "Which tenders have no offers?" -> {"tool":"tenders","args":{"without_offers":"true"}}
            "How many tenders are published?" -> {"tool":"counts","args":{"subject":"tenders"}}
            "How many offers does TND-002 have?" -> {"tool":"counts","args":{"subject":"offers","tender_number":"TND-002"}}
            "Which requests are waiting for me?" -> {"tool":"requests","args":{"status":"Submitted"}}
            "Compare the offers on TND-002" -> {"tool":"classic","args":{"number":"TND-002"}}
            "Who has the lowest price on TND-002?" -> {"tool":"offers","args":{"tender_number":"TND-002","sort":"price","limit":"3"}}
            """,
        Roles.EvaluationCommittee => """
            "Which tenders do I evaluate?" -> {"tool":"tenders","args":{}}
            "How far am I with my scoring?" -> {"tool":"evaluation_progress","args":{}}
            "What is due soon?" -> {"tool":"deadlines","args":{"days":"14"}}
            "Show the offers of TND-002" -> {"tool":"classic","args":{"number":"TND-002"}}
            "Which offer is cheapest on TND-001?" -> {"tool":"offers","args":{"tender_number":"TND-001","sort":"price","limit":"3"}}
            """,
        Roles.SourcingManager => """
            "What is waiting for my review?" -> {"tool":"tenders","args":{"status":"Awaiting Manager Review"}}
            "How many tenders are at the committee stage?" -> {"tool":"counts","args":{"subject":"tenders"}}
            "Summarize the report of TND-005" -> {"tool":"classic","args":{"number":"TND-005"}}
            "Which supplier is recommended for the tenders waiting for me?" -> {"tool":"tenders","args":{"status":"Awaiting Manager Review"}}
            """,
        Roles.TopCommittee => """
            "What do I have to decide on?" -> {"tool":"tenders","args":{"status":"Awaiting Committee Decision"}}
            "How many votes were cast?" -> {"tool":"votes","args":{}}
            "Show recently awarded tenders" -> {"tool":"tenders","args":{"status":"Awarded"}}
            "Tell me about TND-007" -> {"tool":"classic","args":{"number":"TND-007"}}
            """,
        Roles.Supplier => """
            "What tenders am I invited to?" -> {"tool":"invitations","args":{}}
            "Which invitations have I not answered?" -> {"tool":"invitations","args":{"status":"Pending"}}
            "When is my next deadline?" -> {"tool":"deadlines","args":{"days":"30"}}
            "Which of my offers are still pending?" -> {"tool":"offers","args":{"status":"Submitted"}}
            "How many offers did I submit?" -> {"tool":"counts","args":{"subject":"offers"}}
            "What is the status of my offer on TND-001?" -> {"tool":"offers","args":{"tender_number":"TND-001"}}
            "What are the terms of TND-001?" -> {"tool":"classic","args":{"number":"TND-001"}}
            """,
        _ => "",
    };
}
