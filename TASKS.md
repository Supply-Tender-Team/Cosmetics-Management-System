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
| **Abdallah** | Admin area, Contact Us, voice search | `Areas/Admin/`, `Controllers/HomeController.Contact.cs`, `Views/Home/Contact.cshtml`, `Data/Entities/Identity/` (`Department`, `EmployeeProfile`, `SupplierProfile`, `SupplierDocument`), `wwwroot/js/voice-search.js`, `Views/Shared/_VoiceSearch.cshtml` |
| **Dana** | Requesting Department, Manager, Top Committee | `Areas/Requester/`, `Areas/Manager/`, `Areas/Committee/`, `Data/Entities/Requests/`, `TenderApproval.cs`, `TopCommitteeVote.cs` |
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
| `AiBidAnalysisService` | Wadea (`AI-01`, ready) | `var run = await _ai.AnalyzeAsync(tenderId, userId, ct);` and `var items = await _ai.GetLatestAsync(tenderId, userId, ct);` (needs the signed-in user id: only members of the tender's evaluation committee may use it) | Evaluation AI page (Marwa) |

Until a service exists, write your code against its method signature above and use a `// TODO` where you call it. Do not write your own copy.
Allowed status values are in `Infrastructure/Statuses.cs`: `Statuses.TenderStatus.Published`, never the raw text.

### Service connection map (who calls which service)

Checked on the code of `dev` after PR #10. "Connected" means the call exists and was tested. Each owner connects the services inside THEIR OWN controllers (nobody edits another person's file).

| Where | Needs | State |
|---|---|---|
| Login, logout, register (`AccountController`) | Audit | connected (every method: password, face, fingerprint) |
| Change / forgot / reset password (`PasswordService`) | Email + Audit | connected (`PasswordChanged`, `PasswordResetRequested`, `PasswordReset`) |
| Add / remove fingerprint, enroll face | Audit | connected (`PasskeyAdded`, `PasskeyRemoved`, `FaceEnrolled`) |
| Admin approves / rejects / suspends a supplier (`Admin/SuppliersController`, Abdallah) | Email (templates `SupplierApproved`, `SupplierRejected`, `SupplierSuspended` are ready) + Audit (`StatusChanged`, `SupplierProfile`) + Notification | to do (Abdallah) |
| Admin creates an employee (`Admin/UsersController.Create`, Abdallah) | `PasswordService.CreateEmployeeAsync` (temporary password, Welcome email, audit, `MustChangePassword`) | service ready (`AUTH-03`); Abdallah only has to call it, see the hand-off below |
| Supplier registration (`AccountController.RegisterSupplier`) | Notification to the Admin + document upload (`FileStorageService`) | connected (`INT-02`, `AUTH-02`) |
| First sign-in with a temporary password (`MustChangePassword`) | `PasswordService` + onboarding gate | connected (`BIO-03`): the gate sends the user to onboarding until it is completed; the change-password step uses `PasswordService` |
| Evaluation AI page (Marwa) | `AiBidAnalysisService` | service ready (`AI-01`), the page still has to call it: see the hand-off to Marwa |
| Anything that triggers an alert | `NotificationService`, which can call `EmailService` for the important ones | waiting for `NTF-01` (Deyaa) |


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

## 4b. Tasks by phase

Same tasks as in section 5, grouped by phase. Fill in the target dates together. Tick a task in section 5 when its PR is merged.

### Phase 0: Now

**Target date:** ____ / ____ / ______

**Goal:** Finish what you started, `git pull`, run the app, align with what already exists in `main`.

**Phase is done when:** everybody has pulled, run the app with the test accounts, and created a branch for their first task.

| ID | Who | Task | Size |
|---|---|---|---|
| `DB-01` | Wadea | ~~Review entities against the Schema Diagram~~ (done) | S |
| `PLT-01` | Wadea | ~~Initial project and structure~~ (done) | L |
| `PLT-02` | Wadea | ~~Auth base: password login, roles, test accounts, supplier registration~~ (done) | L |
| `PLT-03` | Wadea | Ownership answers, Statuses.cs, shared helpers | S |
| `HOM-01` | Nada | Home page refinement | S |
| `UI-01` | Deyaa | Layout and palette polish (responsive) | M |

### Phase 1: Foundations

**Target date:** ____ / ____ / ______

**Goal:** The base everything else stands on: fingerprint login and onboarding, users and supplier approval, requests, demo data and the shared services (audit, notifications, email, file storage).

**Phase is done when:** all roles can sign in with password or fingerprint and finish onboarding, the Admin can create users and approve suppliers, a department can create requests, demo data exists, and `AuditService`, `NotificationService`, `EmailService` and `FileStorageService` are merged.

| ID | Who | Task | Size |
|---|---|---|---|
| `AUTH-01` | Wadea | ~~Password flows: change, forgot and reset by email~~ (done) | M |
| `BIO-01` | Wadea | ~~Fingerprint login (WebAuthn)~~ (done) | L |
| `BIO-03` | Wadea | Onboarding gate after login | S |
| `EML-01` | Wadea | ~~EmailService (SMTP) and email templates~~ (done) | M |
| `AUTH-03` | Wadea | ~~Employee creation support: temporary password, welcome email, forced first change~~ (service done) | M |
| `ADM-01` | Abdallah | Admin dashboard | S |
| `ADM-02` | Abdallah | Users: list, create, edit, disable, reset password | L |
| `ADM-03` | Abdallah | Supplier review: approve, reject, suspend | M |
| `ADM-04` | Abdallah | Departments: create, edit, deactivate | S |
| `CON-01` | Abdallah | Contact Us page | S |
| `REQ-01` | Dana | Requester dashboard | S |
| `REQ-02` | Dana | Create purchase request | M |
| `REQ-03` | Dana | My requests and request details | M |
| `HOM-02` | Nada | About and How It Works pages | M |
| `SRC-01` | Nada | Sourcing dashboard | S |
| `NTF-01` | Deyaa | Notifications (service, page, bell) | M |
| `UI-02` | Deyaa | UI kit partials (header, badge, empty state, toast) | M |
| `AUD-01` | Marwa | AuditService | S |
| `DB-02` | Marwa | Demo data seeder (Development only) | M |
| `ONB-01` | Sara | Onboarding screens | M |
| `SUP-01` | Sara | Supplier dashboard | S |
| `SUP-05` | Sara | FileStorageService and secure upload | M |

### Phase 2: Core flow

**Target date:** ____ / ____ / ______

**Goal:** The main business flow end to end (request review, tenders, invitations, supplier offers), plus the pages that can already be built on the demo data: face login, compare offers, compliance, technical and financial evaluation, manager and Top Committee screens, audit page, voice search.

**Phase is done when:** a request can go from Submitted to Approved, become a published tender, invite suppliers, and a supplier can accept and submit an offer with documents; face login works; the pages listed above open and work with the demo data.

| ID | Who | Task | Size |
|---|---|---|---|
| `AUTH-02` | Wadea | ~~Supplier registration: legal document upload~~ (done) | M |
| `BIO-02` | Wadea | ~~Face login (enrollment, matching, liveness)~~ (done) | L |
| `ADM-05` | Abdallah | Audit logs page | S |
| `VOI-01` | Abdallah | Voice search for the task list | M |
| `MGR-01` | Dana | Manager dashboard and report review | M |
| `TOP-01` | Dana | Top Committee dashboard and review package | M |
| `SRC-02` | Nada | Incoming requests review | M |
| `SRC-03` | Nada | Tenders: create, criteria and weights | L |
| `SRC-04` | Nada | Select suppliers and send invitations | M |
| `SRC-05` | Deyaa | Compare offers | M |
| `SRC-06` | Deyaa | Compliance check | M |
| `EVL-01` | Marwa | Evaluation dashboard | S |
| `EVL-02` | Marwa | Technical evaluation | M |
| `EVL-03` | Marwa | Financial evaluation | M |
| `SUP-02` | Sara | Invitations (accept, decline) | M |
| `SUP-03` | Sara | Tender details | S |
| `SUP-04` | Sara | Submit offer | L |
| `SUP-06` | Sara | My offers and summary report | M |
| `SUP-07` | Sara | Company profile and documents | S |

### Phase 3: Evaluation and decisions

**Target date:** ____ / ____ / ______

**Goal:** The parts that need real data from earlier phases: AI analysis, the unified report, final decisions and award, evaluation tracking, clarifications.

**Phase is done when:** a tender can go from closed offers through evaluation (with the AI view) to one unified report, the manager decision and the Top Committee award, with clarifications working.

| ID | Who | Task | Size |
|---|---|---|---|
| `AI-01` | Wadea | AiBidAnalysisService (Ollama) | M |
| `MGR-02` | Dana | Manager decision (approve, reject, return, forward) | M |
| `TOP-02` | Dana | Final decision: votes and award | L |
| `CLR-01` | Deyaa | Clarifications (role-based questions and answers) | L |
| `SRC-07` | Deyaa | Evaluation tracking | S |
| `EVL-04` | Marwa | AI analysis view | S |
| `EVL-05` | Marwa | Unified report | L |

### Phase 4: Extras and polish

**Target date:** ____ / ____ / ______

**Goal:** Reports and exports, bid history and statistics, the two optional features, and a polish pass.

**Phase is done when:** reports export to PDF and Excel, QR code and watermark work, the pages are responsive, and the team has rehearsed the 15-minute demo.

| ID | Who | Task | Size |
|---|---|---|---|
| `TOP-03` | Dana | Bid history and statistics report | M |
| `OPT-01` | Nada | QR code (optional feature) | S |
| `RPT-01` | Marwa | Reports with PDF and Excel export | L |
| `OPT-02` | Sara | Watermark (optional feature) | M |

### Continuous (every phase)

| ID | Who | Task |
|---|---|---|
| `DB-03` | Wadea | Own the migrations for the whole team |
| `LEAD-01` | Wadea | Review and merge pull requests |
| `UI-03` | Deyaa | Keep the 7 sidebars up to date |

---

## 5. Task list per person

### Wadea (lead)
**Owns the whole auth, the database foundation and migrations, and email.**
- [x] `PLT-01` Initial project and structure (L)
- [x] `PLT-02` Auth base: password login, logout, roles, test accounts, supplier registration (L)
- [ ] `PLT-03` Answer ownership questions, keep `Statuses.cs` correct, publish shared helpers (S) **Phase 0**
- [x] `DB-01` **Review the entities** against the Schema Diagram (`docs-site`): names, types, required fields, indexes. Fix mismatches, decide whether to add CHECK constraints for status values (S) **Phase 0**
- [ ] `DB-03` **Own the migrations:** create, merge and fix migration conflicts for the whole team. Teammates send entity changes, Wadea creates the migration (continuous)
- [x] `EML-01` **`EmailService` (SMTP):** send HTML email using the `SMTP_*` keys in `.env`; in Development with no SMTP host it only logs the email so nothing breaks. Templates: welcome (temporary password), supplier approved / rejected, password reset, important notifications (M) **Phase 1** (publish the service first)
- [x] `AUTH-01` **Password flows:** change password (used by onboarding), forgot password and reset by email link, lockout messages (M) **Phase 1**
- [x] `AUTH-02` **Supplier registration documents:** the form asks for the commercial registration and tax certificate (required, optional expiry date) and one optional extra document; type and size come from `UPLOAD_*` in `.env`, files are stored with `FileStorageService` under random names, and the Admin is notified (M) **Phase 2**
- [x] `AUTH-03` **Employee creation support:** `PasswordService.CreateEmployeeAsync` creates the employee with a random temporary password, sets `MustChangePassword`, sends the `Welcome` email and audits it; the first sign-in lands on `/Shared/Onboarding/ChangePassword` (M) **Phase 1**
- [x] `BIO-01` **Fingerprint (WebAuthn):** add `Fido2NetLib`, endpoints for register and login (`Passkey/Options`, `Passkey/Verify`), `webauthn.js`, store in `BiometricCredentials` (L) **Phase 1**
- [x] `BIO-02` **Face:** `FaceService` (embedding, encryption, matching, liveness check), enroll and verify endpoints, `face.js` (L) **Phase 2**
- [x] `BIO-03` Onboarding gate: after login, users who have not finished onboarding go to the onboarding screens first. The security step is a menu: fingerprint (passkey widget) or face (camera widget); the finish step needs AT LEAST ONE of them saved (checked on the server), both are allowed (S) **Phase 1**
- [x] `BIO-04` **One biometric is enough:** the user chooses fingerprint and/or face from a menu (not every device has a fingerprint sensor); at least one must stay saved, so the last sign-in method cannot be removed (S) **Phase 1**
- [x] `AI-01` `AiBidAnalysisService`: reads each submitted offer next to the tender requirements, asks the local model for a fixed JSON answer (price, delivery, missing documents, strengths, weaknesses, recommendation), saves it into `AiBidAnalyses`. Committee members only; one analysis of a tender at a time (M) **Phase 3**
- [x] `AI-02` **Assistant chat:** floating, role-aware chat on every dashboard page (`_AiChat.cshtml`, `AiChatController`, `Services/Ai/`). One `.env` switch picks a local Ollama model, a hosted OpenAI-compatible API, or off. See `Services/Ai/README.md` (M) **Phase 3**
- [x] `AI-03` **Assistant data access (read only):** the chat answers with real data limited to the asker's role (Admin: audit logs, suppliers, users; Requester: own requests; Supplier: own offers and invitations; Evaluation Committee: offers of tenders they sit on; Officer, Manager, Top Committee: their stages). Tables come straight from the database. A hosted model gets no data unless `AI_CHAT_DATA_WITH_API=true` (M) **Phase 3**
- [x] `AI-04` **Assistant command center (Phase 2):** an Admin tells the assistant to e-mail or notify employees (all, one role, one department, or just themselves). The model only proposes; the server picks the recipients; the Admin must approve every action with a fresh fingerprint or face check; it runs once and is audited. Off by default (`AI_ACTIONS=true`). Employees only, one Admin is enough (L) **Phase 4**
- [x] `DB-05` **Evaluation drafts and Ready (schema, from Marwa's request):** `FinancialEvaluations.ReadyAt` (member's final confirmation), `TechnicalEvaluations.Score` and `FinancialEvaluations.Score` now nullable (NULL = not answered, a real 0 stays 0), unique index per member and offer (and criterion). Migration `AddEvaluationReadyAndNullableScores`; the readers that would not compile or would count drafts as done were fixed in the same change (S)
- [x] `AI-05` **Assistant polish:** the round button and the chat window are animated (pop-in, breathing ring, window grows out of the button, messages slide in, thinking pulse; off for users who prefer less motion). The Admin's Command mode is now a separate tab inside the same window (Ask / Command), each tab with its own conversation, and the normal chat only answers questions (S)
- [x] `BIO-05` **One-step face sign-in:** at sign-in the camera asks only "look straight" (about 2 seconds instead of about 9). A small open-source anti-photo model (MiniFASNet, Apache-2.0, in `Services/Biometrics/Models`) checks the face is live; when it is not sure, the sign-in automatically asks for one head turn to a random side, and an address with many "not sure" answers always gets the head turn. Setup (both sides) and AI action approvals are unchanged. `.env`: `FACE_PASSIVE_LIVENESS`, `FACE_PASSIVE_MIN_REAL`, `FACE_LOGIN_TURNS` (S)
- [x] `AI-06` **AI tender summary (step 1, documents not read yet):** one saved summary of a whole tender for the Evaluation Committee (after the member's own forms are Ready), the Sourcing Manager and the Top Committee: overview, per-offer strengths, weaknesses and risk, points to check and a labelled leaning. The server calculates every number from CONFIRMED forms only, the model only writes the words; assistant only, never fills scores or the report. Table `AiTenderSummaries` (migration `AddAiTenderSummaries`), `AiTenderSummaryService`, partial `_AiTenderSummary` (S). Next step: read the attached Word / Excel / text-PDF files
- [x] `AI-07` **Chat can analyze a tender package (Sourcing Manager and Top Committee):** "can you analyze a review package?" now answers with an analysis of the tender waiting for them (offers table calculated by the server, report and manager decision in the facts, documents marked unread), asks which one when several wait, and says plainly when nothing is waiting instead of a vague "no records" (S)
- [x] `AI-08` **Assistant helpers for each role (read only):** the Evaluation Committee can ask the chat to compare and explain the offers of its tender BEFORE scoring (rubric, requested vs offered items, server-calculated observations, own progress only, never another member's scores; "which offer is best" is an AI opinion, never a score; clarification questions to a supplier; confirmed committee results only after the member's own forms are Ready). The Requester gets help wording a request, the Sourcing Officer a compliance checklist of the offers against the tender terms, technical questions with weight options for a tender and a supplier shortlist by category, the Supplier a completeness check of its own offer. Nothing is filled in, saved or sent for the user (M)
- [x] `AI-09` **Excel templates for the command center:** the Admin downloads a template (create employee accounts, or send emails / notifications), fills it and attaches it in the Command tab. The server reads and checks every row with plain code (the AI never sees the file; it only writes the optional common message text), the card previews the valid and skipped rows, the Admin approves the whole file once with a fingerprint or face, and the accounts or messages are created or sent exactly once. Safe reading (.xlsx only, no macros or formulas, size and row limits, never stored, audit without names or text) (M)
- [x] `AI-11` **Smarter assistant chat:** follow-up questions understand what they refer to ("what about its delivery?", "the second one") without giving anyone new access, general work questions are answered and labelled "General knowledge" (procurement concepts, drafting supplier emails, translation), and every answer and table has a copy button plus Export to Excel (S)
- [x] `AI-12` **Live nudges and voice typing:** the cloud above the assistant button now says real things from counts of the user's own work (offers waiting for compliance, a report waiting for the manager, invitations waiting for a supplier...) and a click runs the matching question; nothing waiting means no cloud. A mic button turns speech into text in the chat box (browser speech recognition, or the local Whisper model when `VOICE_MODEL_PATH` is set); the text is never sent by itself (M)
- [ ] `LEAD-01` Review and merge pull requests, keep `main` building, answer "I need a change in a shared file" requests (continuous)

**Hand-off to Marwa (`EVL-04`, AI analysis page, `AI-01` is done):** in `Areas/Evaluation/Controllers/AiController.cs` inject `AiBidAnalysisService` (`using TenderSystem.Services.Ai;`, `using System.Security.Claims;`).
```csharp
// show what was already analysed (no model call)
var items = await _ai.GetLatestAsync(tenderId, User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct);

// button "Analyse offers": POST, takes about 10 seconds per offer with a local model
var run = await _ai.AnalyzeAsync(tenderId, User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct);
// run.Ok, run.Message (safe to show), run.Items (AiBidAnalysisItem: Supplier, Price, DeliveryDays, MissingFiles, Strengths, Weaknesses, Recommendation, Reason)
```
Rules for the page: always show `AiBidAnalysisService.Disclaimer` ("assistant only"); never use the recommendation to fill in scores or decisions automatically; the service already refuses users who are not on the tender's committee, but keep your `[Authorize(Roles = Roles.EvaluationCommittee)]`. Use a POST with the anti-forgery token for the button. More in `Services/Ai/README.md`.

**Hand-off for `DB-05` (evaluation drafts and Ready), to Marwa, Nada and Dana:** after you merge `dev` the schema supports drafts: `TechnicalEvaluations.Score` and `FinancialEvaluations.Score` are `decimal?` (NULL = not answered yet, never show it as 0, a real 0 is a score), `FinancialEvaluations.ReadyAt` is the member's final confirmation (NULL = still a draft, set = locked), and there is one row per member and offer (and criterion for technical): a unique index now refuses a second one, so update the existing row instead of adding another. Do not create your own migration.
* **Marwa (`EVL-02`..`EVL-05`):** build Save draft, Ready (separate POST, antiforgery, signed-in user id only), the lock after Ready and the report when the last member of EVERY eligible offer is Ready. The rules are in your hand-off file; the database part is done.
* **Nada (Sourcing):** `CommitteeBrief/SaveEvaluation` writes the same `TechnicalEvaluations`: it must refuse a member whose form is Ready (`FinancialEvaluations.ReadyAt != null` for that bid and member). The tracking page now counts only rows WITH a score as progress; add confirmed (Ready) versus pending when you touch it.
* **Dana (Manager, Top Committee):** the score view models are `decimal?` now and the pages show `Pending` for NULL. Keep that if you change them.
* **Anyone reading a score:** `score.Score ?? ...` is wrong for totals, a missing score is Pending, never zero or complete.

**Hand-off for `AI-06` (AI tender summary), to Marwa and Dana:** the card is a ready partial, `<partial name="_AiTenderSummary" model="tenderId" />`; it asks the server who may see it and shows nothing when the user may not. One line was added for you on each page, nothing else of your files changed: `Areas/Evaluation/Views/Ai/Index.cshtml` (Marwa), `Areas/Manager/Views/Reports/Index.cshtml` and `Areas/Committee/Views/Review/Index.cshtml` (Dana). Keep the line if you rearrange the page. It is an assistant only: do not use it to fill scores, the report or a decision. A new table exists after you merge `dev` (migration `AddAiTenderSummaries`, do not create your own).

**Hand-off to Sara (change password, `AUTH-01` is done):** in your onboarding controller inject `PasswordService` (`using TenderSystem.Services.Auth;`) and call
`var result = await _passwords.ChangePasswordAsync(userId, model.CurrentPassword, model.NewPassword);`. On success it already clears `MustChangePassword`, moves onboarding to InProgress, keeps the user signed in and sends a notice email. On failure show `result.Errors` (`ModelState.AddModelError("", e.Description)`). Get `userId` with `UserManager.GetUserId(User)`. Do not write your own password code.

**Hand-off to Sara (fingerprint step, `BIO-01` is built):** on your Onboarding Fingerprint page replace the "Continue demo" form with the ready widget:
`<partial name="_PasskeyRegister" />` and load `<script src="~/js/passkey.js"></script>` plus `<link rel="stylesheet" href="~/css/face.css" />`. It offers "Set up on THIS device" (Windows Hello, Touch ID, phone fingerprint) and "Use my PHONE" (QR code), so the user can finish the setup from the laptop or the phone. It talks to `/Passkey/RegisterOptions` and `/Passkey/Register` (you write no backend) and raises a `passkey:registered` event on the widget element. A working standalone page exists at `/Passkey` (it also lists and removes the user's devices). Whether the user HAS a fingerprint saved: `PasskeyService.CountAsync(userId) > 0`.

**Hand-off to Sara (face step, `BIO-02` is built):** on your Onboarding Face page replace the "Continue demo" form with the ready widget:
`<partial name="_FaceCapture" model="@("enroll")" />` and load `<script src="~/js/face.js"></script>` plus `<link rel="stylesheet" href="~/css/face.css" />` (inside your `@section Scripts` / `@section Styles`). The widget opens the camera, runs the guided head movement and saves the template through `/Face/Begin` and `/Face/Enroll` (you write no backend). When it succeeds it raises a `face:enrolled` event on the widget element, for example `document.querySelector('[data-face-capture]').addEventListener('face:enrolled', () => { /* show "Continue" */ })`. A working standalone page already exists at `/Face` to compare with. Whether a user HAS a face saved: `FaceService.CountAsync(userId) > 0`.

**Hand-off to Sara (for `ONB-01`):** the endpoints and JavaScript functions of `BIO-01` and `BIO-02` are published as a short note in the PR description. Sara builds the screens against that note.
**Hand-off to Abdallah (`AUTH-03` is built):** in `Areas/Admin/Controllers/UsersController.cs` replace the "Employee creation is unavailable..." message by a call. Inject `PasswordService` (`using TenderSystem.Services.Auth;`) and write, inside `if (ModelState.IsValid)`:
```csharp
var (result, _) = await _passwords.CreateEmployeeAsync(model.FullName, model.Email, model.Role!, model.DepartmentId, model.JobTitle, model.PhoneNumber);
if (result.Succeeded) { TempData["UserSuccess"] = "Employee created."; return RedirectToAction(nameof(Index)); }
foreach (var e in result.Errors) ModelState.AddModelError("", e.Description);
```
It creates the account, generates the temporary password, emails it (it is never shown on screen) and audits the creation. If the email cannot be sent the account is not kept. Also remove the "Account creation is paused" notice from the Create view. **Sara:** the first sign-in of that employee lands on your `Onboarding/ChangePassword` page, so it must call `PasswordService.ChangePasswordAsync` (it clears the `MustChangePassword` flag).

**Hand-off to Abdallah (`AUTH-02` is built):** a supplier now uploads legal documents while registering; they are rows in `SupplierDocuments` (`DocType`: "Commercial registration", "Tax certificate", "Other document"; `ExpiryDate`; `FilePath`). On your supplier **Review** page (`Areas/Admin/Views/Suppliers/Review.cshtml`) show them and link the download (only an Admin can open it, every download is logged in `DocumentAccessLogs`):
```csharp
// SuppliersController.Review: load them with the supplier
var documents = await _db.SupplierDocuments.AsNoTracking().Where(d => d.SupplierProfileId == id).OrderBy(d => d.Id).ToListAsync();
```
```html
<a class="btn btn-sm btn-outline-brand" asp-area="" asp-controller="SupplierDocument" asp-action="Download" asp-route-id="@doc.Id">@doc.DocType</a>
@if (doc.ExpiryDate is not null) { <small>expires @doc.ExpiryDate.Value.ToString("yyyy-MM-dd")</small> }
```
Show an "expired" badge when `ExpiryDate < DateTime.UtcNow.Date`. The download endpoint is `Controllers/SupplierDocumentController.cs` (do not edit it).

**Hand-off to everyone who sends email:** `EmailService` is ready (`EML-01`). Inject it and call `await _email.SendAsync(to, subject, EmailTemplates.Welcome(...))`. Templates: `Welcome`, `SupplierApproved`, `SupplierRejected`, `SupplierSuspended`, `PasswordReset`, `Notification`. It never throws. With `SMTP_ENABLED=false` (default) emails are saved as .html files in `App_Data/emails` instead of being sent, so you can test without a mail server.

### Abdallah
- [ ] `ADM-01` Admin dashboard: counts of users, suppliers, departments, requests, tenders (S) **Phase 1**
- [ ] `ADM-02` **Users:** list with search and role filter, create (name, email, department, role, job title, temporary password with `MustChangePassword`), edit, enable/disable, reset password (L) **Phase 1**
- [ ] `ADM-03` **Supplier review:** list by status, details with company data, Approve / Reject / Suspend with a reason. Approving sets `SupplierProfile.Status` and `IsActive`, and notifies the supplier (M) **Phase 1** (this unlocks the whole supplier flow)
- [ ] `ADM-04` Departments list: create, edit, deactivate (S) **Phase 1**
- [ ] `ADM-05` Audit logs page: filter by user, entity, date, includes login attempts (S) **Phase 2** (needs `AUD-01`)
- [ ] `CON-01` **Contact Us** page: company details, form with validation, success message, optional email through the SMTP settings (S) **Phase 1**
- [ ] `VOI-01` **Voice search** for the task list: `voice-search.js` + `_VoiceSearch.cshtml` partial (browser speech recognition), reusable on every employee dashboard (M) **Phase 2**

### Dana
- [ ] `REQ-01` Requester dashboard: my requests by status and the task list (S) **Phase 1**
- [ ] `REQ-02` **Create purchase request:** title, reason, priority, target date and an items table (name, quantity, unit, specifications), Save draft / Submit (M) **Phase 1**
- [ ] `REQ-03` My requests (filters, search) and Request details with review notes and timeline, edit when Draft or Returned (M) **Phase 1**
- [ ] `MGR-01` Manager dashboard and report review page (M) **Phase 2**
- [ ] `MGR-02` Manager decision: Approve / Reject / Return with comments, forward to Top Committee when `RequiresTopCommittee` (M) **Phase 3**
- [ ] `TOP-01` Top Committee dashboard and review package (requisition, bids, report, manager notes) (M) **Phase 2**
- [ ] `TOP-02` Final decision: each member votes, comment saved, award the tender, notify suppliers (L) **Phase 3**
- [ ] `TOP-03` Bid history and statistics report (M) **Phase 4**

### Nada
- [ ] `HOM-01` Home page: refine the landing page that exists (images, wording, real data) (S) **Phase 0**
- [ ] `HOM-02` **About** page and **How It Works** page with the visual 12-step workflow (M) **Phase 1**
- [ ] `SRC-01` Sourcing dashboard (S) **Phase 1**
- [ ] `SRC-02` **Incoming requests:** list, details, Approve / Return (with note) / Reject (M) **Phase 2**
- [ ] `SRC-03` **Tenders:** list, create from an approved request, dates and terms, evaluation criteria with weights that must total 100%, criteria locked when published (L) **Phase 2**
- [ ] `SRC-04` **Select suppliers and invitations:** only Approved suppliers, send invitations, invitation status table (M) **Phase 2**
- [ ] `OPT-01` Optional feature **QR code** for each tender and request details page (S) **Phase 4**

### Deyaa
- [ ] `UI-01` **Layout and palette polish:** responsive check on phone, tablet, desktop (mobile sidebar), consistent look of cards, tables, forms, badges (M) **Phase 0**
- [ ] `UI-02` UI kit as small partials in `Views/Shared/`: page header, status badge, empty state, confirm dialog, toast message (M) **Phase 1** (others will reuse them)
- [ ] `UI-03` Keep the 7 sidebars up to date when new pages appear (S, continuous)
- [ ] `NTF-01` **Notifications:** `NotificationService`, Notifications page, bell with unread count in the header, mark as read (M) **Phase 1** (blocker for others, publish the service first)
- [ ] `SRC-05` Sourcing **Compare offers:** side-by-side price, specs, delivery, documents (M) **Phase 2**
- [ ] `SRC-06` Sourcing **Compliance check:** Compliant / Non-Compliant (reason) / Pending Clarification (M) **Phase 2**
- [ ] `SRC-07` Sourcing **Evaluation tracking:** who finished, report reached the manager (S) **Phase 3**
- [ ] `CLR-01` **Clarifications:** allowed receivers by role and stage, create, inbox (sent / received), thread with replies, status Pending / Answered / Resolved, linked to request, tender or bid, notification on each message (L) **Phase 3**

### Marwa
- [ ] `DB-02` **Demo data seeder** (`Data/Seed/DemoDataSeeder.cs`, Development only, switched by `SEED_DEMO_DATA` in `.env`): sample requests with items, a published tender with criteria, 3 to 4 suppliers, invitations, bids with items, some evaluations (M) **Phase 1** (priority: unblocks Nada, Deyaa, Sara, Dana). If it needs a schema change, ask Wadea
- [ ] `AUD-01` **`AuditService`** and its registration (S) **Phase 1** (blocker for others, publish it first)
- [ ] `EVL-01` Evaluation dashboard (S) **Phase 2**
- [ ] `EVL-02` **Technical evaluation:** score per criterion, observations, non-compliance notes (M) **Phase 2**
- [ ] `EVL-03` **Financial evaluation:** price, taxes, transport, payment terms, total cost (M) **Phase 2**
- [ ] `EVL-04` **AI analysis view** (consumes `AiBidAnalysisService`, labelled "assistant only") (S) **Phase 3**
- [ ] `EVL-05` **Unified report:** scores, comparison, recommendation with justification, send to the manager (L) **Phase 3**
- [ ] `RPT-01` **Reports:** requests, tenders, suppliers, bids, decisions, with filters and PDF / Excel export (L) **Phase 4** (the PDF and Excel packages are approved by Wadea first)

### Sara
- [ ] `ONB-01` **Onboarding screens** (Shared area): change password, profile (`EmployeeProfile`), fingerprint page, face page, finish with consent checkbox. Build the UI now; connect to Wadea's biometric endpoints when published (M) **Phase 1**
- [ ] `SUP-01` Supplier dashboard: invitations waiting, my offers, approval status (S) **Phase 1**
- [ ] `SUP-02` **Invitations:** accept / decline, deadline countdown, expiry (M) **Phase 2**
- [ ] `SUP-03` Tender details, only for invited tenders (S) **Phase 2**
- [ ] `SUP-04` **Submit offer:** items with prices, delivery time, validity, payment terms, technical summary, Save draft / Submit / Withdraw, deadline and invitation checks on the server (L) **Phase 2**
- [ ] `SUP-05` **`FileStorageService` and secure upload:** allowed types and size from `.env`, random file name, stored in `App_Data/uploads`, download endpoint (M) **Phase 1** (publish the service first)
- [ ] `SUP-06` My offers with status and a downloadable personal summary report (M) **Phase 2**
- [ ] `SUP-07` Company profile and renewed document upload (S) **Phase 2**
- [ ] `OPT-02` Optional feature **watermark**: the viewer's name and time on viewed and downloaded documents, logged in `DocumentAccessLogs` (M) **Phase 4**

*Optional features: the brief asks for at least two. We chose QR code and watermark. Digital signature is not planned.*

---

### Fixes found by the project scan (each owner fixes inside THEIR OWN files)

A scan of `dev` after PR #22 found these. Wadea fixed the ones in his own files (`INT-02`). The rest sit in files that belong to an open task of their owner, so each owner adds them with their task (no one edits another person's file). Use `AuditService`, `NotificationService` and `EmailService` as shown; add the services as extra constructor parameters.

| Id | Owner | File | What |
|---|---|---|---|
| `FIX-01` | Abdallah (`ADM-03`) | `Areas/Admin/Controllers/SuppliersController.cs` | approve / reject / suspend must audit, notify and email the supplier **(done by Wadea in `INT-03`)** |
| `FIX-02` | Abdallah (`ADM-02`, `ADM-04`, `CON-01`) | `UsersController`, `DepartmentsController`, `ContactMessagesController` | audit entries for edits, role changes, department changes **(done by Wadea in `INT-03`)** |
| `FIX-03` | Abdallah (`ADM-05`) | `Areas/Admin/Controllers/AuditLogsController.cs` and its view | the Audit logs page is still a stub |
| `FIX-04` | Dana (`REQ-02`) | `Areas/Requester/Controllers/RequestsController.cs` | audit the new request and notify Sourcing on Submit (replaces the TODO) **(done by Wadea in `INT-03`)** |
| `FIX-05` | Sara (`SUP-04`) | `Areas/Supplier/Controllers/OffersController.cs` | audit offer submission and withdrawal, notify Sourcing (replaces the two TODOs) **(done by Wadea in `INT-03`)** |

**Status:** `FIX-01`, `FIX-02`, `FIX-04` and `FIX-05` were implemented by Wadea in `INT-03` (one commit per owner, tested end to end). Pull `dev`; do not add them again. Only `FIX-03` (the Audit logs page) is still Abdallah's. The code blocks below show what was added, so you can follow the pattern in your next actions.

**`FIX-01` for Abdallah.** In `SuppliersController` add `AuditService _audit`, `EmailService _email`, `NotificationService _notify` (`using TenderSystem.Services.Audit; using TenderSystem.Services.Email; using TenderSystem.Services.Notifications;`). In `ChangeStatusAsync`, right after `await transaction.CommitAsync();` and before the success message:
```csharp
var info = await _db.SupplierProfiles.AsNoTracking().Where(s => s.Id == id)
    .Select(s => new { s.UserId, s.CompanyName, s.User.Email }).FirstAsync();
await _audit.LogAsync("StatusChanged", "SupplierProfile", id.ToString(), newStatus, currentStatus);
await _notify.NotifyAsync(info.UserId, "SupplierStatus", "Your registration was " + newStatus.ToLowerInvariant(),
    reason ?? "You can now sign in.", "/Supplier/Dashboard");
var body = newStatus switch
{
    "Approved" => EmailTemplates.SupplierApproved(info.CompanyName, _email.AbsoluteUrl("/Account/Login")),
    "Rejected" => EmailTemplates.SupplierRejected(info.CompanyName, reason!),
    _ => EmailTemplates.SupplierSuspended(info.CompanyName, reason!),
};
await _email.SendAsync(info.Email!, "Your supplier registration: " + newStatus, body);
```
`EmailService` never throws; the reason is already validated (10 to 500 characters) for Rejected and Suspended.

**`FIX-02` for Abdallah.** Same pattern (`await _audit.LogAsync(action, entity, id, details)`), after the save succeeds:
* `UsersController.Edit` (POST), after `await transaction.CommitAsync();`: `await _audit.LogAsync("Updated", "ApplicationUser", user.Id, "Role: " + model.Role, string.Join("|", currentRoles));` (the old roles go in the last argument).
* `DepartmentsController.Save`, after `SaveChangesAsync`: `"Created"` or `"Updated"`, entity `"Department"`; `SetActive`: `isActive ? "Activated" : "Deactivated"`.
* `ContactMessagesController.MarkRead`: `"MarkedRead"`, entity `"ContactMessage"` (optional).

**`FIX-03` for Abdallah.** `AuditLogs` already fills with `LoginSucceeded`, `LoginFailed`, `LoggedOut`, `Registered`, `PasswordChanged`, `PasswordReset`, `PasswordResetRequested`, `PasskeyAdded`, `PasskeyRemoved`, `FaceEnrolled`, `OnboardingCompleted`, `Created`, `StatusChanged`, `Submitted`. The page lists them newest first with filters for user, entity and date (see `ADM-05`); join the user's name through `UserId`.

**`FIX-04` for Dana.** In `RequestsController.Create`, replace the `// TODO: Record this action...` line, after `SaveChangesAsync()` (add `AuditService _audit`, `NotificationService _notify`):
```csharp
await _audit.LogAsync(isSubmit ? "Submitted" : "Created", "PurchaseRequisition", request.Id.ToString(), request.RequestNumber);
if (isSubmit)
    await _notify.NotifyRoleAsync(Roles.SourcingOfficer, "RequestSubmitted", "New purchase request",
        $"{request.Title} ({request.RequestNumber}) was submitted.", "/Sourcing/Requests");
```

**`FIX-05` for Sara.** In `OffersController` add `AuditService _audit`, `NotificationService _notify`. Replace the first TODO (inside `if (isSubmit)`):
```csharp
await _audit.LogAsync("Submitted", "Bid", bid.Id.ToString(), "Offer submitted");
await _notify.NotifyRoleAsync(Roles.SourcingOfficer, "OfferReceived", "New offer received", "A supplier submitted an offer.", "/Sourcing/Offers");
```
and the second TODO (in `Withdraw`): `await _audit.LogAsync("Withdrawn", "Bid", bid.Id.ToString(), "Offer withdrawn");`.
 
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

1. **One task = one branch = one pull request.** Create the branch from `dev`: `feature/<ID>-<short-name>` (for example `feature/SUP-04-submit-offer`). Push **only that branch**, never `dev` or `main`, and open the PR **into `dev`**. Wadea merges `dev` into `main`. Small PRs, merged within 1-2 days.
2. **Pull first, every morning:** `git checkout dev`, `git pull`, then merge `dev` into your branch.
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
