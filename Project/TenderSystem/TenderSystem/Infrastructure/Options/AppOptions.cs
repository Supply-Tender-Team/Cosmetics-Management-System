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

public class WebAuthnOptions
{
    public string RelyingPartyId { get; set; } = "localhost";
    public string RelyingPartyName { get; set; } = "Sourcing & Tender System";
    public string[] Origins { get; set; } = ["https://localhost:7180"];
}

public class FaceOptions
{
    public double MatchThreshold { get; set; } = 0.6;
    public double MinLivenessScore { get; set; } = 0.9;
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

        services.Configure<WebAuthnOptions>(o =>
        {
            // Derived from PUBLIC_URL. WEBAUTHN_RP_ID / WEBAUTHN_ORIGINS only exist as optional overrides.
            o.RelyingPartyId = Env.GetString("WEBAUTHN_RP_ID", new Uri(PublicUrl).Host);
            o.RelyingPartyName = Env.GetString("WEBAUTHN_RP_NAME", o.RelyingPartyName);
            o.Origins = List("WEBAUTHN_ORIGINS", PublicUrl);
        });

        services.Configure<FaceOptions>(o =>
        {
            o.MatchThreshold = Env.GetDouble("FACE_MATCH_THRESHOLD", o.MatchThreshold);
            o.MinLivenessScore = Env.GetDouble("FACE_MIN_LIVENESS_SCORE", o.MinLivenessScore);
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
