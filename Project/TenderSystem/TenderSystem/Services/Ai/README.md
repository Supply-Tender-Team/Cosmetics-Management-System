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

## Not the same as `AI-01`

`AiBidAnalysisService` (bid analysis for the Evaluation committee, saved in `AiBidAnalyses`) is a separate service. It can reuse `OLLAMA_*` and the provider classes here.
