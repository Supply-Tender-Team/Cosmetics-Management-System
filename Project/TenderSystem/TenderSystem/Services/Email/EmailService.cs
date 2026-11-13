using System.Text.RegularExpressions;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using TenderSystem.Infrastructure.Options;

namespace TenderSystem.Services.Email;

/// <summary>
/// Sends HTML email through SMTP (settings come from the SMTP_* keys in .env).
///
/// How to use it from any controller or service:
///     private readonly EmailService _email;   // inject it in the constructor
///     await _email.SendAsync(user.Email, "Welcome", EmailTemplates.Welcome(user.FullName, user.Email, tempPassword, loginUrl));
///
/// It NEVER throws: if the mail server is down it logs the error and returns false,
/// so a failed email never breaks approving a supplier or creating a user.
/// When SMTP_ENABLED=false (the default) nothing is sent: in Development the email is saved as an
/// .html file in App_Data/emails so you can open it in the browser and see exactly what would be sent.
/// </summary>
public class EmailService
{
    private readonly EmailOptions _options;
    private readonly IHostEnvironment _env;
    private readonly ILogger<EmailService> _log;

    public EmailService(IOptions<EmailOptions> options, IHostEnvironment env, ILogger<EmailService> log)
    {
        _options = options.Value;
        _env = env;
        _log = log;
    }

    /// <summary>"smtp" when real sending is on, otherwise "saved to App_Data/emails" (or "off" outside Development).</summary>
    public string Mode => _options.Enabled ? "smtp" : _env.IsDevelopment() ? "saved to App_Data/emails" : "off";

    /// <summary>Turns "/Supplier/Dashboard" into a full link such as "https://localhost:7180/Supplier/Dashboard".</summary>
    public string AbsoluteUrl(string path) => _options.BaseUrl.TrimEnd('/') + "/" + path.TrimStart('/');

    /// <returns>true when the email was handed to the mail server (or saved in file mode), false on any problem.</returns>
    public async Task<bool> SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(toEmail))
        {
            _log.LogWarning("Email '{Subject}' skipped: no recipient.", subject);
            return false;
        }

        if (!_options.Enabled)
            return await SaveToFileAsync(toEmail, subject, htmlBody, ct);

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_options.FromName, _options.From));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;
            message.Body = new BodyBuilder { HtmlBody = htmlBody, TextBody = ToPlainText(htmlBody) }.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(_options.Host, _options.Port, SecureSocketOptions.Auto, ct);
            if (!string.IsNullOrEmpty(_options.User))
                await client.AuthenticateAsync(_options.User, _options.Password, ct);
            await client.SendAsync(new SendRequest(message), ct);
            await client.DisconnectAsync(true, ct);

            _log.LogInformation("Email '{Subject}' sent to {To}.", subject, toEmail);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Email '{Subject}' to {To} failed. Check the SMTP_* settings in .env.", subject, toEmail);
            return false;
        }
    }

    private async Task<bool> SaveToFileAsync(string toEmail, string subject, string htmlBody, CancellationToken ct)
    {
        if (!_env.IsDevelopment())
        {
            _log.LogWarning("Email '{Subject}' to {To} NOT sent: SMTP_ENABLED is false.", subject, toEmail);
            return false;
        }

        try
        {
            var folder = Path.Combine(_env.ContentRootPath, "App_Data", "emails");
            Directory.CreateDirectory(folder);
            var safeName = Regex.Replace(toEmail, @"[^a-zA-Z0-9]+", "_");
            var file = Path.Combine(folder, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{safeName}.html");
            var header = $"<!-- To: {toEmail} | Subject: {subject} -->\n";
            await File.WriteAllTextAsync(file, header + htmlBody, ct);
            _log.LogInformation("Email '{Subject}' to {To} saved to {File} (SMTP_ENABLED is false).", subject, toEmail, file);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Could not save the email file.");
            return false;
        }
    }

    private static string ToPlainText(string html)
    {
        var text = Regex.Replace(html, @"<(br|/p|/h\d|/tr|/li)[^>]*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<style[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", "");
        return System.Net.WebUtility.HtmlDecode(Regex.Replace(text, @"\n\s*\n+", "\n\n")).Trim();
    }
}
