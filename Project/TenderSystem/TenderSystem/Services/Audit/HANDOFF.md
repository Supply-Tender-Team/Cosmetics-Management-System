# Phase 1: audit and development demo data

## What is connected now

- AuditModule registers AuditService automatically through the existing IFeatureModule discovery.
- Program.cs calls DemoDataSeeder immediately after IdentitySeeder in the existing startup scope.
- AccountController preserves its existing BiometricAuthLogs records and also writes AuditLogs for LoginSucceeded, LoginFailed, LoggedOut and Registered.
- Auth events use LogForUserAsync because the current request principal may not yet represent the account. Unknown accounts and anonymous registration have a null actor. The account's ID is the entity ID; no email or password is logged.
- No other area's write actions currently exist in this checkout: the 36 area controllers are placeholders. They were not changed. Audit calls for future actions must be added when those actions are implemented.

## Every developer: local setup

Keep .env local. Do not rename the tracked .env.example. Copy it to .env on first setup and supply the local connection and development password. On an existing machine, retain all existing values and add:

```dotenv
SEED_TEST_USERS=true
SEED_DEMO_DATA=true
```

Set SEED_TEST_PASSWORD locally to a value meeting Identity's password policy. Run in Development with the existing migrations available. Startup applies existing migrations, creates roles/departments/test accounts, then adds demo data. The tracked template defaults SEED_DEMO_DATA to false; deliberately enable it on each development machine. No demo data is seeded outside Development. No new migration is required.

.env.example1 is a local duplicate created at Marwa's request; it is not the team template and is ignored by the existing .env.* rule. No ignore rule was changed.

## Every controller owner: ready integration example

Add the using and field inside your own existing controller:

```csharp
using TenderSystem.Services.Audit;

private readonly AuditService _audit;
```

Add AuditService audit as an extra parameter to your existing constructor, retaining every existing dependency. Add this assignment in that constructor:

```csharp
_audit = audit;
```

After successful validation, authorization and saving the business action, call:

```csharp
await _audit.LogAsync("Submitted", "Bid", bid.Id.ToString(), "Offer submitted");
```

For status changes, capture the old status BEFORE changing it, then pass safe summaries after the successful save:

```csharp
var oldStatus = bid.Status;
// Apply the authorized status change and save the bid in your action.
await _audit.LogAsync("StatusChanged", "Bid", bid.Id.ToString(), bid.Status, oldStatus);
```

Examples to agree on when each action is implemented:

| Owner | Feature | Suggested action / entity |
| --- | --- | --- |
| Abdallah | Users / departments / supplier review | Created or Updated / ApplicationUser or Department; StatusChanged / SupplierProfile |
| Dana | Request submission / manager decision / votes | Submitted / PurchaseRequisition; Approved, Rejected or Returned / TenderApproval; Voted / TopCommitteeVote |
| Nada | Tender creation / publishing / invitations | Created or Published / Tender; Sent / Invitation |
| Deyaa | Compliance / clarifications | Checked / ComplianceCheck; Created or Answered / Clarification |
| Sara | Invitation response / bid / upload | Accepted or Declined / Invitation; Submitted or Withdrawn / Bid; Uploaded / BidDocument |
| Marwa | Technical / financial evaluation / unified report | Evaluated / TechnicalEvaluation or FinancialEvaluation; Submitted / EvaluationReport |
| Wadea | Future password and biometric flows | Add an explicit safe event only after the actual result is known |

Use the actual saved entity ID, not a guessed value. Log successful business actions after saving; do not log success when validation fails. Explicit auth failures are intentionally recorded as LoginFailed. Opening a page alone is not a successful business change. Existing login events are already connected: do not duplicate their calls.

## Audit API

```csharp
Task LogAsync(string action, string entityName, string entityId,
    string? details = null, string? oldValues = null);

Task LogForUserAsync(string? userId, string action, string entityName,
    string entityId, string? details = null, string? oldValues = null);
```

LogAsync reads the actor from the current authenticated principal. LogForUserAsync accepts a known account ID explicitly, or null for an unknown/anonymous actor; callers must derive that ID on the server, never trust a submitted actor ID. Details maps to AuditLog.NewValues, oldValues to AuditLog.OldValues. Values are supplied by the caller, not inferred automatically. IP comes from the current connection and time is UTC. Limits are action 100, entityName 100 and entityId 50 characters. Never pass passwords, tokens, biometric data, full submitted models or personal data in summaries.

The service uses the scoped ApplicationDbContext and SaveChangesAsync; this saves all pending changes in that context. Save the business action first. Database failures propagate. Business changes and their audit records are not automatically atomic: an owner requiring atomicity must use a transaction spanning both saves. No interception, middleware or automatic logging of future actions was introduced.

## Demo dataset

Five requests (two converted, one submitted, one approved, one draft), two tenders (Published and Under Evaluation), two items per request, criteria totaling 100%, four committee members per tender, three suppliers including the existing BottlePro supplier, six invitations, five bids, three compliance checks, three technical and three financial evaluations. Extra accounts: supplier2@demo.local, supplier3@demo.local and financial@demo.local. They use SEED_TEST_PASSWORD and retain normal onboarding requirements. No documents or biometric records are fabricated.

All statuses/types use Infrastructure/Statuses.cs. Existing records are not overwritten. A transaction covers the new dataset and additional accounts. If DEMO-PR-001 exists, the seeder returns without changes. This is a complete-dataset marker, not a repair process for individual deleted rows. Run startup sequentially on a local database; concurrent multiple-instance seeding is not supported. Dates are relative to first creation and are not refreshed on later runs.

## Verification after merging dev

- Build: zero errors and zero warnings.
- Service tests: invalid inputs, explicit user, anonymous actor and persisted audit fields passed; temporary service records rolled back.
- Seeder tests: production/disabled guards, repeat-run counts, criteria totaling 100 and four committee members passed.
- App startup and GET /Account/Login succeeded with the demo flag enabled.
- Real HTTP login/logout passed for all seven role accounts. Database verification found seven LoginSucceeded, seven LoggedOut and one LoginFailed from these HTTP checks. Those legitimate test-session events remain in the audit history.
- Unknown-login and invalid supplier registration returned validation errors; invalid registration did not create a success audit event.
- Valid supplier registration and its audit passed in a database transaction; the temporary account, company and test audit were rolled back.
- No entity, migration, package, csproj, layout or area controller was edited. Shared-file additions are confined to startup, the environment template and AccountController audit wiring, under the lead approval relayed by Marwa.
