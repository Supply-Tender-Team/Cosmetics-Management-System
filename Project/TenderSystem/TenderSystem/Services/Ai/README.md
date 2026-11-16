# AI assistant chat (`AI-02` chat, `AI-03` data access)

A floating chat button on every dashboard page. The answers depend on the signed-in user's **role** (Admin, Requester, Sourcing Officer, Evaluation Committee, Sourcing Manager, Top Committee, Supplier) and on the page the user is looking at. Since `AI-03` it also reads **real data**, limited to what that role may see (see "How it reads data").

## Who answers: one switch in `.env`

```text
# local model. Needs Ollama running and the model installed on that computer: ollama pull <name>
# (it can be a teammate's computer: OLLAMA_BASE_URL=http://<his-IP>:11434, trusted network only)
AI_CHAT_PROVIDER=ollama
OLLAMA_BASE_URL=http://localhost:11434
OLLAMA_MODEL=gemma4:12b

# OR a hosted API that speaks the OpenAI "chat/completions" format (OpenAI, Groq, OpenRouter, Gemini's compatible endpoint...)
# AI_CHAT_PROVIDER=openai
# AI_API_BASE_URL=https://api.openai.com/v1
# AI_API_KEY=            (secret, only in your own .env)
# AI_API_MODEL=gpt-4o-mini

# OR no assistant at all: the button is hidden. THIS IS THE DEFAULT when the key is missing.
# AI_CHAT_PROVIDER=off

# optional limits
# AI_CHAT_TIMEOUT_SECONDS=120   AI_CHAT_MAX_CHARS=1000   AI_CHAT_MAX_HISTORY=10   AI_CHAT_MAX_PER_MINUTE=8

# data access (AI-03)
# AI_CHAT_DATA=true             false = the assistant never reads the database (answers about pages only)
# AI_CHAT_DATA_WITH_API=false   a HOSTED model gets no database data unless you set this to true (the data would leave the company)
```

Restart the app after changing `.env`. The API key stays on the server; the browser never sees it. With a hosted API the question text leaves the company, so decide that before switching it on.

## How it reads data (`AI-03`)

The model never queries anything. For each question the server (`AiDataService`, one file per role) picks a lookup from the words of the question (English and Arabic), runs a query that is **already limited to the user inside the query**, and gives the rows to the model as a `FACTS` block. The same rows are also sent to the browser as a **table** (shown right under the answer, copied from the database, never retyped by the model).

| Role | What it can ask for | Limited to |
|---|---|---|
| Admin | last N audit log records, suppliers (pending / by status), users by role, contact messages | Admin only. No passwords, IP addresses, message bodies or e-mails |
| Requester | own requests (latest, by status), one request by its number | `RequestedByUserId == me` |
| Supplier | own invitations, own offers, a tender they were invited to | their own `SupplierProfile`; never another supplier's offer or price |
| Sourcing Officer | tenders, submitted requests, offers of a tender, a request or tender by number | no drafts of other people |
| Evaluation Committee | their tenders, offers of a tender with a precomputed summary (lowest price, fastest delivery, technical summaries) | tenders where they are in `CommitteeMembers` |
| Sourcing Manager / Top Committee | tenders waiting at their level, with the committee report | status of the tender |
| Everyone | own notifications, a small overview of own numbers | `UserId == me` |

Examples: *"show me the last 10 audit records"* (Admin), *"summarize the offers of DEMO-TND-002"* (Evaluation Committee), *"which of my requests are approved?"* (Requester). A request or tender number typed in the question is only a filter inside the scoped query; if the user may not see it the answer is "not found", never "it exists but...".

Rules: at most 25 rows (10 by default), long text is cut, text written by users is wrapped as data and cannot close the `FACTS` block, every lookup is written to the audit log as `AiDataAccess` (lookup name and row count, **never the question**), and with `AI_CHAT_PROVIDER=openai` no database data is sent unless `AI_CHAT_DATA_WITH_API=true`.

**Adding a lookup** (for example when Sourcing or Evaluation get new screens): add a method in the role's `AiDataService.<Role>.cs`, start the query from the user's own scope (copy the rule of the matching controller), return `Make(name, table, extraFacts)`, and add a check to the e2e test. Keep it read only.

