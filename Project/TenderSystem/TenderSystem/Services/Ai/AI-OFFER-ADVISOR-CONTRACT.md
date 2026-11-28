# Offer advisor service: agreement with Evaluation (AI-22)

Written for Marwa (Evaluation). This is the service you asked for in `Areas/Evaluation/WADEA-EVALUATION-AI-PROMPT.txt`.

* **Class:** `TenderSystem.Services.Ai.AiOfferAdvisorService` (file `Services/Ai/Writers/AiOfferAdvisorService.cs`)
* **Registered:** automatically (scoped), in `AiGenerationModule`. Inject it in your controller or service.
* **Model:** the same one as the Admin reports and the analysis cards (`AI_INSIGHTS_*`, or the shared `AI_ANALYSIS_PROVIDER` / `AI_API_*` when `AI_INSIGHTS_*` is not set). No new key.
* **What it does:** reads the facts YOU prepare, asks the model for advice, checks the answer with code, and returns a clean result. It never reads the database, never saves anything, never awards, never scores.

## Who does what

| You (Evaluation) | This service |
|---|---|
| Check the user is a committee member of the tender and the tender is in an evaluating state | Nothing about users or tenders: it trusts only the input you give |
| Choose the **eligible** offers (submitted, Compliant or Accepted) | Refuses any suggestion that is not one of the offers you sent |
| Fill the facts from the database (prices, days, criteria, texts) | Prepares comparison lines in C# (lowest and highest price **per currency**, shortest delivery, longest validity) |
| The endpoint (POST + antiforgery), the card, the loading state, the "stale" notice | Rate limit per user (3 per minute), timeout, one retry when the answer breaks the rules |
| Audit of the user's action ("advice requested") | An `AiGenerated` audit row without any content (transport only) |

## Call it

```csharp
var request = new AiOfferAdvisorRequest(
    TenderId: tender.Id, TenderNumber: tender.TenderNumber, TenderTitle: tender.Title,
    Requirements: requirementsText,                       // buyer text, up to a few thousand characters
    Criteria: criteria.Select(c => new AiOfferCriterion(c.Id, c.Type, c.Question, c.WeightPercent, c.MaxScore)).ToList(),
    Offers: bids.Select(b => new AiOfferInput(b.Id, b.Supplier.CompanyName, b.TotalPrice, b.Currency, b.DeliveryDays,
                b.ValidityDays, b.PaymentTerms, b.TechnicalSummary, items, documentNames, complianceNote)).ToList(),
    Limits: ["Pictures and scans of the offers were not read."]);

AiOfferAdvisorResult result = await _advisor.AnalyzeAsync(request, userKey: User.GetUserId().ToString(), ct);
```

`userKey` is only used for the rate limit (use the user id). Always pass the request's `CancellationToken`.

### Input rules (broken rule gives `invalid-request`, no model call)
* Tender id, number (up to 60 characters) and title (up to 300) are given. Every criterion has a question, a weight of 0 or more and `MaxScore > 0` (an empty criteria list is allowed). Criteria weights are **not** assumed to be 70/30 or any fixed split: send the real ones.
* Offer ids are unique. Every offer has a supplier name and a currency. Price, delivery days and validity days are not negative.
* At most **12 offers** and about **16,000 characters** of facts (`too-large` otherwise). Cut long text yourself.
* `Evidence` (optional per offer) is a list of `(Reference, Text)`: passages read from the offer files, for example `("Offer file drop-test.pdf, page 1", "Drop test passed...")`. Without it the advice uses only your facts. We will fill this from the tender document index in a later phase; the signature will not change.

## Result

```csharp
result.Ok          // true for "ok" and "no-suggestion"
result.Status      // see the table
result.Message     // a sentence you can show to the user
result.Advice      // null unless Ok
result.Model       // model name, for the footer: "AI advice (mistralai/...)"
```

`Advice`: `SuggestedBidId` (**nullable**), `SuggestedSupplier`, `Reasons`, `Checks` (what a person should verify), `Alternatives` (`BidId`, `Supplier`, `Comparison`), `CoverageNotes` (your `Limits` plus what the AI could not read), `GeneratedAtUtc`, `Fingerprint`.

| Status | Ok | Meaning | Show |
|---|---|---|---|
| `ok` | yes | There is a suggestion | The card |
| `no-suggestion` | yes | The data does not support a safe suggestion, or there is only one offer | The `Message` and the checks, no "suggested" badge |
| `no-eligible-offers` | no | You sent no offers | "No eligible offers yet" |
| `invalid-request` | no | Input rule broken | A bug on your side: log it |
| `too-large` | no | Too many offers or too much text | "Too many offers for the advisor" |
| `rate-limited` | no | The user asked too often | Ask them to wait a minute |
| `disabled` | no | No AI configured | Hide or disable the button |
| `timeout` | no | The model was too slow | "Try again" |
| `unavailable` | no | The model could not be reached | "Try again later" |
| `rejected` | no | The model broke the rules twice | "No advice this time, try again" |
| `cancelled` | no | The user left the page | Nothing |

