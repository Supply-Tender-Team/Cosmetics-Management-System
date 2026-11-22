# AI report service: the contract (AI-16)

Owner of the service: Wadea (`Services/Ai`). First user: Marwa's Admin Reports AI box. Anyone can use it the same way.

The contract is the agreement between the code that calls the service (the caller) and the service: what goes in, what comes out, what the errors are, how long it takes, and who is responsible for what. If both sides follow this page, they can be built separately and will fit when connected.

---

## 1. The service

| | |
|---|---|
| Class | `TenderSystem.Services.Ai.AiReportService` |
| Registered | automatically (`AiGenerationModule`), scoped. No change in `Program.cs`. |
| Inject | `public MyController(AiReportService report) { _report = report; }` |
| Methods | `SummarizeAsync(AiReportFacts, CancellationToken)` short overview. `AnalyzeAsync(AiReportFacts, CancellationToken)` overview plus more findings, review points and details. |
| Also | `Enabled` (can the AI answer?), `ModelName`, `RunAsync(facts, depth, ct)` |

For the Admin Reports box use **`AnalyzeAsync` once** per generation. Show the summary and the key figures first. "Expand" only reveals the rest of the same result (findings, review points, details). It does not call the service again. Use `SummarizeAsync` for small cards.

## 2. What the caller sends: `AiReportFacts`

| Field | Rule |
|---|---|
| `Title` | Report title. Required, at most 200 characters. Server-owned, never typed by a user. |
| `Scope` | The scope in words, for example "All suppliers, 1 Oct 2026 to 31 Oct 2026". Required, at most 400 characters. |
| `RecordCount` | How many records the report really covers (the whole scope, not the 25 visible rows). 0 means "no data". |
| `KeyFigures` | List of `AiReportFigure(Label, Value)`. At most 20. Label at most 80 characters, value at most 120. The system calculated them. |
| `FactsText` | The verified data as plain text: full aggregates first, then a short list of supporting rows. **At most 12,000 characters.** Required when `RecordCount > 0`. |
| `Limits` | Optional honest notes, for example "Only the 40 newest offers are listed; the totals cover all 1,350." Returned unchanged. |

Rules for the caller:
- Load the facts for the **signed-in user** and send only what that user is allowed to see. The service does no permission check.
- Calculate every count, rate, duration and amount in C# or SQL. Keep currencies separate. Never add different currencies.
- Do not send raw HTML, secrets, SQL, or passwords. Text written by users (notes, titles) is allowed: the service treats it as data, never as instructions.
- Missing data must be written as "not available", never as 0.
- If the scope is large, send the full totals and fewer rows, and say so in `Limits`. The service refuses longer text instead of cutting it silently.

## 3. What comes back: `AiReportResult`

| Field | Meaning |
|---|---|
| `Ok` | true only when `Status` is `ok` |
| `Status` | `ok`, `disabled`, `no-data`, `invalid-request`, `too-large`, `timeout`, `unavailable`, `rejected` |
| `Message` | Safe to show to the Admin. Empty when ok. |
| `Narrative` | Only when ok (see below) |
| `Model` | The model name, for the audit log |

`AiReportNarrative`:

| Field | Source |
|---|---|
| `Summary` | the model, checked. 30 to 1,200 characters. |
| `Findings` | the model, checked. Summary: up to 3. Analysis: up to 6. |
| `NeedsAttention` | the model, checked. Points for a person to review. Same limits. |
| `Details` | the model, checked. Analysis only, up to 8 label/value pairs. Empty for a summary. |
| `CoverageNotes` | **the caller's `Limits`, unchanged.** Always show them. |

Mapping to Marwa's `AiReportViewModel`: `Title`, `Scope`, `GeneratedBy`, `GeneratedAt`, `RecordCount` and `KeyFigures` are **yours** (the caller's). `Summary`, `Findings`, `NeedsAttention` and `Details` come from the narrative.

## 4. Errors: what each status means and what to show