## Command center (`AI-04`): the assistant can DO things, but only with the Admin's fingerprint or face

Off by default (`AI_ACTIONS=true` turns it on). Only an **Admin** sees it, as a separate **Command** tab inside the same chat window (next to **Ask**), with its own conversation (the Ask tab only answers questions and never sends anything). First actions: send an **email** or an in-app **notification** to **employees** (everyone, one role, one department, **one person by email address or full name**, or just yourself as a test). Suppliers, people without an account here and inactive users are not reachable.

How to write a command: normally, one sentence with who and what, for example `Email everyone in Packaging that tomorrow's meeting moved to 10:00`, `Notify the sourcing officers to review the incoming requests today`, or `Email sara@company.com: please send your request by Friday`. Arabic works too.

If email sending is not connected (`SMTP_ENABLED=false`) the card says so and the result says the message was only **saved as a file** in `App_Data/emails`, so nobody thinks a real email left the server.

```
Admin types       "Email everyone in Packaging that tomorrow's meeting moved to 10:00"
1 PROPOSE         the model only fills a fixed form (action, audience, subject, body). The SERVER finds the real recipients with a query.
2 PREVIEW         the chat shows a card: how many people, who, the exact subject and text, a countdown.
3 APPROVE         the Admin proves it is really them with a FRESH fingerprint or face check, for this one action.
4 EXECUTE         once, from what the server stored. Audited (never the message text).
```

* **Step-up approval.** Fingerprint: the challenge only accepts the signed-in Admin's own keys and is tied to this action (`PasskeyService.BeginApprovalAsync` / `CompleteApprovalAsync`). Face: ONE quick step (look straight; the anti-photo model checks the face is live) and only when it is not sure one more head turn to a random side, then a 1:1 match against the signed-in Admin's own saved face (`FaceService.MatchAsync`). A key or face of anyone else is refused, a proof made for another action is refused, a proof cannot be replayed.
* **The browser cannot change anything.** It sends only the action id and the proof. Text and recipients were fixed on the server when the action was proposed (kept in memory for `AI_ACTION_TTL_MINUTES`, default 5).
* **Exactly once.** `Proposed -> Approved -> Done|Failed` (or `Cancelled` / `Expired`). A second click, a replay or a parallel request cannot run it again. 3 wrong fingerprint or face checks cancel the action.
* **Limits** (all in `.env`): `AI_ACTION_MAX_RECIPIENTS` (100), `AI_ACTION_MAX_PER_HOUR` (5 actions per Admin per hour, even with a valid fingerprint), `AI_ACTION_MAX_PROPOSALS_PER_MINUTE` (10). Only active employees with an email; never a supplier.
* **Text hygiene.** The subject is one line (no header injection), the body is cut and HTML-escaped, and every email names who sent it.
* **The model never sees recipients or database data**, only what the Admin typed, so there is nothing for a prompt injection to carry.
* **Audit** (`AiActionProposed`, `AiActionApproved` with the method, `AiActionExecuted` with the counts and the subject, `AiActionCancelled`, `AiActionApprovalFailed`): never the body.
* An Admin with no fingerprint and no face saved cannot even propose an action.

Files: `Services/Ai/AiActionService.cs` (all the rules), `AiActionModels.cs`, `AiActionModule.cs` (settings), `Controllers/AiActionController.cs`, and the approve helpers in `wwwroot/js/passkey.js` (`MsWandPasskey.approve`) and `face.js` (`approve` mode). **Adding another action type** (for example suspending a supplier): add it to the form and to `ExecuteAsync`, keep the same propose, approve and execute-once pipeline, and add tests.

Not built, by decision: emailing suppliers, a second approving Admin, scheduled messages, attachments.

### Excel templates (`AI-09`): create accounts or send messages from a file

In the Command tab the Admin can attach an **Excel file made from a template** (the paperclip button). Two templates: **Create employee accounts** (Full name, Email, Role, Department, Job title, Phone) and **Send emails or notifications** (Email, Subject, Message). The template is downloaded from the server (`GET /AiAction/Template?kind=accounts|messages`): a header row only, plus a Help sheet with the allowed roles and the real department names, and drop-downs for Role and Department.

