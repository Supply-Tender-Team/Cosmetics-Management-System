# Deyaa handoff: identical committee report in Evaluation tracking

Scope: Marwa supplies the report display; Deyaa owns all changes to Areas/Sourcing/Views/Evaluation/Index.cshtml and its forwarding buttons. Do not modify Nada's Offer ranking. Marwa has not edited any Sourcing, Manager, or shared layout file.

## 1. Use the SAME report display as Evaluation Committee

The ready ViewComponent is EvaluationResultsReport. It calls EvaluationReportReader, uses _OfficerReport.cshtml, and renders the exact SAME _ReportContent.cshtml used by Evaluation/Report/Index. It includes evaluation-report.css. This is the source of the committee's design, not a similar reconstructed layout.

Inside the existing Read the full report disclosure/modal, keep its summary and .tracking-report-modal-content wrapper. Replace the current .tracking-report-grid and raw TechnicalSummary/FinancialSummary/Justification blocks with this call:

```cshtml
<details class="tracking-report-modal">
    <summary><i class="bi bi-file-earmark-text" aria-hidden="true"></i> Read the full report</summary>
    <div class="tracking-report-modal-content">
        @await Component.InvokeAsync("EvaluationResultsReport", new { tenderId = Model.SelectedTender!.Id })
    </div>
</details>
```

Place this inside the existing SelectedTender != null and LatestReport != null branches. Pass the tender ID, NOT LatestReport.Id. Replace the entire old modal content rather than nesting our component inside tracking-report-grid: the grid would shrink the report to a column. Keep any Officer workflow/decision history outside the shared report body. Do not render the old four text boxes alongside our component.

The current modal JS copies .tracking-report-modal-content.innerHTML into a Bootstrap modal. It can continue to use our server-rendered content. Preserve .evaluation-report and its report-* classes; do not flatten the details or convert it back to plain snapshot text. Retain the component stylesheet and verify Sourcing CSS does not override it. Do not redirect to /Evaluation/Report: the Officer must stay in Sourcing with the Sourcing sidebar.

The shared body contains the same tender overview, eligible/confirmed/scoring cards, supplier comparison/ranking table, optional member scores and grouped comments, excluded offers where relevant, and collapsed saved report copy. Questions associated with repeated comments remain accessible. The committee's outer page header, tender picker, Back to offers link, and sidebar are intentionally not embedded in the Officer modal.

No new service registration, package, migration, iframe, or endpoint is required. MVC discovers the ViewComponent. It verifies the signed-in Officer and tender access itself, and returns no content for unauthorized access or an unsubmitted report.

## 2. Add a SECOND send button in the Unified evaluation report stage card

Keep the existing lower Send full report to Sourcing Manager button. In the existing foreach(var stage in Model.Stages), inside the card whose stage.Name is Unified evaluation report, add another form using the EXACT same action, tender ID, and CanSendReportToManager condition. Do not wrap the card or the page in a form; do not nest forms.

```cshtml
@if (stage.Name == "Unified evaluation report" && Model.CanSendReportToManager)
{
    <form asp-area="Sourcing" asp-controller="Evaluation"
          asp-action="SendToManager" method="post" class="mt-3">
        <input type="hidden" name="tenderId" value="@Model.SelectedTender!.Id" />
        <button type="submit" class="btn btn-brand">
            <i class="bi bi-send-check me-2" aria-hidden="true"></i>
            Send full report to Sourcing Manager
        </button>
    </form>
}
```

Use the existing POST with antiforgery and its server validation for both locations. Do not create a second send action. Both buttons must disappear/disable after sending, according to the existing state checks. Already-sent reports should show their existing sent status, not another active send button. The screenshot shows an already-submitted test report; do not reset or resend it merely to show the new button. Deyaa owns forwarding implementation and verification; Marwa does not execute it.

## 3. Delivery from Marwa is already implemented

After all four assigned members confirm Ready for EVERY eligible offer, one submitted EvaluationReport is saved and one Officer notification links to /Sourcing/Evaluation?tenderId={id}. Drafts and ordinary saves cannot deliver it. No automatic forwarding to the Manager occurs. The existing tracking controller already reads this saved report through LatestReport; only the requested display/button integration is needed for this new UI request.

## Verify after Deyaa integrates

- Open tracking manually and via the notification; the Sourcing menu stays unchanged.
- Open Read the full report: same sections, numbers, grouped comments and folded details as the committee report, including mobile/modal scrolling.
- Compare the same tender in both views. Do not compare report ID to tender ID.
- Before all Ready confirmations: no completed report and no active send buttons.
- After completion but before forwarding: both top and bottom buttons use the same existing action and conditions.
- After forwarding: neither location allows duplicate sending.
- Access checks still reject unrelated Officers and other roles; no supplier receives the internal report.

Marwa's shared component/data access has been tested; the actual Sourcing modal and two-button UI must be verified by Deyaa after she adds these calls.
