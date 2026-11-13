# Team Tasks: who does what, in which order

7 people, one repository. This plan splits the whole project so that **every person owns separate files** (no conflicts) and everybody can see what comes next.
Rules for HOW to work are in `AI_INSTRUCTIONS.md`. This file says WHAT to work on.

**Task IDs** (like `ADM-02`) are used in branch names and pull requests: branch `feature/ADM-02-users-crud`, PR title `[ADM-02] Users list and create`.
**Size:** S = about half a day, M = 1-2 days, L = 3+ days.
**Status:** tick `[x]` when the PR is merged into `main`.

---

## 1. Read this first: what already exists in `main`

Part of your in-progress tasks is already in the repository. **Do `git pull`, run the app, and look at it before you build anything**, then continue from what is there instead of starting over.

| Already done and merged | Who it affects |
|---|---|
| Initial project, packages, `.env`, `.gitignore`, folder structure per area | Wadea (done) |
| Auth: email + password login, logout, 7 roles, one test account per role, supplier self-registration (no documents yet) | Wadea (done, biometrics come later) |
| Color palette (TEAMLY) and Cairo font as CSS variables in `site.css` | Deyaa: polish, do not rebuild |
| 3 layouts: public (navbar + big footer), auth (login/register, no chrome), dashboard (sidebar + header + small footer), one sidebar per role | Deyaa: polish, do not rebuild |
| Home landing page with real statistics, services, 6 steps | Nada: refine, do not rebuild |
| All 29 entities, DbSets, configurations and migrations (`InitialIdentity`, `AddAllEntities`) | Wadea owns the database foundation and migrations: do not recreate, do not add your own migration |
| Placeholder controller and view for every page of every role | everyone: fill in your own |
| `Infrastructure/Statuses.cs`: constants for every status value | everyone: use them instead of typing strings |

Test accounts: run the app in Development and use the quick sign-in buttons on the login page.

---

## 2. Who owns what (one owner per file, always)

| Person | Main responsibility | Owns these folders and files |
|---|---|---|
| **Wadea** (lead) | Platform, **the whole auth**, **database foundation and migrations**, **email (SMTP)**, biometrics, AI service, merging | `Program.cs`, `*.csproj`, `appsettings*`, `Properties/`, `Infrastructure/`, `Data/ApplicationDbContext.cs`, `Data/Configurations/`, `Migrations/`, `Data/Seed/IdentitySeeder.cs`, `Controllers/AccountController.cs`, `Views/Account/`, `Services/Auth/`, `Services/Email/`, `Services/Biometrics/`, `Services/Ai/`, `wwwroot/js/webauthn.js`, `wwwroot/js/face.js`, `Data/Entities/Biometrics/` (+ `AiBidAnalysis.cs`), `ApplicationUser.cs` |
| **Abdallah** | Admin area, Contact Us | `Areas/Admin/`, `Controllers/HomeController.Contact.cs`, `Views/Home/Contact.cshtml`, `Data/Entities/Identity/` (`Department`, `EmployeeProfile`, `SupplierProfile`, `SupplierDocument`) |
| **Dana** | Requesting Department, Manager, Top Committee, voice search | `Areas/Requester/`, `Areas/Manager/`, `Areas/Committee/`, `Data/Entities/Requests/`, `TenderApproval.cs`, `TopCommitteeVote.cs`, `wwwroot/js/voice-search.js`, `Views/Shared/_VoiceSearch.cshtml` |
| **Nada** | Home pages, Sourcing (requests and tenders) | `Controllers/HomeController.cs`, `Views/Home/` (except Contact), `wwwroot/css/home.css`, `Areas/Sourcing/` controllers `Dashboard`, `Requests`, `Tenders`, `Data/Entities/Tenders/` |
| **Deyaa** | Layout and UI kit, Notifications, Clarifications, Sourcing (offers and compliance) | `Views/Shared/**`, `Views/_ViewStart.cshtml`, `Views/_ViewImports.cshtml`, `wwwroot/css/site.css`, `public.css`, `dashboard.css`, `auth.css`, `wwwroot/js/site.js`, `Areas/Shared/` controllers `Notifications`, `Clarifications`, `Areas/Sourcing/` controllers `Offers`, `Compliance`, `Evaluation`, `Services/Notifications/`, `Services/Clarifications/`, `Data/Entities/Collaboration/` (`Notification`, `Clarification`, `ClarificationMessage`), `ComplianceCheck.cs` |
| **Marwa** | Demo data, audit, reports, Evaluation committee | `Data/Seed/DemoDataSeeder.cs`, `Services/Audit/`, `Areas/Evaluation/`, `Areas/Shared/` controller `Reports`, `Services/Reports/`, `Data/Entities/Evaluation/` (`TechnicalEvaluation`, `FinancialEvaluation`, `EvaluationReport`), `AuditLog.cs` |
| **Sara** | Onboarding screens, Supplier area, file uploads, watermark | `Areas/Shared/` controllers `Onboarding`, `Profile`, `Areas/Supplier/`, `Services/Files/`, `Data/Entities/Bids/` (`Bid`, `BidItem`, `BidDocument`), `DocumentAccessLog.cs` |

