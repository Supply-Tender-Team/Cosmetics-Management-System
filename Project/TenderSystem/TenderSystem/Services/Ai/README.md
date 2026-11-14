# AI assistant chat (`AI-02` chat, `AI-03` data access)

A floating chat button on every dashboard page. The answers depend on the signed-in user's **role** (Admin, Requester, Sourcing Officer, Evaluation Committee, Sourcing Manager, Top Committee, Supplier) and on the page the user is looking at. Since `AI-03` it also reads **real data**, limited to what that role may see (see "How it reads data").

## Who answers: one switch in `.env`

```text
# local model on this computer (default). Needs Ollama running and the model installed: ollama pull <name>
AI_CHAT_PROVIDER=ollama
OLLAMA_BASE_URL=http://localhost:11434
OLLAMA_MODEL=gemma4:12b

# OR a hosted API that speaks the OpenAI "chat/completions" format (OpenAI, Groq, OpenRouter, Gemini's compatible endpoint...)
# AI_CHAT_PROVIDER=openai
# AI_API_BASE_URL=https://api.openai.com/v1
# AI_API_KEY=            (secret, only in your own .env)
# AI_API_MODEL=gpt-4o-mini

# OR no assistant at all: the button is hidden
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

## Phase 2 (planned, `AI-04`): command center

Not built. The idea: an Admin tells the assistant to *do* something ("email all employees of Packaging ..."). The model would only **propose** the action; the chat shows it with a **Confirm** button; the server re-checks the role and runs it through the existing services (`EmailService`, `NotificationService`, `AuditService`), always audited. No model output may ever run an action by itself. It will live in a separate `AiActionService`, so the read-only lookups above stay read only.

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

## Not the same as `AI-01`

`AiBidAnalysisService` (bid analysis for the Evaluation committee, saved in `AiBidAnalyses`) is a separate service. It can reuse `OLLAMA_*` and the provider classes here.
