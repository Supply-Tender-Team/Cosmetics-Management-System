using System.Net;

namespace TenderSystem.Areas.Admin.Models;

// Admin-owned notification content. EmailService supplies the plain-text alternative from this HTML.
public static class AdminEmailTemplates
{
    public const string EmployeeWelcomeSubject = "Welcome to MS.WAND! 🎉";
    public const string SupplierApprovalSubject = "Your Supplier Registration Has Been Approved!";
    public const string SupplierRejectionSubject = "Update on Your MS.WAND Supplier Registration";

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string Layout(string title, string content, string accent, string accentBackground)
    {
        return $"""
            <!doctype html>
            <html lang="en">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"></head>
            <body style="margin:0;padding:0;background:#F4EEEB;color:#4A302C;font-family:Cairo,'Segoe UI',Arial,sans-serif">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#F4EEEB;padding:24px 12px">
                <tr><td align="center">
                  <table role="presentation" width="560" cellspacing="0" cellpadding="0" style="width:100%;max-width:560px;background:#FAF5F0;border:1px solid #D8C1AE;border-radius:14px;overflow:hidden">
                    <tr><td style="padding:22px 28px;background:#722F37;color:#FAF5F0;font-size:21px;font-weight:700">MS.WAND</td></tr>
                    <tr><td style="padding:26px 28px 28px;border-top:4px solid {accent};background:{accentBackground}">
                      <h1 style="margin:0 0 18px;color:#4A302C;font-size:22px;line-height:1.35">{Encode(title)}</h1>
                      <div style="font-size:16px;line-height:1.7">{content}</div>
                    </td></tr>
                    <tr><td style="padding:18px 28px;color:#805B52;font-size:12px;line-height:1.5">MS.WAND · Sourcing &amp; Tender Management<br>This is an automated message. Please do not reply.</td></tr>
                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """;
    }

    public static string SupplierApproved(string companyName) => Layout(
        "Your supplier registration has been approved",
        $"<p style=\"margin:0 0 14px\">Congratulations! The registration for <strong>{Encode(companyName)}</strong> has been approved.</p>" +
        "<p style=\"margin:0\">Welcome to the MS.WAND Supplier Network. We look forward to working with you.</p>",
        "#2F7654", "#F1F8F3");

    public static string SupplierRejected(string companyName, string? reason)
    {
        var reasonHtml = string.IsNullOrWhiteSpace(reason)
            ? string.Empty
            : $"<p style=\"margin:0 0 14px\"><strong>Reason:</strong> {Encode(reason.Trim())}</p>";
        return Layout(
            "Update on your supplier registration",
            $"<p style=\"margin:0 0 14px\">Thank you for applying to join the MS.WAND Supplier Network on behalf of <strong>{Encode(companyName)}</strong>.</p>" +
            "<p style=\"margin:0 0 14px\">After review, we are unable to approve your registration at this time.</p>" + reasonHtml +
            "<p style=\"margin:0\">Thank you for your interest in working with us.</p>",
            "#A55860", "#FBF3F2");
    }

    // The Auth owner can use this for the existing single temporary-password email.
    public static string EmployeeWelcome(string fullName, string email, string? department, string role,
        string temporaryPassword, string loginUrl) => Layout(
        "Welcome to MS.WAND!",
        $"<p style=\"margin:0 0 14px\">Welcome to MS.WAND, <strong>{Encode(fullName)}</strong>. We are pleased to have you on the team.</p>" +
        $"<p style=\"margin:0 0 14px\"><strong>Email:</strong> {Encode(email)}<br><strong>Department:</strong> {Encode(string.IsNullOrWhiteSpace(department) ? "Not provided" : department)}<br><strong>Role:</strong> {Encode(role)}</p>" +
        $"<p style=\"margin:0 0 14px\"><strong>Temporary password:</strong> <code style=\"font-size:16px;font-weight:700\">{Encode(temporaryPassword)}</code></p>" +
        "<p style=\"margin:0 0 14px\">This password is temporary. You will be asked to choose a new password when you first sign in.</p>" +
        $"<p style=\"margin:0\"><a href=\"{Encode(loginUrl)}\" style=\"color:#722F37;font-weight:700\">Sign in to MS.WAND</a></p>",
        "#B77A81", "#FBF4F3");
}
