using DotNetEnv;

namespace TenderSystem.Infrastructure.Options;

// Typed settings. Values come from the .env file (see .env.example).
// Inject with IOptions<OllamaOptions>, IOptions<WebAuthnOptions> ...

public class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "llama3";
    public int TimeoutSeconds { get; set; } = 120;
}

/// <summary>
/// The floating assistant chat (every dashboard page). One switch decides who answers:
///   AI_CHAT_PROVIDER=ollama  local model (uses OLLAMA_BASE_URL / OLLAMA_MODEL)
///   AI_CHAT_PROVIDER=openai  any hosted API that speaks the OpenAI "chat/completions" format
///                            (OpenAI, Groq, OpenRouter, Gemini's compatible endpoint...) with AI_API_*
///   AI_CHAT_PROVIDER=off     the button is hidden
/// </summary>
public class AiChatOptions
{
    public string Provider { get; set; } = "ollama";
    public string ApiBaseUrl { get; set; } = "https://api.openai.com/v1";
    public string ApiKey { get; set; } = "";
    public string ApiModel { get; set; } = "gpt-4o-mini";
    public int TimeoutSeconds { get; set; } = 120;
    /// <summary>Longest question a user may send.</summary>
    public int MaxMessageChars { get; set; } = 1000;
    /// <summary>How many earlier messages are sent along so the assistant remembers the conversation.</summary>
    public int MaxHistoryMessages { get; set; } = 10;
    /// <summary>Questions allowed per user per minute (a local model is slow, and an API costs money).</summary>
    public int MaxQuestionsPerMinute { get; set; } = 8;
}

public class WebAuthnOptions
{
    public string RelyingPartyId { get; set; } = "localhost";
    public string RelyingPartyName { get; set; } = "Sourcing & Tender System";
    public string[] Origins { get; set; } = ["https://localhost:7180"];
    /// <summary>Failed fingerprint sign-ins allowed per client address in 10 minutes (the passkey is not known yet, so there is no account to lock).</summary>
    public int MaxFailuresPer10Minutes { get; set; } = 10;
}

public class FaceOptions
{
    /// <summary>Cosine similarity needed to accept a face (1 = identical, ~0 = different people). Tune with real faces.</summary>
    public double MatchThreshold { get; set; } = 0.5;
    /// <summary>Share of the captured frames that must behave as asked (0..1).</summary>
    public double MinLivenessScore { get; set; } = 0.7;
    /// <summary>Straight-look frames: head turn must stay below this (0 = perfectly straight).</summary>
    public double YawCenterMax { get; set; } = 0.18;
    /// <summary>Turn frames: head turn must reach at least this much.</summary>
    public double YawTurnMin { get; set; } = 0.25;
    /// <summary>The face must be at least this wide compared with the picture (too small = too far away).</summary>
    public double MinFaceWidthRatio { get; set; } = 0.15;
    /// <summary>How many head turns the LOGIN asks for: 2 = both sides (strictest, the default), 1 = one random side (faster). Enrollment always uses both.</summary>
    public int LoginTurns { get; set; } = 2;
    /// <summary>"Smart" sign-in: with an empty email the face is searched among ALL enrolled people. false = email is always required.</summary>
    public bool AllowIdentify { get; set; } = true;
    /// <summary>Similarity needed to name a person when no email was typed. Stricter than MatchThreshold because the search is wider.</summary>
    public double IdentifyThreshold { get; set; } = 0.55;
    /// <summary>The best match must beat the second-best PERSON by at least this much, otherwise it is ambiguous (look-alikes) and refused.</summary>
    public double IdentifyMargin { get; set; } = 0.10;
    /// <summary>Failed "smart" attempts allowed per client address in 10 minutes (there is no account to lock without an email).</summary>
    public int IdentifyMaxFailures { get; set; } = 8;
    public string TemplateEncryptionKey { get; set; } = "";
}

public class UploadOptions
{
    public string RootPath { get; set; } = "App_Data/uploads";
    public int MaxFileSizeMb { get; set; } = 10;
    public string[] AllowedExtensions { get; set; } = [".pdf", ".png", ".jpg", ".jpeg", ".docx", ".xlsx"];
}

public class EmailOptions
{
    /// <summary>false = nothing is sent (emails are saved to App_Data/emails in Development).</summary>
    public bool Enabled { get; set; } = false;
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 587;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "no-reply@lumiere.local";
    public string FromName { get; set; } = "Sourcing & Tender System";
    /// <summary>Public address of the site, used to build links inside emails.</summary>
    public string BaseUrl { get; set; } = "https://localhost:7180";
}

public static class AppOptionsExtensions
{
    /// <summary>
    /// THE one place for the public address of the site (PUBLIC_URL in .env), e.g. https://localhost:7180
    /// or your tunnel link https://something.trycloudflare.com. Email links and WebAuthn (fingerprint) use it.
    /// </summary>
    public static string PublicUrl
    {
        get
        {
            var value = Env.GetString("PUBLIC_URL", "https://localhost:7180").Trim().TrimEnd('/');
            return Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http")
                ? value
                : "https://localhost:7180";
        }
    }