Shared files that nobody edits without asking (see `AI_INSTRUCTIONS.md` section 5): `Program.cs`, `.csproj`, `appsettings*`, `ApplicationDbContext.cs`, `Infrastructure/*`. Ask Wadea.
Two people in the same area (Sourcing, Shared) work in **different controllers**, so no file is shared.

> **Please confirm:** "Department Page" was understood as the **Requesting Department area** (Requester: dashboard, create request, my requests). The **Departments list** (create, edit, deactivate a department) is a small page in the Admin area under Abdallah (`ADM-04`). If Dana meant the Departments list, swap these two with Abdallah.

---

## 3. Shared services (the only things people depend on each other for)

Five small services are used by many pages. Their owners publish them **first**, even as a stub that does nothing yet, in a separate small PR in the first days. Everybody else just calls them.

| Service | Owner | Call it like this | Used by |
|---|---|---|---|
| `AuditService` | Marwa (`AUD-01`) | `await _audit.LogAsync("Submitted", "Bid", bid.Id.ToString(), "optional details");` | everyone, for every important action |
| `NotificationService` | Deyaa (`NTF-01`) | `await _notify.NotifyAsync(userId, "BidReceived", "New offer", "Offer received for TND-14", "/Sourcing/Offers/Compare/14");` and `NotifyRoleAsync(role, ...)` | everyone who triggers an alert |
| `FileStorageService` | Sara (`SUP-05`) | `var path = await _files.SaveAsync(formFile, "bids");` and `_files.OpenAsync(path)` | uploads and downloads |
| `EmailService` | Wadea (`EML-01`) | `await _email.SendAsync("user@example.com", "Subject", "<p>HTML body</p>");` | welcome emails, approvals, password reset, important alerts |
| `AiBidAnalysisService` | Wadea (`AI-01`) | `var result = await _ai.AnalyzeAsync(tenderId);` | Evaluation AI page (Marwa) |

Until a service exists, write your code against its method signature above and use a `// TODO` where you call it. Do not write your own copy.
Allowed status values are in `Infrastructure/Statuses.cs`: `Statuses.TenderStatus.Published`, never the raw text.

---

## 4. The phases (the order of work)

| Phase | Goal | Why this order |
|---|---|---|
| **0. Now** | Finish what you started, `git pull`, align with what exists (section 1) | avoids duplicate work |
| **1. Foundations** | Biometrics, onboarding, Admin (users and supplier approval), Requester, shared services, demo data | unlocks everything else |
| **2. Core flow** | Sourcing requests and tenders, invitations, supplier offers, notifications | the main business flow |
| **3. Evaluation and decisions** | Compliance, comparison, evaluation, AI, manager, top committee, clarifications | needs phase 2 data |
| **4. Extras and polish** | Reports and exports, voice search, QR code, watermark, responsive and accessibility pass, demo rehearsal | last, needs everything |

The demo data (`DB-02`) is the key to working in parallel: with it, the person building Sourcing does not have to wait for the Requester pages to exist.

---

## 5. Task list per person

