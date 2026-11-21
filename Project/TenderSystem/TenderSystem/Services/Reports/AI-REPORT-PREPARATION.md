# Admin AI report preparation

Status: UI and presentation/export templates prepared. No AI report service connected.

## Current behavior

- Admin only; inserted between regular filters and results. Other roles keep their existing Reports flow.
- Quick summary requests the selected report's full scope, ignoring table filters.
- Specific topic allows one relevant supplier, tender, department or person, with optional dates; a date-only scope is also available where supported.
- Focus also supports a recorded status, supplier comparison and department comparison where relevant. Comparison scopes do not require an individual ID. The future service adapter must implement these scopes and validate selected status/IDs against current authorized database choices; all currently return the same honest not-connected state.
- AiReportChoices reads current database options from the selected report's authorized base query. No typed names, provider calls or database writes.
- GenerateAiReport validates the request and deliberately returns HTTP 503 with a clear not-connected message. It never claims a report was generated.
- PDF export stays disabled until there is a real generated snapshot. No fake output or sample report is shown in the product.
- Regular results, filters, exports and pagination are unchanged.

## Files

- _AiReport.cshtml, reports-ai.css, reports-ai.js: controls, focus selection, loading/validation/unavailable/network states.
- AiReportRequestViewModel: report key, SpecificTopic, Focus, FocusId, From and To.
- AiReportViewModel: plain-text title/scope, server-generated metadata, summary, figures, findings, attention items and expanded details.
- _AiGeneratedReport.cshtml: prepared Razor-encoded narrative result layout with native expand/collapse.
- AiReportPdfWriter: prepared narrative PDF template; title, scope, date/time, prepared-for name, source count, findings and ALL expanded details; every page has MS.WAND watermark/header/footer. No table layout or four-page cap. Uses the already-installed PdfPig, as the existing Reports PDF writer does. Does not modify Sara's watermark service.

## Connection work once Wadea supplies the dedicated service

1. Agree on the service signature and structured response. The existing general AiGenerationService is not called by this preparation.
2. In GenerateAiReport: resolve the selected focus ID against its permitted report query, derive a fresh independent filter (ignore regular filters in Quick summary), and query full scope, not just 25 visible rows. Respect existing export bounds or compute full aggregates with explicitly bounded supporting records; never silently truncate or claim all records were analyzed.
3. Compute counts, rates and money in C#, keep currencies separate, and send only necessary authorized database facts. Do not pass raw HTML, secrets, arbitrary SQL, or unrestricted audit text to the model. Do not invent unresolved KPIs.
4. Validate response structure, lengths and factual numbers. Use server-owned labels, counts and generation metadata. Render _AiGeneratedReport; never render model HTML with Html.Raw.
5. Store the successful report as a short-lived server-side snapshot owned by this Admin. Update the form result through a partial response and bind the export control to an authenticated, Admin-only snapshot export action. Do not accept a client-supplied report body as authoritative. Changing AI scope must invalidate/mark stale the displayed result and disable its export until regeneration.
6. Export the SAME snapshot through AiReportPdfWriter (including all expanded details), not a second model request. Recheck owner/expiry, return application/pdf and audit successful generation/export without sensitive content. No database schema is required for this preparation; choose the final snapshot storage with the lead.
7. Replace the pending badge/note only after generation works. Retain honest unavailable, empty-data and failure states. Normal Reports must work when AI is disabled or fails.

## Verification

Build succeeds. Preparation checks cover unavailable state, invalid scopes/dates, unknown report, binding errors, Admin restriction, antiforgery requirement, supplier rejection, and a narrative PDF beyond four pages with final detail/header/watermark checks.

Read-only visual host verifies Admin-only display, independent live supplier choices, no navigation on generation, honest unavailable feedback, disabled PDF export and mobile containment. Test fixtures are external to the repository and are not production AI results. No database writes or AI requests were made.

## Reports presentation refresh

Screen styles now use a burgundy table header, restrained alternating rows, text-labelled status pills and clearer details buttons. Regular exports remain the same existing links, grouped in a native keyboard-accessible Export disclosure; Escape and outside clicks close it. Filters still require the apply button. No new summary cards or KPI calculations were added. Existing print rules remain separate from screen-only styles.

The same visual table treatment is applied in Marwa's evaluation-report.css to the owned Unified report, without changing its model, ranking or calculations. The existing embedded report inherits those owned styles; no Officer page was edited.

Checks: all seven roles render Reports; AI stays Admin-only; actual status and supplier lists load; comparison scopes require no individual; invalid focus combinations are rejected; details and Export work; filters do not auto-navigate; mobile pages remain contained. Unified report visual fixture retains its 86 and 71.33 scores and functional disclosures.
