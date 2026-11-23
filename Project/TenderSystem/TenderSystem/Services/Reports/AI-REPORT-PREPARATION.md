# Admin AI Reports — connected adapter (AI-16)

The preparation is now connected to Wadea's existing `AiReportService.AnalyzeAsync`. The caller does not change Services/Ai or Program.cs. NVIDIA/live provider operation uses local AI_INSIGHTS_* or shared fallback settings. The supplied shared NVIDIA settings were subsequently configured in the ignored local .env; no credentials are stored in tracked files.

## Current flow

- Admin-only, antiforgery-protected GenerateAiReport loads and revalidates the selected report scope from ReportQueryService.ReadAiFactsAsync.
- Quick summary starts from the selected report's full query, ignoring ordinary Results filters. Specific topic resolves a live permitted supplier/person/department/tender/status or a supported comparison/date scope.
- Full-scope database counts, status groups, department/supplier counts and separate currency totals are computed before loading at most 40 supporting records. There is no visible-page or 5,000-row export cap on the aggregate scope.
- FactsText is bounded to 12,000 characters. Any sampled groups/rows and shortened supporting details are stated in Limits. Raw attachments/audit payloads are not analyzed, and undefined timing/performance/verification metrics are not invented.
- One AnalyzeAsync call produces the narrative. Title/scope/record count/key figures/viewer/time remain caller-owned. CoverageNotes are retained from the service and displayed on screen and in PDF.
- Success renders the Razor-encoded _AiGeneratedReport. Summary/figures/coverage are visible first; native Expand reveals findings, review points and details of the same result with no further service call.

## Snapshot and PDF

AiReportSnapshotStore uses existing IMemoryCache: one JSON-frozen snapshot per signed-in Admin, 20-minute absolute lifetime. A successful new generation replaces the previous token. App restart also loses these temporary snapshots; no new database table was added.

ExportGeneratedAiReport checks Admin role, signed-in owner and opaque token, then sends that SAME snapshot to AiReportPdfWriter. It never calls the provider or accepts a client report body. The existing narrative PDF design, MS. WAND header/footer/watermark mark and all expanded details are retained. Coverage is printed too. No Sara watermark code was edited.

The browser downloads PDF without navigating away. Missing/expired/replaced snapshots produce a message in the Reports page. Changing AI scope disables export and marks old output stale. Generation failure does not invent AI text. Disabled/no-data responses display verified figures and coverage, with PDF still disabled.

## Reliability

reports-ai.js waits up to 200 seconds, shows an honest progress message and provides Cancel analysis. MVC CancellationToken reaches the facts query and AnalyzeAsync. Scope changes/page closure abort the current browser request; stale results are not applied. Statuses no-data, disabled, invalid-request, too-large, timeout, unavailable and rejected are handled. Normal Reports and exports are independent.

The shared service retains its own AiGenerated audit. The caller records AiReportGenerated/AiReportExported with actor, safe scope identifiers and counts, never facts/narrative/API credentials.

## Files

- Services/Reports/ReportQueryService.Ai.cs: verified scope and bounded facts adapter.
- Services/Reports/AiReportSnapshotStore.cs: temporary owner-bound result.
- Areas/Shared/Controllers/ReportsController.cs: GenerateAiReport and ExportGeneratedAiReport.
- Areas/Shared/Models/Reports/AiReportViewModel.cs: added CoverageNotes.
- Areas/Shared/Views/Reports/_AiGeneratedReport.cshtml and _AiReport.cshtml: connected presentation.
- wwwroot/js/reports-ai.js and wwwroot/css/reports-ai.css: generation, scope invalidation, error/disabled states, cancel, snapshot download and result panels.
- Services/Reports/AiReportPdfWriter.cs: prints CoverageNotes in the same snapshot PDF.
- Services/Reports/ReportsModule.cs: registers snapshot store using existing memory cache.

## Verification and limits

All Admin report types passed read-only scope/facts checks against the project database. Separate disposable SQL integration checks exercised actual service validation with an offline fake provider: success, single provider call, disabled/no-data, timeout/unavailable/rejection, forged scope, non-Admin denial, cancellation, immutable/foreign/replaced/missing snapshots, same-result PDF including coverage, >5,000-record scope, mixed currencies and actor audit. UI checks used external test fixtures: Expand and export do not generate again, scope changes invalidate export, expired download stays in-page, cancellation/retry works, no JS errors or mobile overflow. A subsequent live NVIDIA test used one synthetic tender already in the database after local configuration. It timed out at the 90-second attempt limit; a successful live narrative has not yet been verified. Existing project data was not reset or reseeded.

The Evaluation offer-assistant remains preparation only. AI-16 expressly does not choose suppliers, so it is not connected to that advisory-offer card.
