# Team demo seed: current end-to-end test data

Owner: Marwa. Changes are confined to Data/Seed/DemoDataSeeder*.cs. AuditService, Sourcing, Manager, shared views/configuration, migrations and project files were not edited.

## On every developer's computer

1. Pull the merged code from dev after the seed change is reviewed/merged.
2. Use your own existing local .env and SQL connection. Never copy another developer's .env.
3. Enable these settings locally:

```dotenv
SEED_TEST_USERS=true
SEED_DEMO_DATA=true
```

Set SEED_TEST_PASSWORD to your team's development password meeting Identity policy. Existing users keep their existing passwords; the seed does not reset passwords. New demo-only users use this setting. Complete the normal onboarding/biometric setup when requested; no biometric or supplier document files are fabricated.

4. Run the project in Development (from the web project folder):

```powershell
dotnet run --launch-profile https
```

Program.cs already calls IdentitySeeder and then DemoDataSeeder. No manual preview script, DB deletion, replacement connection string or new migration is required for this seed update. The new scenarios are added even when DEMO-PR-001 already exists.

## Which scenario to use

| Tender number | Initial state | Purpose |
| --- | --- | --- |
| DEMO-FLOW-001 | Published, 3 submitted offers, no committee dispatch | Officer reviews compliance, then uses the existing Send to evaluation committee action. Check supplier offers, comparison, compliance and dispatch. |
| DEMO-EVL-001 | Under Evaluation, 3 Compliant offers, 4 dispatch notifications, no saved scores or Ready forms | Four committee accounts can save drafts, evaluate and confirm Ready. The final required confirmation creates the Officer report. |
| DEMO-REPORT-001 | Under Evaluation, 12 synthetic Ready forms, 1 complete report | Officer can inspect the report immediately and test Deyaa's top/bottom send buttons. Clearly marked DEMO; not a real committee decision. |

All three use six technical questions (24,12,8,16,12,8 = 80), a financial criterion of 20, maximum technical score 5, three comparable JOD prices, invitations and bid items. Prices have different ranks so everyone receives a nonzero financial score. The complete fixture is generated using EvaluationReportService's real Ready/report checks, not a manually fabricated report string.

## Accounts for the four forms

| Committee seat | Account |
| --- | --- |
| Department | requester@lumiere.local |
| Financial | financial@demo.local |
| Technical | evaluator@lumiere.local |
| SourcingManager | manager@lumiere.local |

Officer: officer@lumiere.local. Suppliers: supplier@bottlepro.local, supplier2@demo.local, supplier3@demo.local. No account IDs are hard-coded; each machine resolves its own Identity users. Financial is an extra demo account and may require typing its email rather than a quick-login shortcut.

## Manual cycle

Use DEMO-EVL-001. In each assigned account, open Evaluation, choose the tender, and evaluate all three eligible suppliers. Save draft remains editable and does not count as Ready. Each member confirms Ready for each offer: 4 x 3 = 12 forms. At 11/12 there must be no completed report. The last confirmation saves one report and one EvaluationReportReady notification to the Officer. The notification opens Sourcing Evaluation tracking with tenderId. The automatic completion does not forward to the Manager.

On the Officer account open Evaluation tracking, select the same tender, and open Read the full report. Deyaa's page already invokes EvaluationResultsReport, which uses the SAME _ReportContent.cshtml and scoped CSS as the committee report. There are two send buttons (the Unified evaluation report stage card and the lower report section), using the same existing SendToManager action and state conditions. After one succeeds, sending is disabled/hidden at both locations and a second request must not duplicate the notification. No Manager code was changed for these tests.

## Existing data and Audit

- DEMO-PR-001..005 and DEMO-TND-001..002 are retained as legacy fixtures. Their original 60/40 scores/criteria are not current evaluation examples. Do not use them for the new Ready cycle.
- Existing requests, scores, ReadyAt, forwarded statuses, reports, notifications and audit entries are not deleted/reset/relabelled by this update.
- Restarting does not reopen completed forms or resend a completed report. Dates are established at first creation and do not roll forward on every startup.
- Missing new scenarios are added individually. If an orphan request already occupies a new scenario number, the seed reports it rather than deleting user work.
- New scenario creation appends clearly marked DemoScenarioCreated audit events; the standard real report and forwarding actions retain their normal audit logs. AuditService implementation is unchanged.
- To repeat a full completed scenario from the beginning, request a new isolated fixture/version; do not erase the database or wipe audit history.

## Verified on 16 November 2026

- Build: 0 warnings / 0 errors.
- 59 SQL-backed checks on separate disposable databases: fresh seed, an existing legacy marker, production/disabled guards, repeat startup, legacy request/audit preservation, 4 assigned member views, 12 drafts and Ready confirmations, 11/12 gating, exactly one report/Officer notification, tracking data, real SendToManager handler, duplicate send prevention and workflow audit events.
- Tested default report display in a temporary read-only local rendering host using the actual compiled Sourcing Razor view, its modal JS and shared component: both buttons present, full report in modal, member score details and grouped comments usable, no nested forms. This rendering host did not submit buttons on the user's working database; the actual send handler was exercised on the disposable DB.
- User's working DB received the three new fixtures. All pre-existing audit rows compared identical before/after. No existing demo evaluations were completed or forwarded by these tests.
- No commits, pushes, package additions or migrations were performed.