### Wadea (lead)
**Owns the whole auth, the database foundation and migrations, and email.**
- [x] `PLT-01` Initial project and structure (L)
- [x] `PLT-02` Auth base: password login, logout, roles, test accounts, supplier registration (L)
- [ ] `PLT-03` Answer ownership questions, keep `Statuses.cs` correct, publish shared helpers (S) **Phase 0**
- [ ] `DB-01` **Review the entities** against the Schema Diagram (`docs-site`): names, types, required fields, indexes. Fix mismatches, decide whether to add CHECK constraints for status values (S) **Phase 0**
- [ ] `DB-03` **Own the migrations:** create, merge and fix migration conflicts for the whole team. Teammates send entity changes, Wadea creates the migration (continuous)
- [ ] `EML-01` **`EmailService` (SMTP):** send HTML email using the `SMTP_*` keys in `.env`; in Development with no SMTP host it only logs the email so nothing breaks. Templates: welcome (temporary password), supplier approved / rejected, password reset, important notifications (M) **Phase 1** (publish the service first)
- [ ] `AUTH-01` **Password flows:** change password (used by onboarding), forgot password and reset by email link, lockout messages (M) **Phase 1**
- [ ] `AUTH-02` Supplier registration: add legal document upload using Sara's `FileStorageService`, notify the Admin (M) **Phase 2**
- [ ] `BIO-01` **Fingerprint (WebAuthn):** add `Fido2NetLib`, endpoints for register and login (`Passkey/Options`, `Passkey/Verify`), `webauthn.js`, store in `BiometricCredentials` (L) **Phase 1**
- [ ] `BIO-02` **Face:** `FaceService` (embedding, encryption, matching, liveness check), enroll and verify endpoints, `face.js` (L) **Phase 1**
- [ ] `BIO-03` Onboarding gate: after login, users who have not finished onboarding go to the onboarding screens first (S) **Phase 1**
- [ ] `AI-01` `AiBidAnalysisService`: call Ollama, ask for JSON, save into `AiBidAnalyses` (M) **Phase 3**
- [ ] `LEAD-01` Review and merge pull requests, keep `main` building, answer "I need a change in a shared file" requests (continuous)

**Hand-off to Sara (for `ONB-01`):** the endpoints and JavaScript functions of `BIO-01` and `BIO-02` are published as a short note in the PR description. Sara builds the screens against that note.
**Hand-off to everyone who sends email:** call `EmailService.SendAsync` as soon as `EML-01` is merged. Until then leave a `// TODO`.

### Abdallah
- [ ] `ADM-01` Admin dashboard: counts of users, suppliers, departments, requests, tenders (S) **Phase 1**
- [ ] `ADM-02` **Users:** list with search and role filter, create (name, email, department, role, job title, temporary password with `MustChangePassword`), edit, enable/disable, reset password (L) **Phase 1**
- [ ] `ADM-03` **Supplier review:** list by status, details with company data, Approve / Reject / Suspend with a reason. Approving sets `SupplierProfile.Status` and `IsActive`, and notifies the supplier (M) **Phase 1** (this unlocks the whole supplier flow)
- [ ] `ADM-04` Departments list: create, edit, deactivate (S) **Phase 1**
- [ ] `ADM-05` Audit logs page: filter by user, entity, date, includes login attempts (S) **Phase 4** (needs `AUD-01`)
- [ ] `CON-01` **Contact Us** page: company details, form with validation, success message, optional email through the SMTP settings (S) **Phase 1**

### Dana
- [ ] `REQ-01` Requester dashboard: my requests by status and the task list (S) **Phase 1**
- [ ] `REQ-02` **Create purchase request:** title, reason, priority, target date and an items table (name, quantity, unit, specifications), Save draft / Submit (M) **Phase 1**
- [ ] `REQ-03` My requests (filters, search) and Request details with review notes and timeline, edit when Draft or Returned (M) **Phase 1**
- [ ] `VOI-01` **Voice search** for the task list: `voice-search.js` + `_VoiceSearch.cshtml` partial (browser speech recognition), reusable on every employee dashboard (M) **Phase 4**
- [ ] `MGR-01` Manager dashboard and report review page (M) **Phase 3**
- [ ] `MGR-02` Manager decision: Approve / Reject / Return with comments, forward to Top Committee when `RequiresTopCommittee` (M) **Phase 3**
- [ ] `TOP-01` Top Committee dashboard and review package (requisition, bids, report, manager notes) (M) **Phase 3**
- [ ] `TOP-02` Final decision: each member votes, comment saved, award the tender, notify suppliers (L) **Phase 3**
- [ ] `TOP-03` Bid history and statistics report (M) **Phase 4**

