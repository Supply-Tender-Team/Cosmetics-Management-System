using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TenderSystem.Infrastructure.Options;
using TenderSystem.Services.Email;

namespace TenderSystem.Tests.Web;

/// <summary>
/// E-mail: how it is sent (a real mail server, a saved file in Development, or not at all), the e-mail bodies, and the outbox rules that need no database.
/// (The outbox's database commands are SQL Server only, so they are tried by hand.)
/// </summary>
public class EmailTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tender-tests-email-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }

    private EmailService Service(bool smtp = false, bool development = true, string baseUrl = "https://site.example.com/", Action<EmailOptions>? tweak = null)
    {
        var options = new EmailOptions { Enabled = smtp, BaseUrl = baseUrl };
        tweak?.Invoke(options);
        return new EmailService(Options.Create(options), new AccountTestKit.TestEnvironment(_folder, development), NullLogger<EmailService>.Instance);
    }

    private string[] SavedFiles() => Directory.Exists(Path.Combine(_folder, "App_Data", "emails")) ? Directory.GetFiles(Path.Combine(_folder, "App_Data", "emails")) : [];

    // ---------------------------------------------------------------- the three modes

    [Theory]
    [InlineData(true, true, "smtp", "smtp")]
    [InlineData(true, false, "smtp", "smtp")]
    [InlineData(false, true, "saved to App_Data/emails", "preview")]
    [InlineData(false, false, "off", "off")]
    public void The_mode_follows_the_SMTP_switch_and_the_environment(bool smtp, bool development, string mode, string delivery)
    {
        var service = Service(smtp, development);

        Assert.Equal(mode, service.Mode);
        Assert.Equal(delivery, service.DeliveryMode);
    }

    [Fact]
    public async Task In_development_without_SMTP_the_email_is_saved_as_a_file_you_can_open()
    {
        var sent = await Service().SendAsync("sara@test.com", "Welcome aboard", "<p>Hello</p>");

        Assert.True(sent);
        var file = Assert.Single(SavedFiles());
        Assert.EndsWith("sara_test_com.html", file);
        var text = await File.ReadAllTextAsync(file);
        Assert.Contains("To: sara@test.com | Subject: Welcome aboard", text);
        Assert.Contains("<p>Hello</p>", text);
    }

    [Fact]
    public async Task Outside_development_without_SMTP_nothing_is_sent_and_nothing_is_saved()
    {
        var sent = await Service(development: false).SendAsync("sara@test.com", "Welcome", "<p>Hello</p>");

        Assert.False(sent);
        Assert.Empty(SavedFiles());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_recipient_is_skipped_without_an_error(string to)
    {
        Assert.False(await Service().SendAsync(to, "Subject", "<p>x</p>"));
        Assert.Empty(SavedFiles());
    }

    [Fact]
    public async Task A_problem_saving_the_file_gives_false_and_never_throws()
    {
        var blocker = Path.Combine(Path.GetTempPath(), "tender-tests-blocker-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(blocker, "a file where a folder should be");
        try
        {
            var service = new EmailService(Options.Create(new EmailOptions()), new AccountTestKit.TestEnvironment(blocker, true), NullLogger<EmailService>.Instance);

            Assert.False(await service.SendAsync("sara@test.com", "Subject", "<p>x</p>"));
        }
        finally { File.Delete(blocker); }
    }

    [Fact]
    public async Task A_mail_server_that_is_not_there_gives_false_and_never_throws()
    {
        var closedPort = FreePort();                                           // nobody listens on it

        var sent = await Service(smtp: true, tweak: o => { o.Host = "127.0.0.1"; o.Port = closedPort; }).SendAsync("sara@test.com", "Subject", "<p>x</p>");

        Assert.False(sent);
    }

    [Fact]
    public async Task A_mail_server_that_never_answers_is_given_up_after_the_timeout()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();                                                      // accepts the connection, then says nothing
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var service = Service(smtp: true, tweak: o => { o.Host = "127.0.0.1"; o.Port = port; o.TimeoutSeconds = 1; });

        var clock = Stopwatch.StartNew();
        var sent = await service.SendAsync("sara@test.com", "Subject", "<p>x</p>");

        Assert.False(sent);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), "it waited " + clock.Elapsed);   // not MailKit's default of 2 minutes
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    // ---------------------------------------------------------------- links

    [Theory]
    [InlineData("https://site.example.com/", "/Supplier/Dashboard", "https://site.example.com/Supplier/Dashboard")]
    [InlineData("https://site.example.com", "Supplier/Dashboard", "https://site.example.com/Supplier/Dashboard")]
    [InlineData("https://site.example.com///", "///x", "https://site.example.com/x")]
    public void A_path_becomes_a_full_link_with_one_slash(string baseUrl, string path, string expected) =>
        Assert.Equal(expected, Service(baseUrl: baseUrl).AbsoluteUrl(path));

    // ---------------------------------------------------------------- the e-mail bodies

    [Fact]
    public void Text_written_by_people_can_never_inject_html_into_an_email()
    {
        const string evil = "<script>alert(1)</script>";

        var bodies = new[]
        {
            EmailTemplates.Welcome(evil, evil, evil, "https://x.example/\"><script>"),
            EmailTemplates.PasswordReset(evil, "https://x.example/?a=1&b=\"2\""),
            EmailTemplates.PasswordChanged(evil, "https://x.example"),
            EmailTemplates.SupplierRejected(evil, evil),
            EmailTemplates.Announcement(evil, evil + "\n" + evil, evil),
            EmailTemplates.Notification(evil, evil, "https://x.example/\"onmouseover=\"x"),
        };

        foreach (var body in bodies)
        {
            Assert.DoesNotContain("<script>", body);
            Assert.DoesNotContain("onmouseover=\"x", body);
        }
        Assert.Contains("&lt;script&gt;", bodies[0]);
    }

    [Fact]
    public void The_welcome_email_carries_the_temporary_password_and_the_sign_in_link()
    {
        var body = EmailTemplates.Welcome("Omar", "omar@test.com", "Tmp-Pass#42", "https://site.example.com/Account/Login");

        Assert.Contains("Tmp-Pass#42", body);
        Assert.Contains("omar@test.com", body);
        Assert.Contains("href=\"https://site.example.com/Account/Login\"", body);
    }

    [Fact]
    public void An_announcement_keeps_each_line_as_its_own_paragraph()
    {
        var body = EmailTemplates.Announcement("Meeting", "First line\r\n\r\nSecond line", "Admin");

        Assert.Contains("<p>First line</p><p>Second line</p>", body);
    }

    // ---------------------------------------------------------------- the outbox rules that need no database

    private static EmailOutboxRequest Request(string key = "award-winner:1:2", string to = "sara@test.com", string subject = "Subject", string body = "<p>x</p>") =>
        new(key, "AwardWinner", to, subject, body);

    private static string? Problem(EmailOutboxRequest r) => EmailOutboxService.Check(r, EmailOutboxService.OneLine(r.Subject));

    [Fact]
    public void A_complete_request_is_accepted() => Assert.Null(Problem(Request()));

    [Theory]
    [InlineData("", "sara@test.com", "S", "<p>x</p>", "dedup key")]
    [InlineData("k", "not-an-email", "S", "<p>x</p>", "recipient")]
    [InlineData("k", "Sara <sara@test.com>", "S", "<p>x</p>", "recipient")]        // a name plus an address is not accepted: only the plain address
    [InlineData("k", "sara@test.com", "   ", "<p>x</p>", "subject")]
    [InlineData("k", "sara@test.com", "S", "", "body")]
    public void An_incomplete_request_is_refused_with_a_reason(string key, string to, string subject, string body, string reason) =>
        Assert.Contains(reason, Problem(Request(key, to, subject, body)));

    [Fact]
    public void Too_long_values_are_refused()
    {
        Assert.NotNull(Problem(Request(key: new string('k', 201))));
        Assert.NotNull(Problem(Request(body: new string('x', 500_001))));
    }

    [Fact]
    public void A_subject_is_one_line_so_it_cannot_inject_mail_headers_and_is_cut_to_200_characters()
    {
        Assert.Equal("Hello Bcc: evil@test.com", EmailOutboxService.OneLine("Hello\r\nBcc: evil@test.com"));
        Assert.Equal(200, EmailOutboxService.OneLine(new string('a', 500)).Length);
        Assert.Equal("", EmailOutboxService.OneLine(null));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 5)]
    [InlineData(3, 15)]
    [InlineData(4, 60)]
    [InlineData(5, 240)]
    [InlineData(9, 240)]                                                      // after the list it stays at the longest wait
    [InlineData(0, 1)]
    public void A_failed_email_is_tried_again_after_a_growing_delay(int failedAttempts, int minutes) =>
        Assert.Equal(TimeSpan.FromMinutes(minutes), EmailOutboxProcessor.RetryDelay(failedAttempts));
}
