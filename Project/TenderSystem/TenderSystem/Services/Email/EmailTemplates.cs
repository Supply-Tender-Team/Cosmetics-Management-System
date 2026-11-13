using System.Net;

namespace TenderSystem.Services.Email;

/// <summary>
/// Ready-made HTML bodies for the emails the system sends (TEAMLY palette, inline CSS because email clients ignore stylesheets).
/// Every value you pass in is HTML-encoded here, so user text can never inject HTML into an email.
/// To add a new email: add one more static method that calls Layout(...).
/// </summary>
public static class EmailTemplates
{
    private static string E(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

    /// <summary>The shared look: burgundy header, cream card, optional button.</summary>
    private static string Layout(string title, string contentHtml, string? buttonText = null, string? buttonUrl = null)
    {
        var button = buttonText != null && buttonUrl != null
            ? $"""<p style="margin:28px 0 8px"><a href="{E(buttonUrl)}" style="background:#722F37;color:#FAF5F0;text-decoration:none;padding:12px 26px;border-radius:10px;font-weight:600;display:inline-block">{E(buttonText)}</a></p>"""
            : string.Empty;

        return $"""
        <!DOCTYPE html>
        <html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"></head>
        <body style="margin:0;padding:0;background:#E2D5CF;font-family:Cairo,'Segoe UI',Tahoma,Arial,sans-serif;color:#4A302C">
          <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#E2D5CF;padding:24px 12px">
            <tr><td align="center">
              <table role="presentation" width="560" cellspacing="0" cellpadding="0" style="max-width:560px;width:100%;background:#FAF5F0;border-radius:16px;overflow:hidden;border:1px solid #D8C1AE">
                <tr><td style="background:#722F37;color:#FAF5F0;padding:20px 28px;font-size:20px;font-weight:700;letter-spacing:.5px">Sourcing &amp; Tender</td></tr>
                <tr><td style="padding:28px">
                  <h1 style="margin:0 0 14px;font-size:22px;color:#4A302C">{E(title)}</h1>
                  <div style="font-size:16px;line-height:1.7;color:#4A302C">{contentHtml}</div>
                  {button}
                </td></tr>
                <tr><td style="padding:16px 28px;background:#E2D5CF;color:#805B52;font-size:12px">
                  This message was sent automatically by the Sourcing &amp; Tender system. Please do not reply to it.
                </td></tr>
              </table>
            </td></tr>
          </table>
        </body></html>
        """;
    }

    /// <summary>New employee account created by the Admin (contains the temporary password).</summary>
    public static string Welcome(string fullName, string email, string temporaryPassword, string loginUrl) =>
        Layout("Your account is ready",
            $"<p>Hello {E(fullName)},</p>" +
            "<p>An account was created for you in the Sourcing &amp; Tender system.</p>" +
            $"<p><b>Email:</b> {E(email)}<br><b>Temporary password:</b> <code style=\"background:#E2D5CF;padding:2px 8px;border-radius:6px\">{E(temporaryPassword)}</code></p>" +
            "<p>You will be asked to choose a new password the first time you sign in.</p>",
            "Sign in", loginUrl);

    public static string SupplierApproved(string companyName, string loginUrl) =>
        Layout("Your registration is approved",
            $"<p>Good news! <b>{E(companyName)}</b> was approved as a supplier.</p>" +
            "<p>You can now sign in, complete your profile and receive invitations to tenders.</p>",
            "Sign in", loginUrl);

    public static string SupplierRejected(string companyName, string reason) =>
        Layout("Your registration was not approved",
            $"<p>We reviewed the registration of <b>{E(companyName)}</b> and could not approve it.</p>" +
            $"<p><b>Reason:</b> {E(reason)}</p>" +
            "<p>If you think this is a mistake, please contact the sourcing team.</p>");

    public static string SupplierSuspended(string companyName, string reason) =>
        Layout("Your supplier account is suspended",
            $"<p>The account of <b>{E(companyName)}</b> was suspended.</p>" +
            $"<p><b>Reason:</b> {E(reason)}</p>" +
            "<p>Please contact the sourcing team to resolve this.</p>");

    public static string PasswordReset(string fullName, string resetUrl) =>
        Layout("Reset your password",
            $"<p>Hello {E(fullName)},</p>" +
            "<p>We received a request to reset your password. Click the button below to choose a new one. " +
            "The link works for a limited time.</p>" +
            "<p>If you did not ask for this, you can ignore this email: your password stays the same.</p>",
            "Choose a new password", resetUrl);

    /// <summary>Security notice after a password change or reset.</summary>
    public static string PasswordChanged(string fullName, string loginUrl) =>
        Layout("Your password was changed",
            $"<p>Hello {E(fullName)},</p>" +
            "<p>The password of your account was just changed.</p>" +
            "<p>If this was you, there is nothing more to do. If it was NOT you, please contact the Admin immediately.</p>",
            "Sign in", loginUrl);

    /// <summary>Generic alert, used for important notifications (new offer, decision needed, clarification...).</summary>
    public static string Notification(string title, string message, string? linkUrl) =>
        Layout(title, $"<p>{E(message)}</p>", linkUrl == null ? null : "Open in the system", linkUrl);
}