### Nada
- [ ] `HOM-01` Home page: refine the landing page that exists (images, wording, real data) (S) **Phase 0**
- [ ] `HOM-02` **About** page and **How It Works** page with the visual 12-step workflow (M) **Phase 1**
- [ ] `SRC-01` Sourcing dashboard (S) **Phase 2**
- [ ] `SRC-02` **Incoming requests:** list, details, Approve / Return (with note) / Reject (M) **Phase 2**
- [ ] `SRC-03` **Tenders:** list, create from an approved request, dates and terms, evaluation criteria with weights that must total 100%, criteria locked when published (L) **Phase 2**
- [ ] `SRC-04` **Select suppliers and invitations:** only Approved suppliers, send invitations, invitation status table (M) **Phase 2**
- [ ] `OPT-01` Optional feature **QR code** for each tender and request details page (S) **Phase 4**

### Deyaa
- [ ] `UI-01` **Layout and palette polish:** responsive check on phone, tablet, desktop (mobile sidebar), consistent look of cards, tables, forms, badges (M) **Phase 0**
- [ ] `UI-02` UI kit as small partials in `Views/Shared/`: page header, status badge, empty state, confirm dialog, toast message (M) **Phase 1** (others will reuse them)
- [ ] `UI-03` Keep the 7 sidebars up to date when new pages appear (S, continuous)
- [ ] `NTF-01` **Notifications:** `NotificationService`, Notifications page, bell with unread count in the header, mark as read (M) **Phase 1** (blocker for others, publish the service first)
- [ ] `SRC-05` Sourcing **Compare offers:** side-by-side price, specs, delivery, documents (M) **Phase 3**
- [ ] `SRC-06` Sourcing **Compliance check:** Compliant / Non-Compliant (reason) / Pending Clarification (M) **Phase 3**
- [ ] `SRC-07` Sourcing **Evaluation tracking:** who finished, report reached the manager (S) **Phase 3**
- [ ] `CLR-01` **Clarifications:** allowed receivers by role and stage, create, inbox (sent / received), thread with replies, status Pending / Answered / Resolved, linked to request, tender or bid, notification on each message (L) **Phase 3**

### Marwa
- [ ] `DB-02` **Demo data seeder** (`Data/Seed/DemoDataSeeder.cs`, Development only, switched by `SEED_DEMO_DATA` in `.env`): sample requests with items, a published tender with criteria, 3 to 4 suppliers, invitations, bids with items, some evaluations (M) **Phase 1** (priority: unblocks Nada, Deyaa, Sara, Dana). If it needs a schema change, ask Wadea
- [ ] `AUD-01` **`AuditService`** and its registration (S) **Phase 1** (blocker for others, publish it first)
- [ ] `EVL-01` Evaluation dashboard (S) **Phase 3**
- [ ] `EVL-02` **Technical evaluation:** score per criterion, observations, non-compliance notes (M) **Phase 3**
- [ ] `EVL-03` **Financial evaluation:** price, taxes, transport, payment terms, total cost (M) **Phase 3**
- [ ] `EVL-04` **AI analysis view** (consumes `AiBidAnalysisService`, labelled "assistant only") (S) **Phase 3**
- [ ] `EVL-05` **Unified report:** scores, comparison, recommendation with justification, send to the manager (L) **Phase 3**
- [ ] `RPT-01` **Reports:** requests, tenders, suppliers, bids, decisions, with filters and PDF / Excel export (L) **Phase 4** (the PDF and Excel packages are approved by Wadea first)

### Sara
- [ ] `ONB-01` **Onboarding screens** (Shared area): change password, profile (`EmployeeProfile`), fingerprint page, face page, finish with consent checkbox. Build the UI now; connect to Wadea's biometric endpoints when published (M) **Phase 1**
- [ ] `SUP-01` Supplier dashboard: invitations waiting, my offers, approval status (S) **Phase 2**
- [ ] `SUP-02` **Invitations:** accept / decline, deadline countdown, expiry (M) **Phase 2**
- [ ] `SUP-03` Tender details, only for invited tenders (S) **Phase 2**
- [ ] `SUP-04` **Submit offer:** items with prices, delivery time, validity, payment terms, technical summary, Save draft / Submit / Withdraw, deadline and invitation checks on the server (L) **Phase 2**
- [ ] `SUP-05` **`FileStorageService` and secure upload:** allowed types and size from `.env`, random file name, stored in `App_Data/uploads`, download endpoint (M) **Phase 2** (publish the service first)
- [ ] `SUP-06` My offers with status and a downloadable personal summary report (M) **Phase 2**
- [ ] `SUP-07` Company profile and renewed document upload (S) **Phase 2**
- [ ] `OPT-02` Optional feature **watermark**: the viewer's name and time on viewed and downloaded documents, logged in `DocumentAccessLogs` (M) **Phase 4**

