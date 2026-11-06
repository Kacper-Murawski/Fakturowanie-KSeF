using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using KSeF.Client.Api.Builders.Auth;
using KSeF.Client.Api.Builders.Online;
using KSeF.Client.Api.Builders.X509Certificates;
using KSeF.Client.Api.Services;
using KSeF.Client.Core.Exceptions;
using KSeF.Client.Core.Interfaces;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.Core.Models;
using KSeF.Client.Core.Models.Authorization;
using KSeF.Client.Core.Models.Invoices;
using Microsoft.Extensions.Caching.Memory;

namespace FakturowanieKSeF.Ksef;

/// <summary>Wynik przyjęcia faktury do przetwarzania przez KSeF.</summary>
public sealed record KsefSendResult(string SessionReferenceNumber, string InvoiceReferenceNumber);

/// <summary>Stan przetwarzania faktury w KSeF (kod 150 - w toku, 200 - przyjęta, inne - odrzucona).</summary>
public sealed record KsefInvoiceStatus(int Code, string Description, IReadOnlyList<string> Details, string? KsefNumber, DateTimeOffset? AcceptedAt)
{
    public const int Processing = 150;
    public const int Accepted = 200;

    public bool IsProcessing => Code is Processing or 100;
    public bool IsAccepted => Code == Accepted && KsefNumber is not null;
}

/// <summary>Metadane faktury z KSeF (wynik wyszukiwania - bez treści faktury).</summary>
public sealed record KsefInvoiceMetadata(
    string KsefNumber,
    string InvoiceNumber,
    DateOnly IssueDate,
    DateTimeOffset AcquisitionDate,
    DateTimeOffset PermanentStorageDate,
    string SellerNip,
    string SellerName,
    decimal NetAmount,
    decimal VatAmount,
    decimal GrossAmount,
    string Currency,
    string? InvoiceHash = null);

/// <summary>
/// Strona wyników wyszukiwania. <see cref="PermanentStorageHwmDate"/> (High Water Mark) - moment, do którego KSeF
/// gwarantuje kompletność: żadna nowa faktura z datą trwałego zapisu ≤ HWM już się nie pojawi.
/// </summary>
public sealed record KsefMetadataPage(
    IReadOnlyList<KsefInvoiceMetadata> Invoices,
    bool HasMore,
    bool IsTruncated,
    DateTimeOffset? PermanentStorageHwmDate);

/// <summary>Nieprawidłowy, nieaktywny lub cofnięty token KSeF.</summary>
public sealed class KsefAuthenticationException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Operacje aplikacji na KSeF - cienka warstwa nad oficjalnym klientem MF (decyzja D10). Reszta aplikacji
/// zna tylko ten interfejs, dzięki czemu logika wysyłki jest testowalna bez dostępu do KSeF (atrapa w testach).
/// Wszystkie operacje dotyczą WYŁĄCZNIE środowiska testowego (D8).
/// </summary>
public interface IKsefGateway
{
    /// <summary>Sprawdza, czy token pozwala uwierzytelnić się w kontekście podanego NIP.</summary>
    Task VerifyTokenAsync(string nip, string ksefToken, CancellationToken ct = default);

    /// <summary>
    /// Tylko środowisko testowe: uwierzytelnia się samopodpisanym certyfikatem właściciela NIP
    /// i generuje token KSeF z uprawnieniami do wystawiania i odczytu faktur.
    /// </summary>
    Task<string> GenerateTestTokenAsync(string nip, CancellationToken ct = default);

    /// <summary>Otwiera sesję interaktywną i wysyła zaszyfrowaną fakturę.</summary>
    Task<KsefSendResult> SendInvoiceAsync(string nip, string ksefToken, byte[] invoiceXml, CancellationToken ct = default);

    Task<KsefInvoiceStatus> GetInvoiceStatusAsync(string nip, string ksefToken, string sessionReferenceNumber, string invoiceReferenceNumber, CancellationToken ct = default);

    Task CloseSessionAsync(string nip, string ksefToken, string sessionReferenceNumber, CancellationToken ct = default);

    /// <summary>UPO faktury albo null, jeśli jeszcze nie zostało wygenerowane.</summary>
    Task<string?> GetUpoAsync(string nip, string ksefToken, string sessionReferenceNumber, string ksefNumber, CancellationToken ct = default);