| Status | When | What the caller does |
|---|---|---|
| `no-data` | `RecordCount` is 0 | Show "no records", no AI text. The model is not called. |
| `disabled` | AI off or not configured | Show the figures without AI text. Normal reports keep working. |
| `invalid-request` | A field breaks the rules in section 2 | A bug in the caller. Show a generic error. |
| `too-large` | `FactsText` over 12,000 characters | Send fewer rows plus totals, and set `Limits`. |
| `timeout` | The model did not answer in time (see section 6) | "Please try again." |
| `unavailable` | Model not running, key refused, rate limit, error | Show `Message`. Offer retry. |
| `rejected` | The text failed the checks twice | "Please try again." Nothing from the model is shown. |

The service never throws for a model problem. It returns one of these statuses.

## 5. What the service checks (so you do not have to)

- The answer is valid JSON in the fixed shape.
- **Every number in the text exists in the facts** (the facts text, the key figures, the scope and the record count). A number the model calculated or invented is rejected. The model gets one retry with the reason.
- No link, no email address, no placeholder or markup.
- The text does not tell people to award or select a supplier. It may only explain and flag review points.
- The facts are sent to the model as data between markers, so text written by users or suppliers cannot give orders.

What it does **not** check: whether the words are wise, numbers written in words ("thirty"), or whether your facts are right. The caller is responsible for the facts.

## 6. Timing

| | |
|---|---|
| Normal answer | about 10 to 40 seconds, depending on the model |
| One attempt | limited by `AI_REPORT_TIMEOUT_SECONDS` (default 90) |
| Worst case | two attempts, so up to twice that (180 seconds) |
| Concurrency | at most 3 answers at the same time in the whole application |

The browser script must wait at least **200 seconds** (today `reports-ai.js` gives up after 15) and should show progress. Honour the `CancellationToken` so a closed page stops the work.

## 7. Settings (names only, never values)

`AI_INSIGHTS_API_KEY`, `AI_INSIGHTS_API_MODEL`, `AI_INSIGHTS_API_BASE_URL`, `AI_INSIGHTS_TIMEOUT_SECONDS` (the model for the analysis and summary cards, shared with this service), `AI_REPORT_TIMEOUT_SECONDS`. Without `AI_INSIGHTS_*` the shared AI settings are used. A hosted model receives the facts, so decide that before switching it on.

## 8. Audit

The service writes one audit row per generation (`AiGenerated`, the task name, the model, never the facts or the text). The caller audits the generation and the export with the actor and the scope.

## 9. Example

```csharp
var facts = new AiReportFacts(
    Title: "Supplier offers",
    Scope: "All suppliers, 1 Oct 2026 to 31 Oct 2026",
    RecordCount: 1350,
    KeyFigures: [new("Offers submitted", "1350"), new("Compliant offers", "1100"), new("Excluded offers", "250")],
    FactsText: "Offers by status: Compliant 1100, Non-Compliant 250.\nTop suppliers by offers: Supplier 3 Co. 40, Supplier 1 Co. 35.\n...",
    Limits: ["Only the 40 newest offers are listed; the totals cover all 1350."]);

var result = await _report.AnalyzeAsync(facts, ct);
if (!result.Ok) return Problem(result.Message);          // or render the figures without AI text
var n = result.Narrative!;                               // n.Summary, n.Findings, n.NeedsAttention, n.Details, n.CoverageNotes
```

## 10. Who does what

| Marwa's side | Wadea's side (this service) |
|---|---|
| Admin-only access and antiforgery on the endpoint | Writing and checking the narrative |
| Checks the selected scope against the real, authorized query | Number, link, placeholder and decision checks |
| Loads the full-scope facts, computes the figures, keeps currencies apart | Time limit, retry, concurrency limit |
| Sets `Limits` honestly | Audit row without content |
| Stores the successful result as a short-lived snapshot owned by the Admin and exports the same snapshot as PDF | The model and its settings |
| Renders the success state and updates the JavaScript timeout | |
