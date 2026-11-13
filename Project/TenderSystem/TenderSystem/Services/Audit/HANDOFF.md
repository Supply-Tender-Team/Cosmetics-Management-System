# Marwa: Phase 1 handoff

## AUD-01

AuditService and AuditModule are in Services/Audit. Program.cs already discovers AuditModule automatically; no startup edit is needed.

Inject AuditService into the controller constructor, then call it after saving the business action:

```csharp
using TenderSystem.Services.Audit;

// Constructor parameter: AuditService audit
// Assign it to a private readonly AuditService _audit field.
await _audit.LogAsync("Submitted", "Bid", bid.Id.ToString(), "Offer submitted");
```

Arguments: action, entityName, entityId, optional details, optional oldValues. Details maps to AuditLog.NewValues; oldValues maps to AuditLog.OldValues. Supply only safe summaries, never passwords, tokens, biometric information or personal data. The current user and remote IP come from HttpContext; background/system calls have a null user and an empty IP. Time is UTC. Field limits: action 100, entityName 100, entityId 50. Invalid arguments and database errors propagate to the caller. This service uses the scoped ApplicationDbContext and SaveChangesAsync, so save the business action before calling it; it also saves any pending changes in that context. It does not automatically intercept actions or record logins: each owner adds calls in their own files.

## DB-02

DemoDataSeeder is implemented in Data/Seed/DemoDataSeeder.cs. It does not run automatically until the lead adds the following call immediately after IdentitySeeder.SeedAsync inside the existing startup scope in Program.cs:

```csharp
await DemoDataSeeder.SeedAsync(scope.ServiceProvider, app.Environment.IsDevelopment());
```

Program.cs was not edited. This connection requires Wadea, the owner of that shared file.

Local environment prerequisites:

- Development environment and a reachable SQL Server database with the existing migrations applied.
- IdentitySeeder test accounts present (SEED_TEST_USERS=true).
- SEED_DEMO_DATA=true (defaults to false; all other environments always return).
- SEED_TEST_PASSWORD set locally, satisfying the existing Identity password rules. No password is hard-coded in this seeder.

Wadea should document SEED_DEMO_DATA=false in the team's .env.example; the existing local deletion of .env.example was left untouched.

The seeder adds five requests (converted, submitted, approved and draft examples), two tenders (Published and Under Evaluation), criteria totaling 100%, four committee members per tender, three suppliers including the existing BottlePro test supplier, invitations, five bids with items, three compliance checks and technical/financial evaluations. Extra accounts: supplier2@demo.local, supplier3@demo.local and financial@demo.local. They use SEED_TEST_PASSWORD and retain the normal onboarding requirements. No documents or fabricated biometric records are created.

Existing records are not reset or overwritten. A transaction covers the complete dataset and new Identity accounts. A second call returns when DEMO-PR-001 already exists; the seeder is intended for sequential local startup, not simultaneous multiple application instances. Dates are relative to the first successful run and are not refreshed later. The exact documented status strings are used because Infrastructure/Statuses.cs is absent in this checkout.

## Validation

- dotnet build: succeeded with zero errors and zero warnings.
- Temporary verification harness outside the repository: production guard, disabled switch, empty action and overlong entity ID checks passed.
- After the local connection was updated, SQL Server checks passed: existing IdentitySeeder initialized development accounts; authenticated and system audit records persisted correctly and the test transaction was rolled back; demo data was inserted successfully; running the seeder again did not add duplicate requests, tenders, bids or users; both tenders have criteria totaling 100% and four committee members. Demo data remains in the local development database.
- No shared file, entity, migration, package or project file was changed.