    /// <summary>
    /// Metadane faktur, w których firma jest nabywcą (Podmiot2), trwale zapisanych w KSeF od <paramref name="from"/>,
    /// rosnąco według daty trwałego zapisu, ograniczone do HWM (synchronizacja przyrostowa wg zaleceń MF).
    /// </summary>
    Task<KsefMetadataPage> QueryReceivedInvoicesAsync(string nip, string ksefToken, DateTimeOffset from, int pageOffset, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// Metadane faktur, w których firma jest sprzedawcą (Podmiot1) - także wystawionych poza aplikacją (inny program,
    /// aplikacja MF); zasady jak w <see cref="QueryReceivedInvoicesAsync"/> (D44).
    /// </summary>
    Task<KsefMetadataPage> QueryIssuedInvoicesAsync(string nip, string ksefToken, DateTimeOffset from, int pageOffset, int pageSize, CancellationToken ct = default);

    /// <summary>Pełny XML faktury o podanym numerze KSeF.</summary>
    Task<string> DownloadInvoiceAsync(string nip, string ksefToken, string ksefNumber, CancellationToken ct = default);
}

/// <summary>Implementacja <see cref="IKsefGateway"/> na oficjalnym kliencie KSeF Ministerstwa Finansów.</summary>
public sealed class KsefGateway(
    IKSeFClient ksefClient,
    IAuthorizationClient authorizationClient,
    IAuthCoordinator authCoordinator,
    ICryptographyService cryptography,
    IMemoryCache cache) : IKsefGateway
{
    private const int AuthInProgress = 100;
    private const int AuthSuccess = 200;

    public async Task VerifyTokenAsync(string nip, string ksefToken, CancellationToken ct = default) =>
        await GetAccessTokenAsync(nip, ksefToken, ct, useCache: false);

    public async Task<string> GenerateTestTokenAsync(string nip, CancellationToken ct = default)
    {
        await cryptography.WarmupAsync(ct);

        // Na środowisku testowym KSeF dopuszcza samopodpisany certyfikat dla dowolnego NIP (dokumentacja MF)
        using var certificate = SelfSignedCertificateForSignatureBuilder.Create()
            .WithGivenName("Użytkownik")
            .WithSurname("Testowy")
            .WithSerialNumber($"TINPL-{nip}")
            .WithCommonName("Fakturowanie KSeF - test")
            .Build();

        var accessToken = await AuthenticateWithCertificateAsync(nip, certificate, ct);

        var generated = await ksefClient.GenerateKsefTokenAsync(new KsefTokenRequest
        {
            Permissions = [KsefTokenPermissionType.InvoiceWrite, KsefTokenPermissionType.InvoiceRead],
            Description = "Fakturowanie KSeF (projekt inżynierski) - token testowy"
        }, accessToken, ct);

        // Token jest aktywowany asynchronicznie - czekamy, aż będzie gotowy do użycia
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var token = await ksefClient.GetKsefTokenAsync(generated.ReferenceNumber, accessToken, ct);
            if (token.Status == AuthenticationKsefTokenStatus.Active) return generated.Token;
            if (token.Status is not AuthenticationKsefTokenStatus.Pending)
                throw new KsefAuthenticationException($"KSeF nie aktywował tokenu (status: {token.Status}).");
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }

        throw new KsefAuthenticationException("Przekroczono czas oczekiwania na aktywację tokenu w KSeF.");
    }

