namespace TenderSystem.Services.Ai;

/// <summary>What the model says about ONE eligible offer.</summary>
public sealed record AiSummaryOffer(string Supplier, List<string> Strengths, List<string> Weaknesses, string MainRisk);

/// <summary>The validated answer of the model. LeaningSupplier is a suggestion for people to check, never a decision (null = no clear best offer).</summary>
public sealed record AiSummaryResult(string Overview, List<AiSummaryOffer> Offers, List<string> PointsToCheck, string? LeaningSupplier, string LeaningReason);

/// <summary>
/// What the page shows. State: "ok" (a card can be shown), "waiting" (allowed later, see Message), "off" (the AI is switched off),
/// "denied" (this user has nothing to do with this tender: show nothing).
/// </summary>
public sealed record AiSummaryView(string State, string Message, string Audience, AiSummaryResult? Summary, DateTime? CreatedAt,
    string? Model, bool Outdated, string FactsLine, bool CanGenerate);
