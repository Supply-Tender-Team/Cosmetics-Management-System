# Evaluation Phase 3 integration

Marwa owns the implementation in this area. No Sourcing, Manager, shared layout,
service, migration, or project configuration file was changed.

## AI analysis

`Evaluation/Ai/Index/{tenderId}` reads saved results through Wadea's
`AiBidAnalysisService`. `Analyze` is POST with antiforgery protection and checks
membership and the Sourcing dispatch notification before calling the service.
The existing service analyses non-draft offers, including excluded offers;
their current bid status is displayed. AI suggestions never change scores,
select the recommended offer, or enter the report's official calculations.
Saved AI suggestions are separate from the submitted report snapshot.

## Unified report

`Evaluation/Report/Index/{tenderId}` lists committee results. Any assigned member
may submit a human recommendation with justification after all four members
have complete saved scores for every eligible offer. This checks completion
of saved scores, not separate final-submission events for each member.

The rubric is six locked technical criteria totaling 80, plus one financial
criterion totaling 20. Committee seats and distinct-price ranking follow
Sourcing's current implementation. Incomplete results remain Pending.

`Submit` stores one EvaluationReport with technical and financial snapshots,
the recommended eligible bid, human justification and SubmittedAt. It sends
`EvaluationReportReady` to the tender's CreatedByUserId. The notification links
to this area's report page; a SourcingOfficer may read reports of tenders they
created. The tender does not move to AwaitingManagerReview automatically.

## Work required from the other owners

- The Sourcing owner must implement the Officer's forwarding/review action.
  The current Sourcing/Evaluation controller is a placeholder. Manager's queue
  already requires AwaitingManagerReview, so notification alone does not place
  this report in that queue. The Officer action should audit and notify when
  forwarding the stored report under the agreed workflow.
- Marwa's Assessment Save rejects edits after a report has been submitted.
  The separate Sourcing/CommitteeBrief/SaveEvaluation action does not yet check
  this. Its owner must coordinate that guard/reopening policy; this area does
  not edit their controller. Submitted snapshots remain unchanged if someone
  edits underlying scores through another endpoint.
- No automatic reopening or resubmission after a Manager return is implemented.
  A reopening policy must be agreed before connecting that path.

## Verification

Use disposable development fixtures for dispatch, complete/incomplete scores,
authorization, report submission, Officer notification and duplicate submission.
Do not add fake evaluations to real tenders to make a report ready.
No export packages or migrations are required by this implementation.
