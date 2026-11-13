using System.Text;
using FakturowanieKSeF.Contracts;
using FakturowanieKSeF.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace FakturowanieKSeF.Api.Services.Email;

/// <summary>
/// Wiadomości systemowe konta (D53): potwierdzenie adresu przy rejestracji, reset hasła i zmiana adresu.
/// Identity (<c>MapIdentityApi</c>) wywołuje tę klasę przy rejestracji, ponownej wysyłce potwierdzenia i „nie pamiętam
/// hasła”. Linki prowadzą do stron aplikacji klienckiej, a kod jest we FRAGMENCIE adresu (po <c>#</c>) - przeglądarka
/// nie wysyła go do serwera, więc nie trafia do logów proxy (jak token zaproszenia - SEC-9).
///
/// Usługa jest singletonem (Identity pobiera ją z głównego kontenera przy mapowaniu endpointów), więc kolejkę poczty
/// (zależną od kontekstu bazy) otwiera we własnym zakresie dla każdej wiadomości.
public sealed class AccountEmails(IServiceScopeFactory scopes, EmailWorkSignal signal, IConfiguration configuration,
    IHttpContextAccessor http) : IEmailSender<ApplicationUser>
{
    /// <summary>Ważność linków: potwierdzenie i zmiana adresu - 24 h, reset hasła - 1 h (D53).</summary>
    public static readonly TimeSpan ConfirmationValidity = TimeSpan.FromHours(24);
    public static readonly TimeSpan PasswordResetValidity = TimeSpan.FromHours(1);

    /// <summary>
    /// Identity buduje link do własnego <c>GET /api/auth/confirmEmail?userId=…&amp;code=…[&amp;changedEmail=…]</c>;
    /// zamieniamy go na stronę klienta <c>/potwierdz-email#userId=…&amp;code=…</c>, która wywołuje ten endpoint.
    /// </summary>
    public async Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
    {
        // Identity przekazuje link zakodowany jako HTML (&amp; między parametrami)
        var query = QueryHelpers.ParseQuery(new Uri(System.Net.WebUtility.HtmlDecode(confirmationLink)).Query);
        var fragment = $"userId={Uri.EscapeDataString(query["userId"].ToString())}&code={Uri.EscapeDataString(query["code"].ToString())}";
        if (query.TryGetValue("changedEmail", out var changed) && changed.Count > 0)
        {
            fragment += $"&changedEmail={Uri.EscapeDataString(changed.ToString())}";
            await SendChangeEmailAsync(email, $"{AppUrl()}/potwierdz-email#{fragment}");
            return;
        }

        var body = new StringBuilder()
            .AppendLine("Dzień dobry,").AppendLine()
            .AppendLine("dziękujemy za założenie konta w aplikacji Fakturowanie KSeF. Aby je aktywować, potwierdź adres e-mail:")
            .AppendLine($"{AppUrl()}/potwierdz-email#{fragment}").AppendLine()
            .AppendLine("Link jest ważny 24 godziny. Jeśli to nie Ty zakładałeś konto, zignoruj tę wiadomość.")
            .ToString();
        await QueueAsync(EmailKind.AccountConfirmation, email, "Potwierdź adres e-mail - Fakturowanie KSeF", body);
    }

    /// <summary>Nieużywane: aplikacja wysyła link (<see cref="SendPasswordResetCodeAsync"/>), nie sam kod.</summary>
    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
        SendPasswordResetCodeAsync(user, email, resetLink);

    /// <summary>„Nie pamiętam hasła” (Identity wysyła tylko na potwierdzony adres istniejącego konta).</summary>
    public async Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
    {
        var code = System.Net.WebUtility.HtmlDecode(resetCode); // Identity koduje kod jako HTML
        var link = $"{AppUrl()}/reset-hasla#email={Uri.EscapeDataString(email)}&code={Uri.EscapeDataString(code)}";
        var body = new StringBuilder()
            .AppendLine("Dzień dobry,").AppendLine()
            .AppendLine("otrzymaliśmy prośbę o ustawienie nowego hasła do Twojego konta w aplikacji Fakturowanie KSeF:")
            .AppendLine(link).AppendLine()
            .AppendLine("Link jest ważny 1 godzinę i działa jeden raz. Jeśli to nie Ty prosiłeś o zmianę hasła, zignoruj tę")
            .AppendLine("wiadomość - dotychczasowe hasło pozostaje bez zmian. Logowanie dwuskładnikowe (jeśli jest włączone) nadal obowiązuje.")
            .ToString();
        await QueueAsync(EmailKind.PasswordReset, email, "Ustaw nowe hasło - Fakturowanie KSeF", body);
    }

    private Task SendChangeEmailAsync(string newEmail, string link)
    {
        var body = new StringBuilder()
            .AppendLine("Dzień dobry,").AppendLine()
            .AppendLine("poproszono o zmianę adresu e-mail konta w aplikacji Fakturowanie KSeF na ten adres. Aby ją potwierdzić, otwórz link:")
            .AppendLine(link).AppendLine()
            .AppendLine("Link jest ważny 24 godziny. Po potwierdzeniu logujesz się nowym adresem. Jeśli to nie Ty, zignoruj tę wiadomość.")
            .ToString();
        return QueueAsync(EmailKind.EmailChange, newEmail, "Potwierdź nowy adres e-mail - Fakturowanie KSeF", body);
    }

    /// <summary>Powiadomienie na DOTYCHCZASOWY adres - właściciel konta dowie się o próbie zmiany adresu.</summary>
    public Task SendEmailChangeNoticeAsync(string oldEmail, string newEmail)
    {
        var body = new StringBuilder()
            .AppendLine("Dzień dobry,").AppendLine()
            .AppendLine($"poproszono o zmianę adresu e-mail Twojego konta w aplikacji Fakturowanie KSeF na: {newEmail}.")
            .AppendLine("Zmiana nastąpi dopiero po kliknięciu linku wysłanego na nowy adres.").AppendLine()
            .AppendLine("Jeśli to nie Ty, natychmiast zmień hasło i włącz logowanie dwuskładnikowe.")
            .ToString();
        return QueueAsync(EmailKind.EmailChangeNotice, oldEmail, "Prośba o zmianę adresu e-mail - Fakturowanie KSeF", body);
    }

    private async Task QueueAsync(EmailKind kind, string to, string subject, string body)
    {
        var ct = http.HttpContext?.RequestAborted ?? CancellationToken.None;
        using var scope = scopes.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<EmailOutbox>();
        if (await outbox.EnqueueSystemAsync(kind, to, subject, body, ct)) signal.Pulse();
    }

    /// <summary>Adres aplikacji z konfiguracji (produkcyjnie), w developmencie - z żądania.</summary>
    private string AppUrl()
    {
        if (configuration["App:PublicUrl"] is { Length: > 0 } url) return url.TrimEnd('/');
        var request = http.HttpContext?.Request;
        return request is null ? "" : $"{request.Scheme}://{request.Host}";
    }
}

/// <summary>Token resetu hasła ważny krócej (1 h) niż pozostałe tokeny Identity (24 h) - D53.</summary>
public sealed class PasswordResetTokenProvider(
    Microsoft.AspNetCore.DataProtection.IDataProtectionProvider dataProtectionProvider,
    Microsoft.Extensions.Options.IOptions<PasswordResetTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<ApplicationUser>> logger)
    : DataProtectorTokenProvider<ApplicationUser>(dataProtectionProvider, options, logger)
{
    public const string Name = "PasswordReset";
}

public sealed class PasswordResetTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public PasswordResetTokenProviderOptions()
    {
        Name = PasswordResetTokenProvider.Name;
        TokenLifespan = AccountEmails.PasswordResetValidity;
    }
}