```
Admin attaches    people.xlsx (made from the template) and presses send
1 PROPOSE         the SERVER reads and checks the file (plain code, not the AI). Valid rows are kept, invalid rows are skipped with their row number and the reason.
2 PREVIEW         the card shows the valid rows, the skipped rows and why, and a countdown.
3 APPROVE         a FRESH fingerprint or face check for THIS file, exactly like every other action.
4 EXECUTE         once, from the rows stored on the server: accounts through PasswordService.CreateEmployeeAsync (temporary password by email), messages through the e-mail / notification services.
```

* **The AI never sees the file.** The rows (names, emails) are read and validated by `AiExcelTemplates` and `AiActionService.Files.cs`. The model only writes the optional *common* text of a messages file, from the note the Admin typed ("tell them the system is down tomorrow"), for the rows that have no Subject / Message of their own. Without a note, rows without their own text are skipped.
* **Safe reading.** Only a real `.xlsx` (no macros, embedded objects or links, not a zip bomb), the headers must match the template, every cell is read as plain text (a formula cell is refused, control characters removed, length capped), and the file is processed in memory and never stored. The audit keeps a short fingerprint of the file and the counts, never names, emails or text.
* **Row checks (accounts):** name 2-150 characters, valid email that has no account yet and is not repeated in the file, role from the same employee list as the Users page (never Admin or Supplier), an active department (name or code), a Jordanian mobile number (a number typed without its leading 0 gets it back), job title up to 100 characters. **(messages):** the email must belong to an active employee (suppliers and outsiders are skipped).
* **Limits** (`.env`): `AI_ACTION_MAX_FILE_MB` (2), `AI_ACTION_MAX_FILE_ROWS` (50 accounts per file; messages use `AI_ACTION_MAX_RECIPIENTS`), `AI_ACTION_MAX_ACCOUNTS_PER_HOUR` (100); a file counts as one action for `AI_ACTION_MAX_PER_HOUR`.
* **Result.** After approval the card lists every person with `Created` / `Not created: reason` (or `Email sent` / `Notification sent`). A failed row never undoes the others. If email is not connected (`SMTP_ENABLED=false`) the card says so before approving, and the temporary passwords only exist in the saved email files.

Files: `Services/Ai/AiExcelTemplates.cs` (template writer, safe reader), `Services/Ai/AiActionService.Files.cs` (propose, row checks, execute), `AiActionModels.cs`, `AiActionModule.cs`, the two endpoints `Template` and `ProposeFile` in `Controllers/AiActionController.cs`, and the attach panel in `Views/Shared/_AiChat.cshtml`, `wwwroot/js/ai-chat.js`, `wwwroot/css/ai-chat.css`. More templates (invite suppliers, bulk role changes) would reuse the same pipeline.

Not built, by decision: external email addresses, supplier invitations from a file, reading the file with the model.

### A smarter chat (`AI-11`): follow-ups, general work help, copy and export

* **Follow-ups.** `AiFollowUp.Resolve` gives a follow-up question ("what about its delivery?", "analyze the second one", "and that?") the record number it refers to: the last tender or request number of the earlier turns, or, for first / second / third / last, the matching row of the last table (the browser sends the record numbers of that table with the assistant turn). The number is added **for the data lookup only**; the model still gets the user's own words. A record number is only a filter inside queries already limited to the user, so a number the user may not see is "not found", and a number typed now always wins. Help questions ("how do I ...") are never turned into lookups.
* **General work help.** The rules now allow general WORK questions (procurement concepts, drafting emails, Arabic / English translation, simple calculations). Such answers start with **"General knowledge:"** ("معلومة عامة:") and are never presented as this company's data; facts about tenders, offers, requests and people still come only from FACTS; unrelated requests (recipes, personal advice) are declined. Each role has one general example among its starter questions.
* **Copy and export.** A copy button on every answer and on every table (a table is copied tab separated, so it pastes into Excel as a grid), and **Export to Excel** on every table (`POST /AiChat/ExportTable`, signed-in users only, anti-forgery token, at most 26 columns, 500 rows and 500 characters per cell; every cell is written as text, so nothing can run as a formula; audited as `AiTableExported` with the row count only).

