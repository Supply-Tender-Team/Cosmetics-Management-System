# RPT-01: reports and exports handoff

Owner: Marwa. Open /Shared/Reports after rebuilding/restarting the app. The existing common Reports sidebar link already points here. Manager/Reports remains Dana's decision queue; Evaluation/Report remains the committee's individual tender report.

## Delivered

- Role-based report choices, based only on signed-in claims. Multiple roles combine their available choices; each query still enforces ownership.
- Admin: user/role directory, suppliers, departments, daily audit activity, daily sign-in attempt counts. No offer prices or scores are granted by the Admin role.
- Requester: own requests, including private own drafts, and published linked tender status.
- Officer: received non-Draft submitted requests, published tender register, submitted non-Draft offers/compliance, evaluation progress.
- Evaluation Committee: assigned published tenders with Ready counts and report status.
- Manager: assigned forwarded reports and own review decisions/duration.
- Top Committee: own decisions/votes and awarded tenders; link to Dana's statistics page.
- Supplier: own offers (including own drafts) and invitations only; link to Sara's offers page.
- Context-appropriate filters, sorting, summary cards, pagination (25), and friendly empty results.
- Data is queried from SQL through ApplicationDbContext. The seed supplies normal database records; there is no hard-coded report dataset or dependency on Audit as the source of purchasing records.
- The same query and ReportTable formatter serve the page, Excel and print view. Exports include all filtered rows, not only the visible page; maximum 5,000. Unauthorized report types are rejected server-side.
- Monetary totals are grouped by currency. Request budgets have no currency field in the schema, so the UI labels this explicitly rather than assuming JOD or converting amounts.
- XLSX download uses the already-installed DocumentFormat.OpenXml. Text formula prefixes are neutralized, numeric amounts remain numeric, headers are bold, and generated-by/time plus filters appear in the workbook.
- Every Excel export and print preparation calls AuditService with report type, format and row count only. AuditService itself was not edited. Print preparation is logged, not a claim that the browser saved a PDF successfully.
- Scoped CSS matches TEAMLY colors/font tokens; no shared CSS/layout/sidebar was modified.

## PDF status: important distinction

Print / Save as PDF opens a dedicated print-friendly report with the same filters and generated-by/time. The user chooses Save as PDF in the browser print dialog. This is NOT a server-generated PDF download. The current endpoint intentionally refuses format=pdf instead of falsely returning HTML as a PDF.

Project inspection found Aspose.PDF 25.11.0 used by DocumentWatermarkService, but no SetLicense call or confirmed report-export license. Wadea's supplied RPT-01 proposal explicitly says not to use Aspose for reports until licensing is confirmed. No new package, license setting, csproj change or shared service change was introduced. Direct PDF download can be added after Wadea confirms the approved library/license.

## Verification: 16 November 2026

- dotnet build: 0 warnings, 0 errors (build in a separate output folder so the running user app is unaffected).
- 85 SQL-backed checks: all report types for all seven roles, text/date filters, Requester/Supplier isolation, private staff drafts, assigned committee scope, unauthorized type tampering including malformed filters, nullable/empty results, currency separation, real decision/vote/award fixtures, correct approval levels, decision durations, 5,000-row export cap, stable pagination, XLSX schema/formula safety and audit events.
- Temporary security/large-row/decision fixtures and their audit entries were enclosed in a transaction and rolled back. No actual existing requests, decisions or audit records were deleted.
- Browser checks used a temporary localhost rendering host with the real compiled Reports views, real scoped SQL queries and isolated test identity contexts. Checked Requester filtering, Supplier choices/results, actual XLSX download, print-view metadata, empty state/export disabled, and 390px layout with no page overflow. These checks do not claim to replace testing your normal login/onboarding flow in the main running app.
- The host's test export/print audit writes were rolled back. No test-only routes were added to Program.cs or the application.

## Files

Only Areas/Shared/Controllers/ReportsController.cs, Areas/Shared/Models/Reports, Areas/Shared/Views/Reports, Services/Reports, wwwroot/css/reports.css and wwwroot/js/reports.js were created/edited. ReportsModule registers our plain services through the existing IFeatureModule discovery. No migration, NuGet, entity, configuration, seed or other developer file was changed in this task.

## Remaining owner coordination

1. Wadea: confirm an approved PDF library/license if direct server PDF download is required.
2. Deyaa: as requested by Marwa/Wadea, remove ONLY the AI analysis navigation item from Views/Shared/Sidebars/_SidebarEvaluation.cshtml. Keep the AI page/service for Wadea; no AI feature code needs deletion. This shared sidebar was left unchanged to respect ownership in AI_INSTRUCTIONS.md, sections 4–5.

No commit, push, pull, merge, branch deletion or change to the active branch was performed.

## Inline record details update

- View details expands a row in place. No separate detail-page/export workflow or new record-ID endpoint was added.
- Field sets are selected by report type and loaded only for IDs already returned by the authorized SQL query. Requester and supplier child lookups repeat ownership restrictions. Supplier details exclude internal compliance notes; Admin audit details remain aggregate counts.
- All exported matching rows include these same concise fields in a wrapped Additional details XLSX column, whether the UI row is open or closed. Print marks IsPrint and renders every detail row visibly; it omits toggle buttons. Notes over 250 characters are explicitly marked excerpts, and item summaries list the first three items with the remaining count when applicable.
- Departments have no stored creation timestamp, so their report no longer invents a Created column/date filter. Their details show actual account totals, active/inactive counts and role counts.
- Export route generation was corrected to keep format=xlsx inside the same route-value dictionary. Excel is also the default for older links missing format. The actual authenticated 7180 link and the original screenshot URL were tested successfully, not only the temporary render host.
- Verification: 85 regression checks, 28 detail checks, and 32 additional exact-ownership checks passed. Ownership checks compared the full record-ID sets against independent SQL queries for every existing development account. An actual browser XLSX was opened and checked for the expected authorized detail values. Test data remained transactional/rolled back.
- Built and restarted the normal app on 7180. Browser testing on the real app confirmed an unauthorized Officer cannot open the Admin department report, can open received-request details, can download detailed Excel, and sees all detail fields automatically in the print view. Existing sidebar/layout files were unchanged.