The service never throws for a model problem.

### Example: a suggestion
```json
{ "ok": true, "status": "ok", "message": "",
  "advice": {
    "suggestedBidId": 12, "suggestedSupplier": "Amber Glass Co",
    "reasons": ["Amber Glass Co states a 2.4 mm wall and attaches a drop-test certificate, which the requirements ask for."],
    "checks": ["Ask Cheap Bottles Ltd for the wall thickness and a certificate before comparing it."],
    "alternatives": [{ "bidId": 15, "supplier": "Cheap Bottles Ltd", "comparison": "Cheap Bottles Ltd has the lowest price (1,900.00 JOD) but states no wall thickness and no certificate." }],
    "coverageNotes": ["Pictures and scans of the offers were not read."],
    "generatedAtUtc": "2026-11-24T09:30:00Z", "fingerprint": "9f3c1a22b7d04e55" } }
```

### Example: no suggestion
```json
{ "ok": true, "status": "no-suggestion",
  "message": "The advisor could not name one offer: Amber Glass Co and Premium Pack GmbH both meet the written requirements and the data does not separate them.",
  "advice": { "suggestedBidId": null, "suggestedSupplier": null, "reasons": [], "checks": ["..."], "alternatives": [], "coverageNotes": ["..."], "generatedAtUtc": "...", "fingerprint": "..." } }
```

### Example: an error
```json
{ "ok": false, "status": "timeout", "message": "The AI took too long to answer. Please try again.", "advice": null, "model": "..." }
```

## Rules the code enforces on every answer (so you do not have to)
* The suggested offer and every alternative must be one of YOUR eligible offers; no duplicates; the suggestion is never its own alternative.
* A suggestion needs at least one reason; no suggestion needs an explanation.
* Every number in the text must exist in your facts (same check as the Admin reports). No invented scores, percentages or totals.
* No HTML, links, placeholders, "confidence" or "probability" words, no "award", "winner" or "should be selected" wording.
* The model compares **blind**: offers are sent as `[B12]`, `[B15]` without supplier names, and the server puts the real names back afterwards. File references are written as `(source: ...)` by the server.
* Supplier text is wrapped as untrusted data; a sentence like "ignore the rules" inside an offer changes nothing.
* Prices of different currencies are never compared with each other.
* `Fingerprint` (16 hex characters, SHA-256 of the facts) is the same for the same facts. Keep it with the card. If you build the facts again later and get a different fingerprint, the offers changed, so show "Offers changed since this advice" (stale). You can get it **without a model call**: `AiOfferAdvisorService.FingerprintOf(request)` (static) builds the facts from a request and returns the value, or `""` when the request is invalid. Compare it with the saved one when the page opens.

## What the model does NOT decide
Scores, votes, ranking, the award, or anything that is saved. The user always votes themselves. Show the card as advice, with its checks.

## Settings (names only, no secrets)
* `AI_INSIGHTS_API_KEY`, `AI_INSIGHTS_API_MODEL`, `AI_INSIGHTS_API_BASE_URL`, `AI_INSIGHTS_TIMEOUT_SECONDS`: the model (optional; the shared `AI_ANALYSIS_PROVIDER` / `AI_API_*` are used without them).
* `AI_OFFER_ADVISOR_TIMEOUT_SECONDS` (default 240): time allowed for one attempt. A reasoning model can need more: see the note below.
* `AI_OFFER_ADVISOR_MAX_PER_MINUTE` (default 3): requests per user per minute.

## Speed: plan the page for a slow answer
A hosted reasoning model answers in about 30 seconds to a few minutes. So: call the service from a button (not on page load), show a loading state with a cancel path, disable the button while a request runs, and keep the previous advice on screen if a new one fails.

## What is left for you
1. Map `Status` and a **nullable** `SuggestedBidId` into your view model: `OfferAssistantResultViewModel.SuggestedBidId` is a non-nullable `int` today, so `no-suggestion` needs a nullable field or a flag.
2. The POST action with `[Authorize]`, antiforgery, the committee-membership check, and the call.
3. An audit row for the user's action (counts and ids only, never the advice text).
4. The card, loading state and stale notice.

## Tests that were run (scratch harness with a fake model server, 45 checks, all passed)
Suggestion, no suggestion, name replacement, blind facts, prices per currency, dynamic weights, evidence tags, one offer, no offers, too many offers, duplicate or negative input, every refusal rule (not eligible, bad number, HTML, links, brackets, confidence words, award words, duplicate or self alternatives, missing reasons, missing tags, text or code-fenced JSON), one retry, prompt injection inside supplier text, disabled, rate limit, fingerprint stability, cancellation, timeout.
