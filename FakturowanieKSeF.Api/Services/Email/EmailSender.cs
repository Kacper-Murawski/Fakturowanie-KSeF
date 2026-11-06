using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace FakturowanieKSeF.Api.Services.Email;

/// <summary>Załącznik wiadomości.</summary>
public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);

/// <summary>Wiadomość gotowa do wysłania.</summary>
/// <param name="SenderName">Nazwa nadawcy widoczna u odbiorcy (np. nazwa firmy użytkownika).</param>
/// <param name="ReplyTo">Adres do odpowiedzi (e-mail firmy) - nadawcą technicznym jest adres aplikacji.</param>
public sealed record OutgoingEmail(
    string To,
    string Subject,
    string Body,
    string? SenderName = null,
    string? ReplyTo = null,
    IReadOnlyList<EmailAttachment>? Attachments = null);

/// <summary>Wysyłka wiadomości (prawdziwa - SMTP, w testach - atrapa zapamiętująca wiadomości).</summary>
public interface IEmailSender
{
    Task SendAsync(OutgoingEmail email, CancellationToken ct = default);
}

/// <summary>Konfiguracja poczty (sekcja „Email”). Hasło SMTP wyłącznie ze zmiennych środowiskowych / user-secrets.</summary>
public sealed class EmailOptions
{
    /// <summary>Adres nadawcy (aplikacji), np. faktury@twojadomena.pl - musi być dozwolony przez serwer SMTP (SPF/DKIM).</summary>
    public string From { get; set; } = "faktury@localhost";

    public string FromName { get; set; } = "Fakturowanie KSeF";

    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }

    /// <summary>Szyfrowanie połączenia: Auto (STARTTLS, jeśli serwer obsługuje), SslOnConnect, StartTls, None (tylko lokalnie - Mailpit).</summary>
    public SecureSocketOptions Security { get; set; } = SecureSocketOptions.Auto;

    /// <summary>Ochrona przed użyciem aplikacji do rozsyłania spamu: wiadomości do kontrahentów na firmę na dobę.</summary>
    public int DailyLimitPerCompany { get; set; } = 100;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SmtpHost);
}

/// <summary>Wysyłka przez serwer SMTP (MailKit). Lokalnie - Mailpit z docker compose (localhost:1025).</summary>
public sealed class SmtpEmailSender(Microsoft.Extensions.Options.IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(OutgoingEmail email, CancellationToken ct = default)
    {
        var o = options.Value;
        if (!o.IsConfigured) throw new InvalidOperationException("Wysyłka e-mail nie jest skonfigurowana (Email:SmtpHost).");

        using var client = new SmtpClient();
        await client.ConnectAsync(o.SmtpHost, o.SmtpPort, o.Security, ct);
        if (!string.IsNullOrEmpty(o.SmtpUsername))
            await client.AuthenticateAsync(o.SmtpUsername, o.SmtpPassword ?? "", ct);
        await client.SendAsync(Build(email, o), ct);
        await client.DisconnectAsync(true, ct);
    }

    /// <summary>
    /// Budowa wiadomości MIME. Nadawcą jest adres aplikacji (inaczej wiadomości „w imieniu” cudzej domeny byłyby
    /// odrzucane jako podszyte - SPF/DMARC), a nazwa wyświetlana i adres odpowiedzi to dane firmy użytkownika.
    /// </summary>
    public static MimeMessage Build(OutgoingEmail email, EmailOptions o)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(Clean(email.SenderName) ?? o.FromName, o.From));
        message.To.Add(MailboxAddress.Parse(email.To));
        if (!string.IsNullOrWhiteSpace(email.ReplyTo)) message.ReplyTo.Add(MailboxAddress.Parse(email.ReplyTo));
        message.Subject = Clean(email.Subject) ?? "";

        var body = new BodyBuilder { TextBody = email.Body };
        foreach (var a in email.Attachments ?? [])
            body.Attachments.Add(a.FileName, a.Content, ContentType.Parse(a.ContentType));
        message.Body = body.ToMessageBody();
        return message;
    }

    /// <summary>Usunięcie znaków sterujących (np. końców linii) z pól nagłówka.</summary>
    private static string? Clean(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : new string(s.Where(c => !char.IsControl(c)).ToArray()).Trim();
}