    public async Task<KsefSendResult> SendInvoiceAsync(string nip, string ksefToken, byte[] invoiceXml, CancellationToken ct = default)
    {
        var accessToken = await GetAccessTokenAsync(nip, ksefToken, ct);
        await cryptography.WarmupAsync(ct);

        // Klucz AES-256 sesji szyfrowany kluczem publicznym MF; faktura szyfrowana kluczem sesji
        var encryption = cryptography.GetEncryptionData();
        var session = await ksefClient.OpenOnlineSessionAsync(OpenOnlineSessionRequestBuilder.Create()
            .WithFormCode(
                systemCode: SystemCodeHelper.GetSystemCode(SystemCode.FA3),
                schemaVersion: SystemCodeHelper.GetSchemaVersion(SystemCode.FA3),
                value: SystemCodeHelper.GetValue(SystemCode.FA3))
            .WithEncryption(
                encryptedSymmetricKey: encryption.EncryptionInfo.EncryptedSymmetricKey,
                initializationVector: encryption.EncryptionInfo.InitializationVector,
                publicKeyId: encryption.EncryptionInfo.PublicKeyId)
            .Build(), accessToken, cancellationToken: ct);

        var encrypted = cryptography.EncryptBytesWithAES256(invoiceXml, encryption.CipherKey, encryption.CipherIv);
        var plainMeta = cryptography.GetMetaData(invoiceXml);
        var encryptedMeta = cryptography.GetMetaData(encrypted);

        var sent = await ksefClient.SendOnlineSessionInvoiceAsync(SendInvoiceOnlineSessionRequestBuilder.Create()
            .WithInvoiceHash(plainMeta.HashSHA, plainMeta.FileSize)
            .WithEncryptedDocumentHash(encryptedMeta.HashSHA, encryptedMeta.FileSize)
            .WithEncryptedDocumentContent(Convert.ToBase64String(encrypted))
            .Build(), session.ReferenceNumber, accessToken, ct);

        return new KsefSendResult(session.ReferenceNumber, sent.ReferenceNumber);
    }

    public async Task<KsefInvoiceStatus> GetInvoiceStatusAsync(string nip, string ksefToken, string sessionReferenceNumber,
        string invoiceReferenceNumber, CancellationToken ct = default)
    {
        var accessToken = await GetAccessTokenAsync(nip, ksefToken, ct);
        var invoice = await ksefClient.GetSessionInvoiceAsync(sessionReferenceNumber, invoiceReferenceNumber, accessToken, ct);

        return new KsefInvoiceStatus(
            invoice.Status.Code,
            invoice.Status.Description ?? "",
            invoice.Status.Details?.ToList() ?? [],
            string.IsNullOrEmpty(invoice.KsefNumber) ? null : invoice.KsefNumber,
            invoice.AcquisitionDate);
    }

    public async Task CloseSessionAsync(string nip, string ksefToken, string sessionReferenceNumber, CancellationToken ct = default)
    {
        var accessToken = await GetAccessTokenAsync(nip, ksefToken, ct);
        await ksefClient.CloseOnlineSessionAsync(sessionReferenceNumber, accessToken, ct);
    }

    public async Task<string?> GetUpoAsync(string nip, string ksefToken, string sessionReferenceNumber, string ksefNumber,
        CancellationToken ct = default)
    {
        var accessToken = await GetAccessTokenAsync(nip, ksefToken, ct);
        try
        {
            var upo = await ksefClient.GetSessionInvoiceUpoByKsefNumberAsync(sessionReferenceNumber, ksefNumber, accessToken, ct);
            return string.IsNullOrWhiteSpace(upo) ? null : upo;
        }
        catch (KsefApiException ex) when ((int)ex.StatusCode is 404 or 400 or 409)
        {
            return null; // UPO jeszcze niewygenerowane - KSeF tworzy je po zamknięciu sesji
        }
    }

    public Task<KsefMetadataPage> QueryReceivedInvoicesAsync(string nip, string ksefToken, DateTimeOffset from,
        int pageOffset, int pageSize, CancellationToken ct = default) =>
        QueryAsync(InvoiceSubjectType.Subject2, nip, ksefToken, from, pageOffset, pageSize, ct); // firma jest nabywcą

    public Task<KsefMetadataPage> QueryIssuedInvoicesAsync(string nip, string ksefToken, DateTimeOffset from,
        int pageOffset, int pageSize, CancellationToken ct = default) =>
        QueryAsync(InvoiceSubjectType.Subject1, nip, ksefToken, from, pageOffset, pageSize, ct); // firma jest sprzedawcą