Files: `Services/Ai/AiFollowUp.cs`, `AiChatService.cs` (`AiChatTurn.Numbers`), `AiChatPrompts.cs`, `Services/Ai/AiTableExcelWriter.cs`, `Controllers/AiChatController.cs`, `wwwroot/js/ai-chat.js`, `wwwroot/css/ai-chat.css`.

## Files

| File | What it does |
|---|---|
| `Views/Shared/_AiChat.cshtml` | the widget (one `<partial name="_AiChat" />` in `_DashboardLayout.cshtml`) |
| `wwwroot/js/ai-chat.js`, `wwwroot/css/ai-chat.css` | behaviour and look |
| `Controllers/AiChatController.cs` | `POST /AiChat/Ask` (signed-in users only, anti-forgery token) |
| `Services/Ai/AiChatService.cs` | checks the question, per-user rate limit, builds the messages, picks the provider |
| `Services/Ai/AiDataService*.cs` | the role-limited lookups (`AI-03`), one file per role |
| `Services/Ai/AiChatPrompts.cs` | the rules and the per-role description of pages. **Edit this file to change what the assistant knows.** |
| `Services/Ai/OllamaChatProvider.cs`, `OpenAiChatProvider.cs` | the two ways to reach a model (`IAiChatProvider`) |

## Safety rules built in

* The role comes from the sign-in on the server, never from the browser. The page path from the browser is accepted only if it is a plain path.
* Earlier messages sent by the browser are trusted only as `user` / `assistant` text, so nobody can inject a `system` message.
* The assistant has no database access of its own. Data reaches it only through `AiDataService`, which loads only what the signed-in user may see, so it cannot leak one supplier's data to another.
* Questions and answers are not stored and not logged. Only errors are logged, and data lookups are audited without the question.
* The widget says "AI assistant only", and the prompt forbids it from deciding or approving anything.

## Not the same as the chat

The bid analysis (`AI-01`, above) is a separate service. It reuses `OLLAMA_*` and the provider classes of the chat.

## Bid analysis (`AI-01`)

`AiBidAnalysisService` is a separate feature from the chat. For every submitted (not draft) offer of a tender it sends the tender's requirements and ONE offer to the model and asks for a fixed JSON answer:

```json
{"price": 2050, "delivery_days": 7, "missing_documents": ["ISO certificate"], "strengths": ["..."], "weaknesses": ["..."], "recommendation": "Recommended | Acceptable | Not recommended", "reason": "one sentence"}
```