    public static IServiceCollection AddAppOptionsFromEnv(this IServiceCollection services)
    {
        static string[] List(string key, string fallback) =>
            Env.GetString(key, fallback).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        services.Configure<OllamaOptions>(o =>
        {
            o.BaseUrl = Env.GetString("OLLAMA_BASE_URL", o.BaseUrl);
            o.Model = Env.GetString("OLLAMA_MODEL", o.Model);
            o.TimeoutSeconds = Env.GetInt("OLLAMA_TIMEOUT_SECONDS", o.TimeoutSeconds);
        });

        services.Configure<AiChatOptions>(o =>
        {
            o.Provider = Env.GetString("AI_CHAT_PROVIDER", o.Provider).Trim().ToLowerInvariant();
            o.ApiBaseUrl = Env.GetString("AI_API_BASE_URL", o.ApiBaseUrl).Trim().TrimEnd('/');
            o.ApiKey = Env.GetString("AI_API_KEY", o.ApiKey).Trim();
            o.ApiModel = Env.GetString("AI_API_MODEL", o.ApiModel).Trim();
            o.TimeoutSeconds = Env.GetInt("AI_CHAT_TIMEOUT_SECONDS", o.TimeoutSeconds);
            o.MaxMessageChars = Env.GetInt("AI_CHAT_MAX_CHARS", o.MaxMessageChars);
            o.MaxHistoryMessages = Env.GetInt("AI_CHAT_MAX_HISTORY", o.MaxHistoryMessages);
            o.MaxQuestionsPerMinute = Env.GetInt("AI_CHAT_MAX_PER_MINUTE", o.MaxQuestionsPerMinute);
        });

        services.Configure<WebAuthnOptions>(o =>
        {
            // Derived from PUBLIC_URL. WEBAUTHN_RP_ID / WEBAUTHN_ORIGINS only exist as optional overrides.
            o.RelyingPartyId = Env.GetString("WEBAUTHN_RP_ID", new Uri(PublicUrl).Host);
            o.RelyingPartyName = Env.GetString("WEBAUTHN_RP_NAME", o.RelyingPartyName);
            o.Origins = List("WEBAUTHN_ORIGINS", PublicUrl);
            o.MaxFailuresPer10Minutes = Env.GetInt("WEBAUTHN_MAX_FAILURES", o.MaxFailuresPer10Minutes);
        });

        services.Configure<FaceOptions>(o =>
        {
            o.MatchThreshold = Env.GetDouble("FACE_MATCH_THRESHOLD", o.MatchThreshold);
            o.MinLivenessScore = Env.GetDouble("FACE_MIN_LIVENESS_SCORE", o.MinLivenessScore);
            o.YawCenterMax = Env.GetDouble("FACE_YAW_CENTER_MAX", o.YawCenterMax);
            o.YawTurnMin = Env.GetDouble("FACE_YAW_TURN_MIN", o.YawTurnMin);
            o.MinFaceWidthRatio = Env.GetDouble("FACE_MIN_FACE_WIDTH", o.MinFaceWidthRatio);
            o.LoginTurns = Env.GetInt("FACE_LOGIN_TURNS", o.LoginTurns);
            o.AllowIdentify = Env.GetBool("FACE_ALLOW_IDENTIFY", o.AllowIdentify);
            o.IdentifyThreshold = Env.GetDouble("FACE_IDENTIFY_THRESHOLD", o.IdentifyThreshold);
            o.IdentifyMargin = Env.GetDouble("FACE_IDENTIFY_MARGIN", o.IdentifyMargin);
            o.IdentifyMaxFailures = Env.GetInt("FACE_IDENTIFY_MAX_FAILURES", o.IdentifyMaxFailures);
            o.TemplateEncryptionKey = Env.GetString("FACE_TEMPLATE_ENCRYPTION_KEY", o.TemplateEncryptionKey);
        });

        services.Configure<UploadOptions>(o =>
        {
            o.RootPath = Env.GetString("UPLOAD_ROOT_PATH", o.RootPath);
            o.MaxFileSizeMb = Env.GetInt("UPLOAD_MAX_MB", o.MaxFileSizeMb);
            o.AllowedExtensions = List("UPLOAD_ALLOWED_EXTENSIONS", string.Join(',', o.AllowedExtensions));
        });

        services.Configure<EmailOptions>(o =>
        {
            o.Enabled = Env.GetBool("SMTP_ENABLED", o.Enabled);
            o.Host = Env.GetString("SMTP_HOST", o.Host);
            o.Port = Env.GetInt("SMTP_PORT", o.Port);
            o.User = Env.GetString("SMTP_USER", o.User);
            o.Password = Env.GetString("SMTP_PASSWORD", o.Password);
            o.From = Env.GetString("SMTP_FROM", o.From);
            o.FromName = Env.GetString("SMTP_FROM_NAME", o.FromName);
            o.BaseUrl = Env.GetString("APP_BASE_URL", PublicUrl); // optional override, normally PUBLIC_URL
        });

        return services;
    }
}
