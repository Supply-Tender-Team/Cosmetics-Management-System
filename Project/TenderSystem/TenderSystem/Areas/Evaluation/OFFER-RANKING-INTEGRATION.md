# Full evaluation report inside Offer ranking

Prepared by Marwa. All integration code is inside Areas/Evaluation. No Sourcing, shared layout, migration or package changes were made.

## Nada: add this inside the Offer ranking tab

In Areas/Sourcing/Views/Tenders/Details.cshtml, inside the existing section with id="tender-scores", use:

```cshtml
@await Component.InvokeAsync("EvaluationResultsReport", new { tenderId = Model.Id })
```

If the desired UI is a button that expands the complete report, Nada can wrap the same call as follows (Bootstrap is already available):

```cshtml
<button class="btn btn-outline-secondary" type="button"
        data-bs-toggle="collapse" data-bs-target="#full-evaluation-report"
        aria-expanded="false" aria-controls="full-evaluation-report">
    View full evaluation report
</button>
<div class="collapse mt-3" id="full-evaluation-report">
    @await Component.InvokeAsync("EvaluationResultsReport", new { tenderId = Model.Id })
</div>
```

Use either the direct call or the button version, not both. Nada should show the button only after the tender has a saved submitted EvaluationReport; before that, the component intentionally returns empty content. This is a button inside Sourcing, not a link to the Evaluation area.

Model.Id is the tender ID, not the bid ID or report ID. The component name is EvaluationResultsReport (class EvaluationResultsReportViewComponent).

It renders the complete report in the current Sourcing page: supplier comparison, individual member scores, grouped comments, excluded offers, and the saved report copy. It includes its own scoped stylesheet. No iframe, redirect, sidebar, picker, separate layout, or manual database query is needed. Do not invoke the standalone Report/Index view as a partial. MVC discovers the ViewComponent automatically; no Program.cs registration is required.

The existing ranking table can remain during evaluation. To avoid showing two comparison tables after completion, Nada can conditionally replace her existing ranking block once the saved EvaluationReport has SubmittedAt. Marwa has not changed that block.

## Access and readiness

The component reads the current signed-in identity itself. It renders only for a Sourcing Officer authorized for the selected tender by the existing report access rules, and only after a submitted results report exists. It returns empty content before completion, for an invalid ID, an unrelated Officer, another role, or an anonymous request. A supplied tenderId is never sufficient authorization. No test/seed data is required: it uses the persisted tender, evaluations and EvaluationReports.

Both the standalone committee report and this component use EvaluationReportReader and the same _ReportContent.cshtml. There is no duplicated scoring logic. Technical is 80, financial is 20; no new submission or recommendation action is added.

## Notification landing page: ready on Marwa's side

AssessmentsController now generates new report notification links with:

```csharp
Url.Action("Details", "Tenders", new { area = "Sourcing", id = model.TenderId, tab = "ranking" })
```

The intended URL is /Sourcing/Tenders/Details/{tenderId}?tab=ranking. This keeps the Sourcing sidebar. Nada must make tab=ranking activate the scores-tab and tender-scores pane, and deactivate Overview for that query parameter, in addition to adding the component. The current Details view only handles criteria and invitations. No further Evaluation change is required for that agreed URL.

Existing stored notifications keep their old links; changing generation does not rewrite historical database records. The manually generated preview also retains its original notification. No shared sidebar or Sourcing file was edited.

## Verified

- Build: zero errors and warnings.
- Development DB preview: 3 suppliers, all 12 Ready forms; Officer sees all member scores/comments and saved summaries.
- Component access: valid Officer succeeds; invalid tender, unrelated Officer, non-Officer, anonymous requests render no data.
- Compiled embedded Razor renders full content and scoped CSS without a sidebar, tender picker or Evaluation navigation.
- Existing preview seed remains available for review. The actual Sourcing tab is not integrated or browser-tested until Nada adds the call.
