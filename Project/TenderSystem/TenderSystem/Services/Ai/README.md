# AI assistant chat (`AI-02` chat, `AI-03` data access)

A floating chat button on every dashboard page. The answers depend on the signed-in user's **role** (Admin, Requester, Sourcing Officer, Evaluation Committee, Sourcing Manager, Top Committee, Supplier) and on the page the user is looking at. Since `AI-03` it also reads **real data**, limited to what that role may see (see "How it reads data").

## How a question flows (start here)

Both chats (the round button and the full page `/Assistant`) answer in the same steps, written in `QuestionService`:

1. **`CheckQuestion`**: is the assistant on, is the question not empty, not too long, not asked too often (answers 503, 400 and 429).
2. **`StartAsync`**: builds one `ChatContext` and finds the record the person is looking at (`AiPageContext`).
3. **`AiLookupPlanner`**: one short model call chooses a tool as JSON from `AiLookupCatalog`. The model only sees the tools of the user's role and never sees data in this step.
4. **`RoleDataService`**: runs the chosen tool inside the user's own scope (`TendersFor`, `BidsFor`, `RequestsFor`). It is a list tool, a record card (`record`), a help task (`help`) or the small overview.
5. **`AiChatPrompts`**: the rules, the pages of the role and the rows as a `FACTS` block (the full page adds `SOURCES` and `WEB`).
6. **The model writes** (`OllamaChatProvider` or `OpenAiChatProvider`); `AiThinkFilter` removes any reasoning text.
7. **The browser draws it** (`chat-core.js` with `ai-chat.js` or `assistant.js`). The table under an answer comes from the database rows, never from the model.

Only the full-page chat (`AssistantChatService`) adds saved chats, company documents, web mode and streaming. Writers of cards, reports and e-mails are separate: they all go through `AiGenerationService` (see "Using the AI from your own feature").

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

## How it reads data (`AI-03`, rebuilt in `AI-R2` to `AI-R4`)

The model never queries anything. For each question the server (`RoleDataService`) runs the tool the planner chose, with a query that is **already limited to the user inside the query**, and gives the rows to the model as a `FACTS` block. The same rows are also sent to the browser as a **table** (shown right under the answer, copied from the database, never retyped by the model).

| Role | Tools in its list (`AiLookupCatalog.For`) | Limited to |
|---|---|---|
| Admin | `counts`, `audit`, `users`, `suppliers`, `contact_messages` | Admin only. No passwords, IP addresses, message bodies or e-mails. No record cards |
| Requester | `requests`, `counts`, `deadlines`, `help` (word a request) | `RequestedByUserId == me`. Never sees tenders |
| Supplier | `tenders`, `offers`, `invitations`, `counts`, `deadlines`, `help` (check my offer) | their own `SupplierProfile`; never another supplier's offer or price |
| Sourcing Officer | `requests`, `tenders`, `offers`, `counts`, `deadlines`, `suppliers`, `help` (compliance, tender questions, shortlist) | no drafts of other people |
| Evaluation Committee | `tenders`, `offers`, `counts`, `deadlines`, `evaluation_progress`, `help` (compare offers, supplier questions, rubric) | tenders where they are in `CommitteeMembers`; only their own scores |
| Sourcing Manager | `tenders`, `offers`, `counts`, `help` (tender package) | tenders that reached the manager review |
| Top Committee | `tenders`, `offers`, `counts`, `votes`, `help` (tender package) | tenders waiting for or past the final decision; vote counts only |
| Everyone with tools | `notifications` | `UserId == me` |

Two more answers are not tools of a list: **`record`** (a request or tender number gives its record card, see `RoleDataService.Cards.cs`: the record and the records linked to it, one step deep, each read through the same scope methods) and **`none`** (a how-to question: only the small overview). A number typed in the question is only a filter inside the scoped query; if the user may not see it the answer is "not found", never "it exists but...".

