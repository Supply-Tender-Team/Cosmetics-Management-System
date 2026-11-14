# AI assistant chat (`AI-02`)

A floating chat button on every dashboard page. The answers depend on the signed-in user's **role** (Admin, Requester, Sourcing Officer, Evaluation Committee, Sourcing Manager, Top Committee, Supplier) and on the page the user is looking at.

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
```

Restart the app after changing `.env`. The API key stays on the server; the browser never sees it. With a hosted API the question text leaves the company, so decide that before switching it on.

## Files

| File | What it does |
|---|---|
| `Views/Shared/_AiChat.cshtml` | the widget (one `<partial name="_AiChat" />` in `_DashboardLayout.cshtml`) |
| `wwwroot/js/ai-chat.js`, `wwwroot/css/ai-chat.css` | behaviour and look |
| `Controllers/AiChatController.cs` | `POST /AiChat/Ask` (signed-in users only, anti-forgery token) |
| `Services/Ai/AiChatService.cs` | checks the question, per-user rate limit, builds the messages, picks the provider |
| `Services/Ai/AiChatPrompts.cs` | the rules and the per-role description of pages. **Edit this file to change what the assistant knows.** |
| `Services/Ai/OllamaChatProvider.cs`, `OpenAiChatProvider.cs` | the two ways to reach a model (`IAiChatProvider`) |

## Safety rules built in

* The role comes from the sign-in on the server, never from the browser. The page path from the browser is accepted only if it is a plain path.
* Earlier messages sent by the browser are trusted only as `user` / `assistant` text, so nobody can inject a `system` message.
* The assistant has **no access to the database** (version 1). It cannot see requests, offers or prices, so it cannot leak one supplier's data to another. If you later give it data, load only what the signed-in user may see, on the server.
* Questions and answers are not stored and not logged. Only errors are logged.
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