*Optional features: the brief asks for at least two. We chose QR code and watermark. Digital signature is not planned.*

---

## 6. Where tasks depend on each other

```
BIO-01 / BIO-02 (Wadea)  ──►  ONB-01 (Sara) connects to them
ADM-03 (Abdallah)        ──►  suppliers can sign in ──► SUP-xx (Sara)
EML-01 (Wadea)           ──►  welcome / approval emails in ADM-02, ADM-03 (Abdallah)
REQ-02 (Dana)            ──►  SRC-02 (Nada)            (use demo data until ready)
SRC-03/04 (Nada)         ──►  SUP-02 / SUP-04 (Sara)   (use demo data until ready)
SUP-04 (Sara)            ──►  SRC-05/06 (Deyaa)  ──►  EVL-xx (Marwa)  ──►  MGR/TOP (Dana)
AUD-01, NTF-01, SUP-05, EML-01, AI-01 = shared services, published early by their owners
DB-02 demo data (Marwa)  ──►  lets everyone work without waiting
```

Waiting for someone? Do not stop and do not copy their work. Use the demo data, call the shared service by its signature with a `// TODO`, and tell the owner on Discord.

---

## 7. How we work every day

1. **One task = one branch = one pull request.** Branch `feature/<ID>-<short-name>`. Small PRs, merged within 1-2 days.
2. **Pull first, every morning:** `git checkout main`, `git pull`, then merge `main` into your branch.
3. **Review buddies** (each PR needs one review before Wadea merges):
   - Abdallah reviews Dana, Dana reviews Abdallah
   - Nada reviews Deyaa, Deyaa reviews Nada
   - Marwa reviews Sara, Sara reviews Marwa
   - Wadea reviews everything that touches shared files
4. **Daily 3 lines in Discord:** what I did, what I do today, what blocks me.
5. **Touch only your own files.** Need a change in someone else's file? Ask the owner or Wadea, do not edit it.
6. **Migrations:** only through Wadea (`DB-03`). If you need a new column, change your entity file, push your branch **without a migration**, and tell Wadea. He creates the migration. Never push a migration file yourself.
7. **A task is done when** (Definition of Done): it builds with 0 errors, you ran the page and tested a normal and an invalid input, validation exists on the server, `[Authorize]` is on, colors come from the palette variables, `git status` shows only your files, and the PR is reviewed and merged.
8. **Blocked or unsure?** Say so on Discord the same day. A question is cheaper than a conflict.

---

## 7b. If you already created a migration on your machine

Some of you ran `migrations add` and `database update` locally. **Do not push those migration files.** Do this once:

1. Stop the app. Run `dotnet build`.
2. See what you have: `dotnet ef migrations list`. Main has only `InitialIdentity` and `AddAllEntities`; anything else is yours.
3. Undo yours: `dotnet ef database update AddAllEntities`, then `dotnet ef migrations remove` (repeat once per extra migration). Do not use `--no-build`.
4. Restore the snapshot: `git checkout -- Project/TenderSystem/TenderSystem/Migrations/ApplicationDbContextModelSnapshot.cs`
5. Check `git status`: no file in `Migrations/` may be left modified or untracked.
6. `git pull`.

If your migration contained a change you really need (a new column or table), keep that change in your **entity file**, push the branch without the migration, and tell Wadea.
Quick alternative if the steps above fail: drop your local database (`dotnet ef database drop`), delete your extra migration files, restore the snapshot as in step 4, and start the app. In Development it recreates the database and the test accounts.

---

## 8. The "demo spine" (build these first if time runs short)

Login with all roles (done) → Supplier registration and Admin approval → Request creation → Request review → Tender with criteria and invitations → Supplier offer → Compliance → Evaluation and unified report → Manager decision → Top Committee decision and award → Notifications and audit trail → fingerprint and face login → AI analysis → voice search → two optional features.
