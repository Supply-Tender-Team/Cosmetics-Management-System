# Simple Admin Reports — 18 November 2026

Choose a Category, choose a Report, then Open report. Pick from the database dropdowns and optionally choose From date / To date. View results applies every selection, including dates. Leaving the choices at All shows the general report.

## Only relevant filters

- Supplier offers: supplier, tender, offer status and submission dates.
- Purchase requests: requester, department, request status and creation dates.
- Tenders: related supplier, department, tender, status and creation dates.
- Committee evaluations: member, supplier, tender, status and save dates.
- Decisions: person who recorded the decision, tender, outcome and decision dates.
- Supplier participation: supplier, approval status and activity dates.

The report hides a status selector when there is only one possible status. Names, departments, suppliers, tenders and statuses are queried from the database, not a static list. Changing a dropdown refreshes related choices in the background without navigating or updating results. Export links remain disabled until View results applies the selections. For example, selecting a supplier limits the tender choices to that supplier's records. New database records appear on the next report read. A period with no records is still a valid empty result.

Country, supplier category, amount ranges, currency selection, sort selection, configurable grouping and More filters were removed from the new Admin interface. Other roles retain their existing Reports interface and ownership rules.

## Direct report choices

- Purchase requests.
- All tenders; published tenders with no current submitted offers.
- Supplier tender counts; Supplier documents; Supplier invitations.
- Supplier offers; excluded offers and reasons; committee assignments; saved evaluations; committee reports and recommendations.
- Manager decisions; Top Committee decisions; Top Committee voting participation; awarded tenders.

This avoids asking the user to configure a special filter to obtain common reports such as excluded offers or tenders with no offers. A small automatic status count replaces the optional grouping selector and the large Admin summary cards.

## Dates and database accuracy

Offer dates are SubmittedAt, not the supplier's registration date. Supplier-participation dates apply to Invitation.SentAt, Bid.SubmittedAt and recorded final approval dates. Supplier registration does not exclude an older company from an activity-period report. With no dates, the participation report gives all-time counts. Counts use distinct tender IDs. Draft and withdrawn offers are separate from current submitted participation.

Select the same From and To date for a single day. End dates are inclusive, with UTC dates as in the existing report workflow. Missing dates and scores are not invented.

Excel, PDF and Print use the same filtered query, details and selected names. PDF now uses the already-installed PdfPig writer and includes every exported record and its details, without the old four-page truncation. Every PDF page carries MS.WAND branding. Sara's watermark service is unchanged. Existing row export limits remain enforced. Top Committee participation does not expose private vote choices or comments.

## Simple code walkthrough

1. ReportQueryService.AdminOptions.cs defines the available reports and relevant named selectors.
2. ReportQueryService.AdminQueries.cs reads the real tables and relationships with a switch and LINQ queries.
3. ReportQueryService.Filters.cs applies the selected IDs, statuses and dates; it also queries dependent dropdown choices.
4. _AdminFilters.cshtml shows those dropdowns and the two date fields. reports.js reloads related choices after selection.
5. ReportQueryService.AdminBusinessDetails.cs loads related details. The existing export writers receive the same page model.

## Verification

160 SQL/controller and export checks passed, including all report types, existing-role isolation, dependent tender choices, fresh choices after new database records, exact single-day filtering, period-specific supplier counts, excluded-offer reasons, separate approval stages, Excel validation and export limits. Temporary records and test audit writes were rolled back.

Browser checks used real compiled views and database queries in a temporary localhost test host. Selecting LAB Packaging 1 narrowed the tender list to its four related tenders and status choices to its recorded statuses. Choosing LAB-01-EVL then showed its one matching offer. Only Supplier, Tender and the two date fields remained because there was only one matching status.

No seed, entity, migration, authentication, shared layout or other developer page was changed. Product changes remain within Marwa's Reports files. No commit, push or merge was performed.

## Clear labels and complete export

Supplier reports > Supplier tender counts > Supplier > optional dates > View results shows Invited tenders, Tenders with offers and Won tenders separately. Tender tables separate Invited suppliers and Submitted offers. Offer tables separate Supplier and Tender. PDF tests verified all 12 sample records and every detail field across more than four pages.
