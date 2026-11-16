# Evaluation Ready and recommendation voting

Owner: Marwa. This change edits Evaluation, its entities/DbSet, its feature CSS/JS and its existing demo-seeder partials only. No Officer, Manager, Top Committee, shared layout, configuration, migration or Audit service file was edited.

## Database prerequisite — Wadea

Do not run the updated Evaluation pages against the old database schema. Create and apply a migration for these two owned model changes before restarting the application:

1. New `EvaluationCommitteeSubmissions` table (`Data/Entities/Evaluation/EvaluationCommitteeSubmission.cs`): Id, TenderId, EvaluatorUserId, ReadyAt, VotingRound (starts at 1 in the application), nullable RecommendedBidId and nullable VotedAt. Unique index on TenderId + EvaluatorUserId. Foreign keys to Tenders, AspNetUsers and Bids use the existing context's Restrict delete policy.
2. Nullable `CompletedAt` datetime2 on `FinancialEvaluations`. Complete Save changes sets it; Save Draft clears it. This distinguishes a complete saved evaluation from a draft with filled-in scores.

Only Wadea creates/commits migrations. No migration or change to the project's current database was made by Marwa's agent. Isolated SQL tests used EnsureCreated for disposable test databases only.

Existing draft/complete-but-unconfirmed evaluations must be reviewed and saved with Save changes before the new Ready flow. Existing reports, recommendations, Ready confirmations and audit records are not reset or overwritten. In particular, old reports already sent with a NULL RecommendedBidId are not silently assigned a winner: this implementation applies to newly finalized reports. Reopening an old sent report requires a separate agreed workflow.

## The new flow

- Save changes for every eligible offer, then confirm one Ready on Evaluation → All offers. Scores/notes for all that member's offers are locked server-side. Save Draft alone cannot enable Ready.
- Four members Ready enables each member's recommendation selector. The page checks for stage changes every ten seconds while visible; it avoids refreshing while a recommendation is selected.
- Each member confirms one immutable choice per voting round. No default offer is selected. All four choices are required, even if one offer is already mathematically ahead.
- The unique vote leader wins, including 2–1–1. Only tied vote leaders are compared by committee technical average + financial points, rounded to two decimals, as displayed to members.
- If the highest vote counts AND highest final totals remain tied, no report is sent. All four choices reset into a new round; scores and Ready timestamps stay locked. Stale round submissions are rejected. Prior votes and the tie are retained in audit events.
- The final successful choice writes exactly one EvaluationReport with non-NULL RecommendedBidId and voting justification, then notifies the tender's Officer using `/Sourcing/Evaluation?tenderId={id}`. It does not submit to the Manager, create an approval, or set RequiresTopCommittee.
- Saves, Ready and voting take the same UPDLOCK/HOLDLOCK on the tender in a serializable transaction. Report + notification + audit are committed together.
- Existing per-offer FinancialEvaluations.ReadyAt values are set together by the single Ready so Officer tracking and other report readers remain compatible.

## Important integration follow-up — Nada

The existing `Areas/Sourcing/Controllers/CommitteeBriefController.cs`, `SaveEvaluation` action can currently write TechnicalEvaluations without checking Ready. It was inspected read-only and left untouched.

Before claiming that evaluations are locked through EVERY site endpoint, Nada must protect that action too, or retire its write path in favor of Marwa's protected Assessments Save action. A targeted fix should:

1. Acquire the same tender UPDLOCK/HOLDLOCK before reading/changing scores, inside its existing serializable transaction.
2. Reject a write if the current assigned member has an EvaluationCommitteeSubmission for that tender, if the relevant FinancialEvaluation.ReadyAt is set, or if the tender report has already been submitted.
3. Keep the existing identity, assigned-member, eligibility and validation checks. Do not accept evaluator identity from the client.
4. If this separate technical-only save remains enabled before Ready, the member still needs complete Save changes from Evaluation to validate/save the current combined technical and financial form; CompletedAt must not falsely mark stale financial calculations as complete.

Marwa's own Save and Save Draft already reject edits after Ready. No changes to Nada's files are included here.

## Manager / Top Committee ownership

New reports now provide RecommendedBidId, the data missing from the Manager approval flow. The separate `Tender.RequiresTopCommittee` flag and account/committee assignment policies remain with their existing owners; this change does not set that flag or promise that every downstream gate is fixed. Officer forwarding remains Deyaa's action.

## Development fixtures

Only creation of NEW completed demo/preview fixtures was adjusted to include complete saves, four tender confirmations and explicit synthetic unanimous votes. The editable evaluation fixture still starts empty. Existing fixtures are not reset on startup. Existing Audit service code/history is preserved.

## Verified

- Project build: zero warnings and errors.
- 72 checks using the real EF SQL Server model and MVC actions on disposable databases: draft versus complete save, membership/eligible-offer guards, single Ready, all-offer lock, no send on final Ready, all four votes required, 2–1–1, 2–2, 1–1–1–1, score tie resolution, unresolved tie/new round, stale round, duplicate clicks, concurrent final choices, exactly one report/Officer notification and no Manager/Top Committee action.
- New completed demo report has a recommended bid; repeated seeding preserves existing audit rows and does not duplicate fixtures.
- Real compiled Razor Offers/Recommendation UI reviewed on desktop and mobile using isolated SQL data through a temporary read-only rendering host. This does not replace testing the normal authenticated browser flow after Wadea applies the migration.
- Existing project database, historical reports and user accounts were not changed by these tests.

After migration and Nada's save guard, test through the ordinary role accounts: save every offer → one Ready per member → four recommendations → Officer tracking → Officer forwards → Manager reviews the populated recommendation. Test Top Committee routing only with an appropriately configured tender.