    private async Task<KsefMetadataPage> QueryAsync(InvoiceSubjectType subject, string nip, string ksefToken, DateTimeOffset from,
        int pageOffset, int pageSize, CancellationToken ct)
    {
        var accessToken = await GetAccessTokenAsync(nip, ksefToken, ct);
        var page = await ksefClient.QueryInvoiceMetadataAsync(new InvoiceQueryFilters
        {
            SubjectType = subject,
            DateRange = new DateRange
            {
                DateType = DateType.PermanentStorage,
                From = from,
                RestrictToPermanentStorageHwmDate = true
            }
        }, accessToken, pageOffset, pageSize, SortOrder.Asc, ct);

        var invoices = (page.Invoices ?? []).Select(i => new KsefInvoiceMetadata(
            i.KsefNumber,
            i.InvoiceNumber,
            DateOnly.FromDateTime(i.IssueDate.Date),
            i.AcquisitionDate,
            i.PermanentStorageDate,
            i.Seller?.Nip ?? "",
            i.Seller?.Name ?? "",
            i.NetAmount,
            i.VatAmount,
            i.GrossAmount,
            i.Currency ?? "PLN",
            i.InvoiceHash)).ToList();

        return new KsefMetadataPage(invoices, page.HasMore, page.IsTruncated, page.PermanentStorageHwmDate);
    }

    public async Task<string> DownloadInvoiceAsync(string nip, string ksefToken, string ksefNumber, CancellationToken ct = default)
    {
        var accessToken = await GetAccessTokenAsync(nip, ksefToken, ct);
        return await ksefClient.GetInvoiceAsync(ksefNumber, accessToken, ct);
    }

    // ------------------------------------------------------------------ uwierzytelnianie

    /// <summary>
    /// Token dostępu (JWT) dla pary NIP + token KSeF. Przechowywany w pamięci do chwili wygaśnięcia, żeby nie
    /// uwierzytelniać się przy każdym odpytaniu o status. Klucz bufora to skrót tokenu - sam token nie trafia do bufora.
    /// </summary>
    private async Task<string> GetAccessTokenAsync(string nip, string ksefToken, CancellationToken ct, bool useCache = true)
    {
        var key = "ksef-access:" + nip + ":" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ksefToken)));
        if (useCache && cache.TryGetValue(key, out string? cached) && cached is not null) return cached;

        await cryptography.WarmupAsync(ct);

        AuthenticationOperationStatusResponse result;
        try
        {
            result = await authCoordinator.AuthKsefTokenAsync(
                AuthenticationTokenContextIdentifierType.Nip, nip, ksefToken, cryptography, cancellationToken: ct);
        }
        catch (KsefApiException ex)
        {
            throw new KsefAuthenticationException("KSeF odrzucił token (sprawdź, czy token jest aktywny i wystawiony dla tego NIP).", ex);
        }

        if (result.AccessToken?.Token is not { Length: > 0 } accessToken)
            throw new KsefAuthenticationException("KSeF nie wydał tokenu dostępu dla podanego tokenu KSeF.");

        var lifetime = result.AccessToken.ValidUntil - DateTimeOffset.UtcNow - TimeSpan.FromMinutes(1);
        if (lifetime > TimeSpan.Zero) cache.Set(key, accessToken, lifetime);
        return accessToken;
    }

    private async Task<string> AuthenticateWithCertificateAsync(string nip, X509Certificate2 certificate, CancellationToken ct)
    {
        var challenge = await authorizationClient.GetAuthChallengeAsync(ct);
        var request = AuthTokenRequestBuilder.Create()
            .WithChallenge(challenge.Challenge)
            .WithContext(AuthenticationTokenContextIdentifierType.Nip, nip)
            .WithIdentifierType(AuthenticationTokenSubjectIdentifierTypeEnum.CertificateSubject)
            .WithAuthorizationPolicy(null!)
            .Build();

        var signed = SignatureService.Sign(AuthenticationTokenRequestSerializer.SerializeToXmlString(request), certificate);
        var operation = await authorizationClient.SubmitXadesAuthRequestAsync(signed, cancellationToken: ct);

        AuthStatus status;
        var attempt = 0;
        do
        {
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            status = await authorizationClient.GetAuthStatusAsync(operation.ReferenceNumber, operation.AuthenticationToken.Token, ct);
        } while (status.Status.Code == AuthInProgress && ++attempt < 60);

        if (status.Status.Code != AuthSuccess)
            throw new KsefAuthenticationException($"Uwierzytelnienie certyfikatem testowym nieudane: {status.Status.Code} {status.Status.Description}");

        var tokens = await authorizationClient.GetAccessTokenAsync(operation.AuthenticationToken.Token, ct);
        return tokens.AccessToken.Token;
    }
}