Examples: *"which tenders close this week?"* (`tenders` with `closing_in_days`), *"tell me about TND-005"* (`record`), *"check the compliance of TND-002"* (`help` with `task=compliance_checklist`), *"which of my requests are approved?"* (`requests`).

Rules: at most 25 rows (10 by default), long text is cut, text written by users is wrapped as data and cannot close the `FACTS` block, every lookup is written to the audit log as `AiDataAccess` (lookup name and row count, **never the question**), and with `AI_CHAT_PROVIDER=openai` no database data is sent unless `AI_CHAT_DATA_WITH_API=true`.

**Adding a lookup** (for example when Sourcing or Evaluation get new screens): describe it in `AiLookupCatalog` (a tool definition, the role's list and one example), add a method and a `case` in `RoleDataService.Tools.cs` that starts from `TendersFor`, `BidsFor` or `RequestsFor`, and add tests (`AiLookupCatalogTests`, a who-may-see-what test like those in `RecordCardTests`, and a question in `RoutingScoreTests`). Keep it read only.

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

Files: `Services/Ai/AiFollowUp.cs`, `QuestionService.cs` (`AiChatTurn.Numbers`), `AiChatPrompts.cs`, `Services/Ai/AiTableExcelWriter.cs`, `Controllers/AiChatController.cs`, `wwwroot/js/ai-chat.js`, `wwwroot/css/ai-chat.css`.

### Live nudges and voice typing (`AI-12`)

* **The cloud above the round button only says real things.** `AiNudgeService` + `GET /AiChat/Nudges` return up to 3 short items made from **counts of the signed-in user's own work**: Admin (suppliers waiting for approval, unread contact messages), Requester (requests returned to them), Sourcing Officer (offers waiting for compliance review, requests waiting for review, reports ready to send to the manager), Evaluation member (tenders whose brief was sent and whose Ready is not confirmed), Sourcing Manager (reports waiting for their review), Top Committee (tenders waiting for their vote), Supplier (invitations waiting for an answer, draft offers) and everybody (clarifications waiting for an answer). No model call, nothing sent anywhere, cached 60 seconds per user, and an empty list when `AI_CHAT_DATA=false`. **Nothing waiting = no cloud, no dot, no hop.** Clicking an item runs the matching question in the chat (for example "Check the compliance of the waiting offers") or opens the page. When the waiting work is NEW or CHANGED the cloud and the pulsing dot appear again (also on a page that stays open: the browser asks again every 2 minutes and when the tab comes back to the front, so nobody has to sign out and in). The same unchanged work only gets a reminder twice, 10 minutes apart, and the X dismisses only that work: new work speaks again. (Before this fix the browser kept "3 clouds per session, dismissed for the whole session" in the tab, so work that arrived later was never announced.)
* **Voice typing.** A mic button next to Send (Ask tab and Command tab). Two modes, chosen by the server (`data-voice` on the widget): **whisper** when the file named in `VOICE_MODEL_PATH` exists (the browser records up to 30 seconds, converts it to 16 kHz mono WAV and posts it to `POST /AiChat/Transcribe`, which uses the same local `VoiceTranscriptionService` as the Admin voice search; 6 recordings per minute per user; nothing leaves the company), otherwise **browser** (the browser's own speech recognition, with a one-time privacy notice because the browser may send the audio to its vendor; an EN / AR switch). The recognised text goes **into the input box for the user to read and send**: it is never sent by itself and never stored. The mic is hidden when the browser cannot do either.

Files: `Services/Ai/AiNudgeService.cs`, `Controllers/AiChatController.cs` (`Nudges`, `Transcribe`), `wwwroot/js/ai-voice.js`, `Views/Shared/_AiChat.cshtml`, `wwwroot/js/ai-chat.js`, `wwwroot/css/ai-chat.css`.

### Reading the attached offer documents (`AI-13`)

* **What is read.** `AiDocumentReader` opens the files of an offer through the same file storage as the protected download and returns a short text excerpt: **PDF** (PdfPig, the text layer, first 30 pages), **Word** `.docx`, **Excel** `.xlsx` (sheet names and the first rows and columns) and `.txt` / `.csv` / `.md`. **Pictures and scanned PDFs cannot be read** (no OCR): the answer says so. Files that are too big, broken, macro-enabled or zip bombs are skipped without breaking the answer.
* **Where it is used.** The AI summary card and the package analysis (`AiTenderSummaryService`), the `help` tasks `compare_offers` and `compliance_checklist` (`RoleDataService.Assist.cs`) and the Evaluation "AI analysis" page (`AiBidAnalysisService`). Always only for offers the user may already see **and** that are eligible (Compliant or Accepted; the officer's compliance helper also reads Submitted and Under review offers). **Drafts and withdrawn offers are never read.** The facts say how many files were read ("Read the text of 3 of 4 attached files").
* **Untrusted text.** File text is supplier content: it sits inside the facts block, the block cannot be closed from inside it, and instructions inside a file are only data.
* **Limits** (all in `.env`, all optional): `AI_READ_DOCUMENTS=true` (false = names only, as before), `AI_DOC_MAX_CHARS=1500` per file, `AI_DOC_MAX_PER_ANSWER=6` files per answer, `AI_DOC_MAX_FILE_MB=5`; a few seconds per file; results are cached 30 minutes and re-read when a file is replaced. A hosted (non-local) model only gets file text when `AI_CHAT_DATA_WITH_API=true`, the same rule as for data.
* **Audit.** `AiDocumentRead` records counts only (never text). The document access history (`DocumentAccessLogs`) is not changed: it keeps its meaning of "a person opened or downloaded the file".

Files: `Services/Ai/AiDocumentReader.cs`, `AiTenderSummaryService.cs`, `RoleDataService.cs`, `RoleDataService.Assist.cs`, `RoleDataService.Package.cs`, `AiBidAnalysisService.cs`, `TenderSystem.csproj` (new package `PdfPig`).

## Files

| File | What it does |
|---|---|
| `Views/Shared/_AiChat.cshtml` | the round-button widget (one `<partial name="_AiChat" />` in `_DashboardLayout.cshtml`) |
| `wwwroot/js/ai-chat.js`, `wwwroot/css/ai-chat.css` | behaviour and look of the round-button chat |
| `wwwroot/js/chat-core.js` | what both chats share: safe text formatting, copy, the table under an answer, Excel export (each chat passes its own CSS class names) |
| `Controllers/AiChatController.cs` | `POST /AiChat/Ask`, `Nudges`, `Transcribe`, `ExportTable` (signed-in users only, anti-forgery token) |
| `Controllers/AssistantController.cs`, `Services/Ai/AssistantChatService.cs` | the full-page assistant (`/Assistant`): saved chats, streaming, documents, web |
| `Services/Ai/QuestionService.cs` | the steps of one question (see "How a question flows"), the per-user rate limit, the provider |
| `Services/Ai/AiLookupPlanner.cs`, `AiLookupCatalog.cs` | the planner call, the tools of each role and their examples |
| `Services/Ai/RoleDataService.cs` | the entry: command check, record card, overview, shared helpers |
| `Services/Ai/RoleDataService.Tools.cs` | the list tools and the scope methods `TendersFor`, `BidsFor`, `RequestsFor` |
| `Services/Ai/RoleDataService.Cards.cs` | record cards (a number gives the record and its linked records) |
| `Services/Ai/RoleDataService.Assist.cs`, `RoleDataService.Package.cs` | the `help` tasks of each role, and the tender package |
| `Services/Ai/AiChatPrompts.cs` | the rules and the per-role description of pages. **Edit this file to change what the assistant knows.** |
| `Services/Ai/OllamaChatProvider.cs`, `OpenAiChatProvider.cs` | the two ways to reach a model (`IAiChatProvider`: `CompleteAsync` and `StreamAsync`) |
| `Services/Ai/AiGenerationService.cs` | the one door for writers of cards, reports and e-mails |

## Safety rules built in

* The role comes from the sign-in on the server, never from the browser. The page path from the browser is accepted only if it is a plain path.
* Earlier messages sent by the browser are trusted only as `user` / `assistant` text, so nobody can inject a `system` message.
* The assistant has no database access of its own. Data reaches it only through `RoleDataService`, which loads only what the signed-in user may see, so it cannot leak one supplier's data to another.
* The round-button chat stores nothing on the server: its conversation lives in the browser tab only. The full-page assistant saves chats in `ChatSessions` and `ChatMessages`, private to their owner (see `AI-17`). Neither writes the question to the log: only errors are logged, and data lookups are audited without the question.
* The widget says "AI assistant only", and the prompt forbids it from deciding or approving anything.

## Not the same as the chat

The bid analysis (`AI-01`, below) is a separate service. It reuses `OLLAMA_*` and the provider classes of the chat, and asks the model through `AiGenerationService`.

## Bid analysis (`AI-01`)

`AiBidAnalysisService` is a separate feature from the chat. For every submitted (not draft) offer of a tender it sends the tender's requirements and ONE offer to the model and asks for a fixed JSON answer:

```json
{"price": 2050, "delivery_days": 7, "missing_documents": ["ISO certificate"], "strengths": ["..."], "weaknesses": ["..."], "recommendation": "Recommended | Acceptable | Not recommended", "reason": "one sentence"}
```

* It is saved in `AiBidAnalyses` (one row per offer per run: the request and response JSON, the extracted price and delivery, missing files, strengths, weaknesses, the recommendation). Running it again adds new rows; `GetLatestAsync` returns the newest one per offer.
* **Strict:** the answer must have the shape above (`Parse`, passed to `AiGenerationService` as `Validate`). If not, the engine asks the model once more; if it still fails, nothing is saved for that offer and the run stops with a clear message. A negative price or an absurd delivery time is ignored (the offer's own values are used). Lists are cut to 8 points.
* **Only committee members of that tender** can run it or read the results (checked in the service). One analysis of a tender runs at a time.
* **It uses the LOCAL model by default** (`AI_ANALYSIS_PROVIDER=ollama`, same `OLLAMA_*` settings as the chat), because the data holds prices and supplier documents. `openai` sends them to a hosted API (uses `AI_API_*`), `off` disables it. `AI_ANALYSIS_TIMEOUT_SECONDS=180` is the time allowed for one offer.
* Supplier text (technical summary, item specifications) is wrapped as data and cut, so it cannot give the model instructions. The model only **assists**: always show `AiBidAnalysisService.Disclaimer` next to a result; the committee scores and decides.
* The text of the files of an offer that is Compliant or Accepted is read as short excerpts (`AI-13`) and added to the facts. Pictures and scans are not read.

Calling it (for the Evaluation AI page, `EVL-04`): see the hand-off to Marwa in `TASKS.md`.

## Tender summary (`AI-06`): one summary of a whole tender for the people who decide
`AiTenderSummaryService` writes a short summary of ONE tender with all its eligible offers side by side: an overview, per offer strengths, weaknesses and the main risk, points to check, and a labelled "leaning" (a suggestion, never a decision). It is shown as a card (`<partial name="_AiTenderSummary" model="tenderId" />`) on three pages: Evaluation AI page, Manager review page, Top Committee review package. It never fills scores, the report or a decision.

* **Who sees it** (decided in the service, not only in the controller): Top Committee for tenders waiting for its decision; Sourcing Manager for tenders it sits on as the manager seat, from the manager review on; the Evaluation Committee only **after the member confirmed Ready on every eligible offer** (or the report exists), so the suggestion cannot influence the scoring. Everybody else gets "denied" and the card stays hidden.
* **The code calculates, the model only writes.** Prices, technical and financial points, totals and the ranking are computed by the server (same rule as the evaluation report) and handed to the model as facts. Only **confirmed (Ready)** forms count; drafts, NULL scores and Draft offers never reach the model. While some forms are unconfirmed the points are marked provisional, there is no ranking, and the server blanks the model's leaning.
* **Supplier text is data.** It sits inside `<<<DATA ... DATA>>>` (the block cannot be closed early), the answer must be the exact JSON shape (one retry), and a supplier name the model invents is dropped.
* The summary is **saved** (`AiTenderSummaries`, one row per tender and audience) with a fingerprint of the facts; when the data changes later the card says **Outdated**. Limits: 6 summaries per user per hour, one at a time per tender and audience, `AI_SUMMARY_TIMEOUT_SECONDS` (default 300). Audited as `AiSummary` (no text in the log).
* The attached files of the eligible offers are read as short text excerpts (`AI-13`, see below); pictures and scans are listed as not read, and the card says how many files were read.

### Analyze a tender package in the chat (`AI-07`)
The Sourcing Manager and the Top Committee can ask the chat to analyze a package (`can you analyze a review package?`, `summarize the report waiting for me`, `analyze DEMO-TND-002`). The planner answers with the `help` tool and the task `tender_package`; `RoleDataService.Package.cs` then finds the tender (a number typed in the question, or the one tender waiting for them; with several it lists them and asks which), checks the access with the same rules as the AI summary card, and hands the model the same facts: confirmed committee results only, every number calculated by the server, plus the report and, for the Top Committee, the manager's decision. The offers table is shown to the user exactly as calculated. When nothing is waiting the chat says so plainly and mentions what was awarded lately. A how-to question (`How do I review a tender package?`) is answered by the planner with `none`, so it is not a data lookup. The evaluation committee uses the task `compare_offers` instead. The files of the offers are read as in `AI-13`.


### Helpers for each role (`AI-08`, since `AI-R4` the `help` tool)
The assistant also helps with the WORK of each role, not only with "where is the page". The planner answers with the tool `help` and a **task name**; `RoleDataService.HelpToolAsync` (in `RoleDataService.Assist.cs`) runs the task only when the role has it, so a task of another role gives nothing. Each task runs a query already limited to the user, returns a table (copied from the database) plus a facts text that starts with a `TASK:` line and ends with a `REMINDER:` line (this is what makes the model do the task instead of describing pages). Everything is read only: nothing is filled in, saved, sent or decided for the user. There are no word lists any more: the model chooses the task, and if the planner is off the overview is shown.

| Role (task names) | Ask for | What the server gives the model |
|---|---|---|
| Evaluation Committee (`compare_offers`, `supplier_questions`, `scoring_rubric`) | "compare the offers of my tender", "which offer is best and why", "what should I ask the supplier?", "explain the scoring criteria" | rubric, requested vs offered items and specifications, price / delivery / validity observations and gaps calculated by the server, the member's OWN scores only. Another member's scores and notes are never read. The confirmed committee results are added only after the member confirmed Ready on all their own forms (same rule as the AI summary card). "Best" is an AI opinion from the offer data, never a score |
| Requester (`word_request`) | "help me write the specifications for ..." | a task to write a draft from the user's own words only (`[to fill]` markers, never invented details) and the user's OWN similar earlier requests as wording examples |
| Sourcing Officer (`compliance_checklist`, `tender_questions`, `supplier_shortlist`) | "check the compliance of TND-...", "suggest technical questions for PR-...", "which suppliers can I invite for packaging?" | requirements found in the tender terms (`ComplianceTermsExtractor`) against each offer's fields and files (a field and keyword check, not proof), the request items with weight options that total 80, approved suppliers of a category |
| Supplier (`offer_check`) | "check if my offer is complete" | a checklist of the supplier's OWN offer (currency, delivery, validity, payment terms, summary, file, price and specification per item, deadline) |

How-to questions ("how do I ...") get the planner answer `none`, so they are not turned into tasks. The Manager and the Top Committee have the task `tender_package` (see `AI-07`). The files of the offers are read as in `AI-13`.

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

**Every writer uses this one door (AI-R6).** The tender summary (`AiTenderSummaryService`, task `tender-summary`), the bid analysis (`AiBidAnalysisService`, task `bid-analysis`), the Admin report, the offer advisor and the award email all call `GenerateAsync`, so the JSON check, the one retry, the time limit and the audit row exist in ONE place. A writer keeps only its own prompt, its own facts and its own `Parse` (the exact shape it accepts, passed as `Validate`). The Admin command center (`AiActionService`) and the lookup planner keep their own small model calls on purpose: the first is security-critical, the second answers in a different, tiny shape. `AiTenderSummaryService.BuildFactsAsync` is one step per kind of fact (read the database, calculate the points, write each part of the text); `TenderPackageGoldenTests` fails if the facts text changes by even one character.

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

* **Same rules as the floating chat.** `AssistantChatService` calls `QuestionService` (`CheckQuestion`, `StartAsync`, `BuildMessages`) for the checks, the role-limited data and the prompt, so a rule is changed in one place. Its own `SendAsync` is a list of small steps: open the chat, load the history, add document passages, add web passages, save the question, stream the answer (with a backup model), save the answer.
* **Private history.** `ChatSessions` and `ChatMessages` (`AssistantChatService`). Every query filters by the signed-in user's id: another user, an Admin included, gets "not found". The audit log records that a chat was deleted, never what was written in it. Chats older than `AI_CHAT_HISTORY_DAYS` (default 30) are deleted by `AiChatCleanupWorker`; the user can also delete one chat or all of them.
* **What the assistant remembers** comes from the database (the last `AI_CHAT_MAX_HISTORY` messages), never from the browser.
* **Streaming.** `IAiChatProvider.StreamAsync` (Ollama: JSON lines, hosted: server-sent events; a provider that cannot stream sends the whole answer as one piece). `/Assistant/Send` answers with lines of JSON (`session`, `context`, `notice`, `sources`, `delta`, `table`, `error`, `done`). Closing the request (Stop) saves the part written so far, marked as stopped. A model that stays silent for `AI_CHAT_TIMEOUT_SECONDS` is given up.
* Settings in `.env.example` (`AI_CHAT_HISTORY_DAYS`, `AI_CHAT_MAX_MESSAGES`, `AI_CHAT_CLEANUP_MINUTES`). Files: `Controllers/AssistantController.cs`, `Views/Assistant/`, `wwwroot/css/assistant.css`, `wwwroot/js/assistant.js`, `Data/Entities/Ai/`.

## Company documents: the knowledge base (`AI-18`)

The full-page assistant can answer from company documents (policies, procedures, guides). An Admin adds them on the page **Assistant documents** (`/Knowledge`, sidebar of the Admin) and chooses which roles may get answers from each one. Suppliers never do. The floating chat does not use them.

**Adding** (`KnowledgeBaseService.AddAsync`): the text is read from the file (`KnowledgeTextReader`: PDF, Word, Excel, txt, md, csv; a scanned PDF has no text), cut into passages of about 300 words that overlap by 50 (`KnowledgeChunker`), each passage is turned into an *embedding* by the local model `AI_KB_EMBED_MODEL` (`AiEmbeddingService`, Ollama `/api/embed`, default `bge-m3`, which understands Arabic and English together) and saved in `KnowledgeChunks`. The file itself is not kept.

**Answering** (`AssistantChatService` calls `KnowledgeBaseService.SearchAsync`): the question is embedded the same way, and compared (cosine similarity) ONLY with the passages of enabled documents whose allowed roles contain the user's role, so a passage the user may not read never reaches the model. The best passages (at most `AI_KB_TOP_K`, each at least `AI_KB_MIN_SCORE` similar) go into the prompt in a block `SOURCES`, numbered `[1]`, `[2]`. The model must answer a company-rule question only from them and cite the number; when nothing matches the prompt says so and the model must say it did not find the answer. The titles of the documents used are shown under the answer and saved with it. Passage text is data, never instructions (same protection as `FACTS`).

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

**How it works now.** `AiLookupPlanner` shows the model the list of lookups its user's role has (`AiLookupCatalog`) and the question, and the model answers with a small JSON choice: `{"tool":"tenders","args":{"closing_in_days":"7"}}`. The SERVER runs that lookup (`RoleDataService.RunToolAsync`, file `RoleDataService.Tools.cs`). The model never writes a query and never sees data at this step.

* **Tools:** `requests`, `tenders`, `offers`, `counts`, `deadlines`, `invitations`, `evaluation_progress`, `votes`, `audit`, `users`, `suppliers`, `notifications` (every role) and `contact_messages` (Admin), each with filters (status, text, days, sort, limit...). Plus the `help` tool (writing and checking help for the work of one role, chosen by a task name: `compare_offers`, `supplier_questions`, `scoring_rubric`, `word_request`, `compliance_checklist`, `tender_questions`, `supplier_shortlist`, `tender_package`, `offer_check`; code in `RoleDataService.Assist.cs` and `RoleDataService.Package.cs`) and two special answers: `record` (ONE request or tender by its number: its record card, see `RoleDataService.Cards.cs`: the record plus the records linked to it, read only through the role scope methods) and `none` (a how-to question: only the small overview). No keyword lists are left: if the planner is off or fails, the overview is shown (a record number in the question still gives its card).
* **Scope is still in the query.** Every tool starts from the user's own scope inside the query (`TendersFor`, `BidsFor`, `RequestsFor`): a supplier only their own offers, a requester only their own requests, a committee member only the tenders they sit on, the Manager and the Top Committee only their statuses, drafts never. A tool a role does not have is refused even if the model names it. Counts are counted by the system, filters that were applied are written into the facts, scores of other members and who voted what are never returned.
* **Seat-holders.** A Requester or Sourcing Manager who also sits on an evaluation committee gets the committee tools (`tenders`, `offers`, `deadlines`, `evaluation_progress`) and the committee lookup by tender number too.
* **Fallback.** If the planner is off (`AI_PLANNER=off`), too slow (`AI_PLANNER_TIMEOUT_SECONDS`) or gives an unusable answer, the lists are NOT guessed from keywords any more (AI-R2a): the assistant shows the small overview, and a record number typed in the question is still read. One extra short model call is made per question.
* **The record on the screen.** The full-screen assistant opens in a new tab, so it reads the page the user came from (the browser's referrer, same site only). `AiPageContext` understands the detail pages of tenders, requests, reports and bids (to teach it another page add ONE line to its table). The address is only turned into a request or tender NUMBER and checked with the user's own scope (`VisibleRecordLabelAsync`); a made-up address can never reveal a record they may not see. The page shows "Talking about: Tender ..." (it can be closed), and "this tender" or "it" in a question then means that record.
* **Add a lookup:** add its description to `AiLookupCatalog` (and an example), a method and a `case` in `RoleDataService.Tools.cs` that starts from `TendersFor` / `BidsFor` / `RequestsFor`, and tests (see "Tests" below).

## Offer advisor for the Evaluation committee (`AI-22`)

`AiOfferAdvisorService` gives a committee member **advice** about the eligible offers of one tender: a suggested offer (or an honest "no suggestion"), reasons, things to verify, alternatives and coverage notes. It never scores, votes or awards, and it never reads the database: the Evaluation area builds the facts (`AiOfferAdvisorRequest`) and the service only asks the model and checks the answer.

* **Same model as the Admin reports** (`AI_INSIGHTS_*` or the shared `AI_ANALYSIS_PROVIDER` / `AI_API_*`), through `AiGenerationService`. Settings: `AI_OFFER_ADVISOR_TIMEOUT_SECONDS` (240: the hosted reasoning model answered in 51 s to over 2 minutes on the same demo tender), `AI_OFFER_ADVISOR_MAX_PER_MINUTE` (3).
* **Blind comparison.** The model sees `[B12]`, `[B15]` instead of supplier names; the server puts the names back after the checks. Prices are compared per currency, in C#.
* **Checks in code.** Suggested and alternative offers must be among the eligible ones, every number must exist in the facts, no HTML, links, award or confidence words, one retry when the answer breaks a rule.
* **Evidence ready.** Each offer can carry passages read from its files (`AiOfferEvidence`); the tender document index (see `AI-TENDER-DOSSIER-PLAN.md`) will fill them later without changing the signature.
* `AiOfferAdvisorService.FingerprintOf(request)` tells, without a model call, whether the offers changed since a saved advice.

The full agreement (statuses, examples, what Evaluation must still build) is in `AI-OFFER-ADVISOR-CONTRACT.md`.

## Fast models and saved results (`AI-24`)

* **The model stays ready.** Every Ollama request now carries `keep_alive` (`OLLAMA_KEEP_ALIVE`, default 24h; `-1` = as long as Ollama runs) and `AiWarmupService` loads the chat / analysis model in the background when the application starts (`AI_WARMUP=false` switches it off). Before, Ollama unloaded an idle model after 5 minutes and the next answer waited 3 to 30 seconds (longer when the graphics card was busy).
* **Award emails.** The email profile has its own settings: `AI_EMAIL_PROVIDER=ollama`, `AI_EMAIL_OLLAMA_MODEL=qwen3.5:4b`, `AI_EMAIL_TIMEOUT_SECONDS` (default 25, was 60). When the model is late, fails or is rejected twice, the ready template (`AwardEmailFallbackTemplates`) is used, so the email is never held back by the AI. The reason is written in the audit row of `AiGenerated`. Measured on a scratch database: the award decision to the email in the queue in 6 to 7 seconds with the 4B model.
* **Saved results.** The Admin AI report and the Evaluation offer advisor keep the last result of each user in `AiSavedResults` (one row per user, kind and key; generating again replaces it). A refresh, a later visit or a restart shows it again without calling the model. The advisor card says "outdated" when the offers changed (fingerprint); the report page shows the saved report of the same report type with its scope and time, and the PDF link keeps working. The tender summary (`AiTenderSummaries`) and the bid analysis (`AiBidAnalyses`) were already saved.

## Tests

The xUnit project is `Project/TenderSystem/TenderSystem.Tests`. Run it from `Project/TenderSystem` with `dotnet test`. If the site is running and locks the build output, build to another folder: `dotnet test -p:UseAppHost=false -p:OutDir=<folder>/`.

| Tests | What they protect |
|---|---|
| `RecordCardTests` | who may see what in a record card (12 cases on an in-memory database: drafts, committee seats, suppliers, requesters, Admin) |
| `HelpToolTests` | a task works only for the role that has it, and keeps the role scope |
| `AiLookupPlannerTests`, `AiLookupCatalogTests` | reading the model's JSON choice, and the tools of each role |
| `QuestionServiceTests` | the first checks (503, 400, 429) |
| `WriterEngineTests`, `SummaryTestKit` | the real tender summary and bid analysis against a fake model: good answer saved, bad answer retried once, two bad answers save nothing |
| `TenderPackageGoldenTests` | the facts text of a tender package must not change by accident. To accept an intended change run once with `UPDATE_GOLDEN=1` and read the difference in git |
| `AiFactCheckTests`, `AiFollowUpTests`, `AiPageContextTests`, `AiThinkFilterTests`, `KnowledgeChunkerTests`, `SafeWebFetcherTests`, `RateLimiterTests`, `EnvFileCheckTests` | the small pure pieces |
| `RoutingScoreTests` | opt-in, needs Ollama: asks the real planner 42 questions and reports how many tools it chose correctly. Run with `AI_ROUTING_TEST=1` after changing a tool description, an example or the model |
