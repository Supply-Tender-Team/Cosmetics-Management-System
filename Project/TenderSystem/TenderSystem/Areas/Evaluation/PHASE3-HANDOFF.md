# Current handoff

See TRACKING-HANDOFF.md. Notifications now open Sourcing Evaluation tracking with tenderId. The last required Ready saves the report and puts the active tender Under Evaluation, never Awaiting Manager Review. Forwarding belongs to Deyaa. Earlier report-page link descriptions below are historical.

# Evaluation Phase 3 integration

Marwa owns the implementation in this area. No Sourcing, Manager, shared layout,
shared service, migration, or project configuration file was changed. A plain
EvaluationReportService and IFeatureModule registration were added inside this area.

## AI analysis

`Evaluation/Ai/Index/{tenderId}` reads saved results through Wadea's
`AiBidAnalysisService`. `Analyze` is POST with antiforgery protection and checks
membership and the Sourcing dispatch notification before calling the service.
The existing service analyses non-draft offers, including excluded offers;
their current bid status is displayed. AI suggestions never change scores,
select the recommended offer, or enter the report's official calculations.
Saved AI suggestions are separate from the submitted report snapshot.

## Unified report

`Evaluation/Report/Index/{tenderId}` lists confirmed committee results and Ready
progress. There is no manual report Submit action or Send button. Save Draft
stores partial scores/notes, Save stores a complete editable assessment, and
Ready saves the posted form then sets FinancialEvaluations.ReadyAt after explicit
confirmation. NULL scores remain unanswered; a real zero remains a real score.
Ready requires complete valid scores and a valid financial comparison.

Ready is per member AND offer. With three eligible offers, all twelve forms must
be confirmed. After Ready, this area's GET is read-only and Save/SaveDraft reject
changes. Repeat Ready is idempotent. All saves acquire a parameterized SQL tender
row lock inside a Serializable transaction before reading final state. The final
Ready request creates one report and one Officer notification in that transaction.

The rubric is six locked technical criteria totaling 80, plus one financial
criterion totaling 20. Committee seats and distinct-price ranking follow
Sourcing's current implementation. Incomplete results remain Pending.

The automatic service stores one EvaluationReport with technical and financial
snapshots and SubmittedAt. It includes questions/weights, individual scores and
notes, member confirmations, average scores, cost details, ranking and excluded
offer decisions. RecommendedBidId remains NULL: ranking is not an award decision.
The report note explicitly says no human supplier recommendation was recorded.
It sends
`EvaluationReportReady` to the tender's CreatedByUserId. The notification links
to this area's report page; a SourcingOfficer may read reports of tenders they
created. The tender does not move to AwaitingManagerReview automatically.

## Work required from the other owners

- The Sourcing owner must coordinate the Officer's forwarding/review action.
  Manager's queue requires AwaitingManagerReview; a Ready notification alone
  does not put the report in that queue. A results report has no automatically
  chosen RecommendedBidId or invented human justification. The owners must
  agree who adds the human recommendation before the Manager's approval flow.
- Sourcing/CommitteeBrief/SaveEvaluation still needs ReadyAt/report locking.
  Marwa does not edit that controller. Until its owner adds the guard, the lock
  is enforced in Evaluation forms, not globally across the separate endpoint.
  Submitted snapshots remain unchanged if another endpoint edits source scores.
- Sourcing Offer ranking currently reads saved technical scores. Its owner must
  label provisional results and check FinancialEvaluations.ReadyAt if ranking
  should be final only after Ready. No changes to that page were made here.
- No automatic reopening or resubmission after a Manager return is implemented.
  A reopening policy must be agreed before connecting that path.

## Verification

43 SQL-backed checks passed on a disposable two-offer/four-member fixture,
including drafts, NULL versus zero, clearing scores, invalid inputs, canceled
confirmation, incomplete Ready, nonmember actions, post-Ready edit rejection,
per-offer locking, provisional complete saves, simultaneous final confirmations,
exactly one automatic report/notification, no Manager transition, and Officer
access. Test data was removed. No migration or package was added by Marwa.
This implementation uses Wadea's AddEvaluationReadyAndNullableScores migration.