* It is saved in `AiBidAnalyses` (one row per offer per run: the request and response JSON, the extracted price and delivery, missing files, strengths, weaknesses, the recommendation). Running it again adds new rows; `GetLatestAsync` returns the newest one per offer.
* **Strict:** the answer must have the shape above. If not, the model is asked once more; if it still fails, nothing is saved for that offer and the run stops with a clear message. A negative price or an absurd delivery time is ignored (the offer's own values are used). Lists are cut to 8 points.
* **Only committee members of that tender** can run it or read the results (checked in the service). One analysis of a tender runs at a time.
* **It uses the LOCAL model by default** (`AI_ANALYSIS_PROVIDER=ollama`, same `OLLAMA_*` settings as the chat), because the data holds prices and supplier documents. `openai` sends them to a hosted API (uses `AI_API_*`), `off` disables it. `AI_ANALYSIS_TIMEOUT_SECONDS=180` is the time allowed for one offer.
* Supplier text (technical summary, item specifications) is wrapped as data and cut, so it cannot give the model instructions. The model only **assists**: always show `AiBidAnalysisService.Disclaimer` next to a result; the committee scores and decides.
* Not yet used: the text inside the offer's uploaded files (PDF, Word). Only the typed offer data and the list of document names are analysed.

Calling it (for the Evaluation AI page, `EVL-04`): see the hand-off to Marwa in `TASKS.md`.

## Tender summary (`AI-06`): one summary of a whole tender for the people who decide
`AiTenderSummaryService` writes a short summary of ONE tender with all its eligible offers side by side: an overview, per offer strengths, weaknesses and the main risk, points to check, and a labelled "leaning" (a suggestion, never a decision). It is shown as a card (`<partial name="_AiTenderSummary" model="tenderId" />`) on three pages: Evaluation AI page, Manager review page, Top Committee review package. It never fills scores, the report or a decision.

* **Who sees it** (decided in the service, not only in the controller): Top Committee for tenders waiting for its decision; Sourcing Manager for tenders it sits on as the manager seat, from the manager review on; the Evaluation Committee only **after the member confirmed Ready on every eligible offer** (or the report exists), so the suggestion cannot influence the scoring. Everybody else gets "denied" and the card stays hidden.
* **The code calculates, the model only writes.** Prices, technical and financial points, totals and the ranking are computed by the server (same rule as the evaluation report) and handed to the model as facts. Only **confirmed (Ready)** forms count; drafts, NULL scores and Draft offers never reach the model. While some forms are unconfirmed the points are marked provisional, there is no ranking, and the server blanks the model's leaning.
* **Supplier text is data.** It sits inside `<<<DATA ... DATA>>>` (the block cannot be closed early), the answer must be the exact JSON shape (one retry), and a supplier name the model invents is dropped.
* The summary is **saved** (`AiTenderSummaries`, one row per tender and audience) with a fingerprint of the facts; when the data changes later the card says **Outdated**. Limits: 6 summaries per user per hour, one at a time per tender and audience, `AI_SUMMARY_TIMEOUT_SECONDS` (default 300). Audited as `AiSummary` (no text in the log).
* **The attached documents are NOT read** (only their names, and the card says so). Reading them (Word, Excel, text PDF) is the planned next step.

### Analyze a tender package in the chat (`AI-07`)
The Sourcing Manager and the Top Committee can ask the chat to analyze a package (`can you analyze a review package?`, `summarize the report waiting for me`, `analyze DEMO-TND-002`). `AiDataService.Package.cs` finds the tender (a number typed in the question, or the one tender waiting for them; with several it lists them and asks which), checks the access with the same rules as the AI summary card, and hands the model the same facts: confirmed committee results only, every number calculated by the server, plus the report and, for the Top Committee, the manager's decision. The offers table is shown to the user exactly as calculated. When nothing is waiting the chat says so plainly and mentions what was awarded lately. A help question (`How do I review a tender package?`) is not a data lookup. The evaluation committee keeps its own offers lookup. Attached documents are not read.

### Helpers for each role (`AI-08`)
The assistant also helps with the WORK of each role, not only with "where is the page". `AiDataService.Assist.cs` adds one lookup per helper. Each runs a query already limited to the user, returns a table (copied from the database) plus a facts text that starts with a `TASK:` line and ends with a `REMINDER:` line (this is what makes the model do the task instead of describing pages). Everything is read only: nothing is filled in, saved, sent or decided for the user.

| Role | Ask for | What the server gives the model |
|---|---|---|
| Evaluation Committee | "compare the offers of my tender", "which offer is best and why", "what should I ask the supplier?", "explain the scoring criteria" | rubric, requested vs offered items and specifications, price / delivery / validity observations and gaps calculated by the server, the member's OWN scores only. Another member's scores and notes are never read. The confirmed committee results are added only after the member confirmed Ready on all their own forms (same rule as the AI summary card). "Best" is an AI opinion from the offer data, never a score |
| Requester | "help me write the specifications for ..." | a task to write a draft from the user's own words only (`[to fill]` markers, never invented details) and the user's OWN similar earlier requests as wording examples |
| Sourcing Officer | "check the compliance of TND-...", "suggest technical questions for PR-...", "which suppliers can I invite for packaging?" | requirements found in the tender terms (`ComplianceTermsExtractor`) against each offer's fields and files (a field and keyword check, not proof), the request items with weight options that total 80, approved suppliers of a category |
| Supplier | "check if my offer is complete" | a checklist of the supplier's OWN offer (currency, delivery, validity, payment terms, summary, file, price and specification per item, deadline) |

Help questions ("how do I ...") are not intercepted. The attached documents are still not read.
