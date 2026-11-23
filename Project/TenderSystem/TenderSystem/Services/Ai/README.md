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

### Live nudges and voice typing (`AI-12`)

* **The cloud above the round button only says real things.** `AiNudgeService` + `GET /AiChat/Nudges` return up to 3 short items made from **counts of the signed-in user's own work**: Admin (suppliers waiting for approval, unread contact messages), Requester (requests returned to them), Sourcing Officer (offers waiting for compliance review, requests waiting for review, reports ready to send to the manager), Evaluation member (tenders whose brief was sent and whose Ready is not confirmed), Sourcing Manager (reports waiting for their review), Top Committee (tenders waiting for their vote), Supplier (invitations waiting for an answer, draft offers) and everybody (clarifications waiting for an answer). No model call, nothing sent anywhere, cached 60 seconds per user, and an empty list when `AI_CHAT_DATA=false`. **Nothing waiting = no cloud, no dot, no hop.** Clicking an item runs the matching question in the chat (for example "Check the compliance of the waiting offers") or opens the page. Same limits as before: at most 3 per session, dismissible.
* **Voice typing.** A mic button next to Send (Ask tab and Command tab). Two modes, chosen by the server (`data-voice` on the widget): **whisper** when the file named in `VOICE_MODEL_PATH` exists (the browser records up to 30 seconds, converts it to 16 kHz mono WAV and posts it to `POST /AiChat/Transcribe`, which uses the same local `VoiceTranscriptionService` as the Admin voice search; 6 recordings per minute per user; nothing leaves the company), otherwise **browser** (the browser's own speech recognition, with a one-time privacy notice because the browser may send the audio to its vendor; an EN / AR switch). The recognised text goes **into the input box for the user to read and send**: it is never sent by itself and never stored. The mic is hidden when the browser cannot do either.

Files: `Services/Ai/AiNudgeService.cs`, `Controllers/AiChatController.cs` (`Nudges`, `Transcribe`), `wwwroot/js/ai-voice.js`, `Views/Shared/_AiChat.cshtml`, `wwwroot/js/ai-chat.js`, `wwwroot/css/ai-chat.css`.

### Reading the attached offer documents (`AI-13`)

* **What is read.** `AiDocumentReader` opens the files of an offer through the same file storage as the protected download and returns a short text excerpt: **PDF** (PdfPig, the text layer, first 30 pages), **Word** `.docx`, **Excel** `.xlsx` (sheet names and the first rows and columns) and `.txt` / `.csv` / `.md`. **Pictures and scanned PDFs cannot be read** (no OCR): the answer says so. Files that are too big, broken, macro-enabled or zip bombs are skipped without breaking the answer.
* **Where it is used.** The AI summary card and the package analysis (`AiTenderSummaryService`), the Evaluation committee's offers lookup and the Sourcing Officer's compliance helper (`AiDataService.Assist.cs`) and the Evaluation "AI analysis" page (`AiBidAnalysisService`). Always only for offers the user may already see **and** that are eligible (Compliant or Accepted; the officer's compliance helper also reads Submitted and Under review offers). **Drafts and withdrawn offers are never read.** The facts say how many files were read ("Read the text of 3 of 4 attached files").
* **Untrusted text.** File text is supplier content: it sits inside the facts block, the block cannot be closed from inside it, and instructions inside a file are only data.
* **Limits** (all in `.env`, all optional): `AI_READ_DOCUMENTS=true` (false = names only, as before), `AI_DOC_MAX_CHARS=1500` per file, `AI_DOC_MAX_PER_ANSWER=6` files per answer, `AI_DOC_MAX_FILE_MB=5`; a few seconds per file; results are cached 30 minutes and re-read when a file is replaced. A hosted (non-local) model only gets file text when `AI_CHAT_DATA_WITH_API=true`, the same rule as for data.
* **Audit.** `AiDocumentRead` records counts only (never text). The document access history (`DocumentAccessLogs`) is not changed: it keeps its meaning of "a person opened or downloaded the file".

Files: `Services/Ai/AiDocumentReader.cs`, `AiTenderSummaryService.cs`, `AiDataService.cs`, `AiDataService.Assist.cs`, `AiDataService.Package.cs`, `AiBidAnalysisService.cs`, `TenderSystem.csproj` (new package `PdfPig`).

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
* The attached files of the eligible offers are read as short text excerpts (`AI-13`, see below); pictures and scans are listed as not read, and the card says how many files were read.

### Analyze a tender package in the chat (`AI-07`)
The Sourcing Manager and the Top Committee can ask the chat to analyze a package (`can you analyze a review package?`, `summarize the report waiting for me`, `analyze DEMO-TND-002`). `AiDataService.Package.cs` finds the tender (a number typed in the question, or the one tender waiting for them; with several it lists them and asks which), checks the access with the same rules as the AI summary card, and hands the model the same facts: confirmed committee results only, every number calculated by the server, plus the report and, for the Top Committee, the manager's decision. The offers table is shown to the user exactly as calculated. When nothing is waiting the chat says so plainly and mentions what was awarded lately. A help question (`How do I review a tender package?`) is not a data lookup. The evaluation committee keeps its own offers lookup. The files of the offers are read as in `AI-13`.


### Helpers for each role (`AI-08`)
The assistant also helps with the WORK of each role, not only with "where is the page". `AiDataService.Assist.cs` adds one lookup per helper. Each runs a query already limited to the user, returns a table (copied from the database) plus a facts text that starts with a `TASK:` line and ends with a `REMINDER:` line (this is what makes the model do the task instead of describing pages). Everything is read only: nothing is filled in, saved, sent or decided for the user.

| Role | Ask for | What the server gives the model |
|---|---|---|
| Evaluation Committee | "compare the offers of my tender", "which offer is best and why", "what should I ask the supplier?", "explain the scoring criteria" | rubric, requested vs offered items and specifications, price / delivery / validity observations and gaps calculated by the server, the member's OWN scores only. Another member's scores and notes are never read. The confirmed committee results are added only after the member confirmed Ready on all their own forms (same rule as the AI summary card). "Best" is an AI opinion from the offer data, never a score |
| Requester | "help me write the specifications for ..." | a task to write a draft from the user's own words only (`[to fill]` markers, never invented details) and the user's OWN similar earlier requests as wording examples |
| Sourcing Officer | "check the compliance of TND-...", "suggest technical questions for PR-...", "which suppliers can I invite for packaging?" | requirements found in the tender terms (`ComplianceTermsExtractor`) against each offer's fields and files (a field and keyword check, not proof), the request items with weight options that total 80, approved suppliers of a category |
| Supplier | "check if my offer is complete" | a checklist of the supplier's OWN offer (currency, delivery, validity, payment terms, summary, file, price and specification per item, deadline) |

Help questions ("how do I ...") are not intercepted. The files of the offers are read as in `AI-13`.

## Using the AI from your own feature (`AI-15`)

Two services, both registered automatically. Inject them in your controller or service.

**1. The analysis and summary cards get their own model.** Set `AI_INSIGHTS_API_KEY` and `AI_INSIGHTS_API_MODEL` in `.env` (see `.env.example`) and the existing offer analysis (`AiBidAnalysisService`) and tender summary (`AiTenderSummaryService`) use that model, with no change in their callers or pages. Without those keys nothing changes: they use `AI_ANALYSIS_PROVIDER` as before. The chat keeps its own model (`AI_CHAT_PROVIDER`, `AI_API_*`).

**2. `AiGenerationService`: ask the AI to write something from verified facts.**

```csharp
var result = await _ai.GenerateAsync(new AiGenerationRequest(
    AiGenerationService.Insights,      // or AiGenerationService.Email
    "report-summary",                  // a short task name for the audit log (never data)
    instructions,                      // what to write, and the JSON shape, written by you
    facts,                             // the verified data as plain text
    Validate: json => /* return null when fine, or a short reason */ null), ct);

if (!result.Ok) { /* show your page without the AI text (or use your fallback template) */ }
else { var json = result.Text; }
```

It wraps the facts as data, asks for one JSON object, extracts and checks it, retries once with your reason, limits the time and the number of parallel calls, and writes an audit row without the content. It never throws for a model problem: it returns `Ok = false`. `AiFactCheck.UnsupportedNumbers(answer, facts)` tells you which numbers the model wrote that are not in your facts.

**3. `AiAwardEmailService`: the award email, checked by code.** Fill an `AwardEmailRequest` from the database (`Winner` with price, currency and verified terms; `Unsuccessful` with the list of confidential terms that must never appear), call `DraftAsync`, and use the fallback template when `Ok` is false. It sends and stores nothing.

Rules: give the service only verified facts from the database; never put user text in `Instructions`; the AI is an assistant, a person decides.

## Reports: `AiReportService` (`AI-16`)

Inject `AiReportService` to get a checked summary or analysis of a report. You send the verified facts (title, scope, record count, key figures, a facts text of at most 12,000 characters, and honest coverage limits); you get a summary, findings, review points and details, or a clear status (`no-data`, `disabled`, `timeout`, `rejected`...). Every number in the text must exist in your facts or the answer is rejected and retried once. It never throws for a model problem and never touches the database.

```csharp
var result = await _report.AnalyzeAsync(new AiReportFacts(title, scope, count, figures, factsText, limits), ct);
if (result.Ok) { var n = result.Narrative!; /* n.Summary, n.Findings, n.NeedsAttention, n.Details, n.CoverageNotes */ }
```

It uses the same model as the analysis and summary cards (`AI_INSIGHTS_*`). The full agreement with the caller (rules, statuses, timing, who does what) is in `AI-REPORT-CONTRACT.md`. `AI_REPORT_TIMEOUT_SECONDS` limits one attempt (default 90).

## The full-page assistant (`AI-17`)

A page like ChatGPT at `/Assistant` for every employee role (not Suppliers, who keep the small floating chat). Saved chats on the left, the conversation on the right, the answer appears while the model writes it, and a Stop button ends it.

* **Same rules as the floating chat.** `AiChatService.PrepareAsync` does the checks (length, speed limit), reads the role-limited data and builds the prompt. Both chats call it, so a rule is changed in one place.
* **Private history.** `ChatSessions` and `ChatMessages` (`AiChatPageService`). Every query filters by the signed-in user's id: another user, an Admin included, gets "not found". The audit log records that a chat was deleted, never what was written in it. Chats older than `AI_CHAT_HISTORY_DAYS` (default 30) are deleted by `AiChatCleanupWorker`; the user can also delete one chat or all of them.
* **What the assistant remembers** comes from the database (the last `AI_CHAT_MAX_HISTORY` messages), never from the browser.
* **Streaming.** `IAiChatProvider.StreamAsync` (Ollama: JSON lines, hosted: server-sent events; a provider that cannot stream sends the whole answer as one piece). `/Assistant/Send` answers with lines of JSON (`session`, `delta`, `table`, `error`, `done`). Closing the request (Stop) saves the part written so far, marked as stopped. A model that stays silent for `AI_CHAT_TIMEOUT_SECONDS` is given up.
* Settings in `.env.example` (`AI_CHAT_HISTORY_DAYS`, `AI_CHAT_MAX_MESSAGES`, `AI_CHAT_CLEANUP_MINUTES`). Files: `Controllers/AssistantController.cs`, `Views/Assistant/`, `wwwroot/css/assistant.css`, `wwwroot/js/assistant.js`, `Data/Entities/Ai/`.

## Company documents: the knowledge base (`AI-18`)

The full-page assistant can answer from company documents (policies, procedures, guides). An Admin adds them on the page **Assistant documents** (`/Knowledge`, sidebar of the Admin) and chooses which roles may get answers from each one. Suppliers never do. The floating chat does not use them.

**Adding** (`KnowledgeBaseService.AddAsync`): the text is read from the file (`KnowledgeTextReader`: PDF, Word, Excel, txt, md, csv; a scanned PDF has no text), cut into passages of about 300 words that overlap by 50 (`KnowledgeChunker`), each passage is turned into an *embedding* by the local model `AI_KB_EMBED_MODEL` (`AiEmbeddingService`, Ollama `/api/embed`, default `bge-m3`, which understands Arabic and English together) and saved in `KnowledgeChunks`. The file itself is not kept.

**Answering** (`AiChatPageService` calls `KnowledgeBaseService.SearchAsync`): the question is embedded the same way, and compared (cosine similarity) ONLY with the passages of enabled documents whose allowed roles contain the user's role, so a passage the user may not read never reaches the model. The best passages (at most `AI_KB_TOP_K`, each at least `AI_KB_MIN_SCORE` similar) go into the prompt in a block `SOURCES`, numbered `[1]`, `[2]`. The model must answer a company-rule question only from them and cite the number; when nothing matches the prompt says so and the model must say it did not find the answer. The titles of the documents used are shown under the answer and saved with it. Passage text is data, never instructions (same protection as `FACTS`).

* **Privacy:** documents reach the model only when the chat model is local (Ollama), or `AI_CHAT_DATA_WITH_API=true`. Embeddings always run on the local Ollama. The audit log records adding, changing and deleting a document (title only) and that passages were given to the assistant (a count, never the question or the text).
* **Changing the embedding model** makes the old vectors useless (they cannot be compared with the new ones). The page marks such documents "Needs re-index" and they are not used until **Re-index** is pressed.
* **Try a question** on the Admin page shows which passages and scores a question gets as a chosen role. Use it to tune `AI_KB_MIN_SCORE` (with `bge-m3`, matching passages score about 0.55 to 0.75, unrelated ones stay under 0.4).
* All vectors are kept in memory (rebuilt after any change) and compared in C#. This is fine for a few thousand passages; a much larger library would need a vector database.

## Web mode (`AI-19`)

An employee can switch **Web** on for ONE question in the full-page assistant (it switches itself off after the question). The assistant then searches ONLY the websites an Admin allowed (Admin page *Assistant documents*, card *Web mode*; the domain and its subdomains), reads the best pages and answers from them, citing `[W1]`, `[W2]`; the pages are shown as links under the answer. It is OFF until `AI_WEB_ENABLED=true` and `AI_WEB_SEARCH_API_KEY` (a Brave Search API key) are set in `.env`, and the switch is hidden until at least one website is allowed. Suppliers and the floating chat never use it.

**Flow** (`WebAnswerService`): `WebSearchClient` asks the search service with `(site:a.org OR site:b.org) question`; only `https` results whose host is allowed are kept; `SafeWebFetcher` opens up to `AI_WEB_MAX_PAGES` of them; `HtmlTextExtractor` makes plain text; the passages most similar to the question (the same `bge-m3` embeddings as the documents) go into a `WEB` block of the prompt. A page that cannot be opened still helps through the short text the search service showed. Results are cached 10 minutes; each user may ask `AI_WEB_MAX_PER_MINUTE` web questions per minute.

**Safety** (what makes opening addresses chosen by a search engine safe):
* Only addresses on the allowed list are opened, also after every redirect (followed by hand, at most 3). https only, no user name in the address, no unusual port, an IP address is never accepted.
* The connection itself is refused when the host resolves to a private, loopback, link-local, carrier-NAT, multicast or cloud-metadata address (`SafeWebFetcher.IsPublicAddress`), checked at connect time so a DNS trick cannot get past an earlier check. No proxy is used.
* A time limit and a size limit per page; only HTML or plain text is read; the regular expressions have a time limit.
* Text from a website is data, never instructions (the `WEB` block rules, delimiters removed). It is never presented as company data and never overrides `SOURCES` or `FACTS`.
* The search key is sent only in a header and never logged or shown. The audit log records that web mode was used and which sites it read, never the question.

**Privacy:** the text of the question is sent to the search service when a user switches Web on, and the page says so. Nothing else about the company is sent. The page text goes to the chat model like any other passage.

## Quality tools (`AI-20`)

* **Thumbs up / down** under every answer of the full-page assistant (`POST /Assistant/Rate`, stored as `ChatMessages.Feedback`). Only the owner of a chat can rate its answers. The Admin page *Assistant documents* shows COUNTS only (up, down, last 30 days, down on answers that used documents or web pages): chats stay private, nobody can read what users asked.
* **Test questions** (`GoldenQuestionService`, table `GoldenQuestions`, Admin page card *Test questions*). A question is asked AS a role. The search must find the expected document, or NOTHING when no document is expected (which proves a role cannot reach a document). Optionally the answer must also contain given words (the model answers from the passages found, at most 15 answers per run). Run them after changing documents, roles, `AI_KB_*` settings or a model; each shows Passed or Failed with the reason. The text of an answer is never stored.
* **Backup model** (`AI_CHAT_FALLBACK_OLLAMA_MODEL`): when the main model gives NO answer at all (Ollama not running, model not installed, too slow), a local backup model tries once, and the user is told that the backup answered. Only a local model is allowed as backup, because the prompt can hold company data. If some text was already written, there is no switch.
* **Voice typing** on the full page uses the same `ai-voice.js` as the floating chat: the local Whisper model when `VOICE_MODEL_PATH` exists, otherwise the browser's speech recognition. The text goes into the box; nothing is sent until the user presses Send.

## Lookup planner and the record on the screen (`AI-21`)

**The problem it fixes.** The lookups of `AI-03` were chosen by keywords, so a natural question ("which tenders close this week?", "who logged in recently?", "how many requests are approved?") often matched no lookup, or the wrong filter, and the assistant said it had no data.

**How it works now.** `AiLookupPlanner` shows the model the list of lookups its user's role has (`AiLookupCatalog`) and the question, and the model answers with a small JSON choice: `{"tool":"tenders","args":{"closing_in_days":"7"}}`. The SERVER runs that lookup (`AiDataService.RunToolAsync`, file `AiDataService.Tools.cs`). The model never writes a query and never sees data at this step.

* **Tools:** `requests`, `tenders`, `offers`, `counts`, `deadlines`, `invitations`, `evaluation_progress`, `votes`, `audit`, `users`, `suppliers`, each with filters (status, text, days, sort, limit...). Plus two special answers: `classic` (one specific request or tender, offers comparison, reports, drafting help: the old keyword lookups of `AI-03` and `AI-08` run as before) and `none` (a how-to question: only the small overview).
* **Scope is still in the query.** Every tool starts from the user's own scope inside the query (`TendersFor`, `BidsFor`, `RequestsFor`): a supplier only their own offers, a requester only their own requests, a committee member only the tenders they sit on, the Manager and the Top Committee only their statuses, drafts never. A tool a role does not have is refused even if the model names it. Counts are counted by the system, filters that were applied are written into the facts, scores of other members and who voted what are never returned.
* **Seat-holders.** A Requester or Sourcing Manager who also sits on an evaluation committee gets the committee tools (`tenders`, `offers`, `deadlines`, `evaluation_progress`) and the committee keyword lookups too.
* **Fallback.** If the planner is off (`AI_PLANNER=off`), too slow (`AI_PLANNER_TIMEOUT_SECONDS`) or gives an unusable answer, the keyword matching of `AI-03` is used, exactly as before. One extra short model call is made per question.
* **The record on the screen.** The full-screen assistant opens in a new tab, so it reads the page the user came from (the browser's referrer, same site only). `AiPageContext` understands the detail pages of tenders, requests, reports and bids (to teach it another page add ONE line to its table). The address is only turned into a request or tender NUMBER and checked with the user's own scope (`VisibleRecordLabelAsync`); a made-up address can never reveal a record they may not see. The page shows "Talking about: Tender ..." (it can be closed), and "this tender" or "it" in a question then means that record.
* **Add a lookup:** add its description to `AiLookupCatalog` (and an example), a method and a `case` in `AiDataService.Tools.cs` that starts from `TendersFor` / `BidsFor` / `RequestsFor`, and a check in the e2e test.
