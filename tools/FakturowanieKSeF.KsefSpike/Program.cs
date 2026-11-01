// =====================================================================================================
//  Spike KSeF (etap 1) - prototyp pełnego przepływu wysyłki faktury do ŚRODOWISKA TESTOWEGO KSeF 2.0
// =====================================================================================================
//  Kroki:
//   1. losowy, poprawny NIP testowy + samopodpisany certyfikat (dopuszczalne tylko na środowisku TEST),
//   2. uwierzytelnienie: wyzwanie -> podpis XAdES -> status -> token dostępu (JWT),
//   3. otwarcie sesji interaktywnej (online) z szyfrowaniem AES-256 (klucz szyfrowany kluczem publicznym MF),
//   4. wysłanie zaszyfrowanej faktury FA(3),
//   5. oczekiwanie na przetworzenie -> numer KSeF,
//   6. zamknięcie sesji i pobranie UPO (urzędowe poświadczenie odbioru).
//  Wyniki (XML faktury, UPO, podsumowanie) zapisywane są w katalogu spike-output/.
//
//  BEZPIECZEŃSTWO: klient rejestrowany jest przez AddKsefTestClient(), który wymusza adres testowy
//  i dopina KsefTestOnlyHandler - żądanie do produkcyjnego KSeF zostanie zablokowane przed wysłaniem.
// =====================================================================================================

using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FakturowanieKSeF.Contracts;
using FakturowanieKSeF.Contracts.Calculation;
using FakturowanieKSeF.Ksef;
using FakturowanieKSeF.Ksef.Fa3;
using FakturowanieKSeF.Models;
using KSeF.Client.Api.Builders.Auth;
using KSeF.Client.Api.Builders.Online;
using KSeF.Client.Api.Builders.X509Certificates;
using KSeF.Client.Api.Services;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.Core.Models;
using KSeF.Client.Core.Models.Authorization;
using KSeF.Client.Core.Models.Invoices;
using KSeF.Client.Core.Models.Sessions;
using KSeF.Client.Core.Models.Sessions.OnlineSession;
using Microsoft.Extensions.DependencyInjection;

var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "spike-output", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
Directory.CreateDirectory(outputDir);
var stopwatch = Stopwatch.StartNew();

void Krok(string opis) => Console.WriteLine($"[{stopwatch.Elapsed:mm\\:ss}] {opis}");

var services = new ServiceCollection();
services.AddLogging();
services.AddKsefTestClient();
await using var provider = services.BuildServiceProvider();

var ksefClient = provider.GetRequiredService<IKSeFClient>();
var authClient = provider.GetRequiredService<IAuthorizationClient>();
var crypto = provider.GetRequiredService<ICryptographyService>();

Krok($"Środowisko: {KsefEnvironments.GetBaseUri(KsefEnvironment.Test)} (TESTOWE)");

// --- 0. Certyfikaty publiczne MF (do szyfrowania klucza sesji) ---------------------------------------
Krok("Pobieranie certyfikatów publicznych MF...");
await crypto.WarmupAsync();

// --- 1. Tożsamość testowa ------------------------------------------------------------------------------
var nip = TestNip.Random();
Krok($"Losowy NIP testowy: {nip}");

using X509Certificate2 certyfikat = SelfSignedCertificateForSignatureBuilder
    .Create()
    .WithGivenName("Jan")
    .WithSurname("Testowy")
    .WithSerialNumber($"TINPL-{nip}")
    .WithCommonName("Jan Testowy")
    .Build();

// --- 2. Uwierzytelnienie XAdES -------------------------------------------------------------------------
Krok("Uwierzytelnianie: pobranie wyzwania...");
var challenge = await authClient.GetAuthChallengeAsync();

var authRequest = AuthTokenRequestBuilder
    .Create()
    .WithChallenge(challenge.Challenge)
    .WithContext(AuthenticationTokenContextIdentifierType.Nip, nip)
    .WithIdentifierType(AuthenticationTokenSubjectIdentifierTypeEnum.CertificateSubject)
    .WithAuthorizationPolicy(null!)
    .Build();

var signedXml = SignatureService.Sign(AuthenticationTokenRequestSerializer.SerializeToXmlString(authRequest), certyfikat);
var authOperation = await authClient.SubmitXadesAuthRequestAsync(signedXml);
Krok($"Wysłano podpisane żądanie, numer operacji: {authOperation.ReferenceNumber}");

AuthStatus authStatus;
var proba = 0;
do
{
    await Task.Delay(1000);
    authStatus = await authClient.GetAuthStatusAsync(authOperation.ReferenceNumber, authOperation.AuthenticationToken.Token);
} while (authStatus.Status.Code == 100 && ++proba < 60);

if (authStatus.Status.Code != 200)
    throw new InvalidOperationException($"Uwierzytelnienie nieudane: {authStatus.Status.Code} {authStatus.Status.Description}");

var tokens = await authClient.GetAccessTokenAsync(authOperation.AuthenticationToken.Token);
var accessToken = tokens.AccessToken.Token;
Krok("Uwierzytelniono, otrzymano token dostępu.");

// --- 3. Sesja interaktywna -----------------------------------------------------------------------------
var encryption = crypto.GetEncryptionData();
var openRequest = OpenOnlineSessionRequestBuilder
    .Create()
    .WithFormCode(
        systemCode: SystemCodeHelper.GetSystemCode(SystemCode.FA3),
        schemaVersion: SystemCodeHelper.GetSchemaVersion(SystemCode.FA3),
        value: SystemCodeHelper.GetValue(SystemCode.FA3))
    .WithEncryption(
        encryptedSymmetricKey: encryption.EncryptionInfo.EncryptedSymmetricKey,
        initializationVector: encryption.EncryptionInfo.InitializationVector,
        publicKeyId: encryption.EncryptionInfo.PublicKeyId)
    .Build();

var session = await ksefClient.OpenOnlineSessionAsync(openRequest, accessToken);
Krok($"Otwarto sesję interaktywną: {session.ReferenceNumber}");

// --- 4. Faktura FA(3) ----------------------------------------------------------------------------------
// Tryb domyślny: przykładowa faktura z repozytorium MF. Tryb --generated: faktura zbudowana przez generator
// aplikacji (Fa3XmlGenerator) - weryfikuje, że KSeF przyjmuje XML wytwarzany przez naszą aplikację.
byte[] invoiceBytes;
if (args.Contains("--generated"))
{
    var invoice = SampleInvoice.Create(nip);
    invoiceBytes = new Fa3XmlGenerator().GenerateBytes(invoice);
    var validation = new Fa3SchemaValidator().Validate(invoiceBytes);
    if (!validation.IsValid)
        throw new InvalidOperationException("XML niezgodny ze schematem: " + string.Join("; ", validation.Errors));
    Krok($"Wygenerowano fakturę {invoice.Number} generatorem aplikacji (zgodna ze schematem FA(3)).");
}
else
{
    var templatePath = Path.Combine(AppContext.BaseDirectory, "Templates", "faktura-fa3-szablon.xml");
    var dzis = DateTime.UtcNow.ToString("yyyy-MM-dd");
    var xml = File.ReadAllText(templatePath, Encoding.UTF8)
        .Replace("#nip#", nip)
        .Replace("#invoice_number#", Guid.NewGuid().ToString("N")[..8])
        .Replace("2025-09-01T00:00:00Z", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"))
        .Replace("<P_1>2025-09-01</P_1>", $"<P_1>{dzis}</P_1>");
    invoiceBytes = new UTF8Encoding(false).GetBytes(xml);
}
File.WriteAllBytes(Path.Combine(outputDir, "faktura.xml"), invoiceBytes);

var encryptedInvoice = crypto.EncryptBytesWithAES256(invoiceBytes, encryption.CipherKey, encryption.CipherIv);
var invoiceMeta = crypto.GetMetaData(invoiceBytes);
var encryptedMeta = crypto.GetMetaData(encryptedInvoice);

var sendRequest = SendInvoiceOnlineSessionRequestBuilder
    .Create()
    .WithInvoiceHash(invoiceMeta.HashSHA, invoiceMeta.FileSize)
    .WithEncryptedDocumentHash(encryptedMeta.HashSHA, encryptedMeta.FileSize)
    .WithEncryptedDocumentContent(Convert.ToBase64String(encryptedInvoice))
    .Build();

var sent = await ksefClient.SendOnlineSessionInvoiceAsync(sendRequest, session.ReferenceNumber, accessToken);
Krok($"Wysłano fakturę, numer referencyjny: {sent.ReferenceNumber}");

// --- 5. Status faktury -> numer KSeF -------------------------------------------------------------------
SessionInvoice invoiceStatus;
proba = 0;
do
{
    await Task.Delay(1000);
    invoiceStatus = await ksefClient.GetSessionInvoiceAsync(session.ReferenceNumber, sent.ReferenceNumber, accessToken);
} while (invoiceStatus.Status.Code == 150 && ++proba < 120);

Krok($"Status faktury: {invoiceStatus.Status.Code} {invoiceStatus.Status.Description}");
if (string.IsNullOrEmpty(invoiceStatus.KsefNumber))
{
    var szczegoly = invoiceStatus.Status.Details is null ? "" : string.Join("; ", invoiceStatus.Status.Details);
    throw new InvalidOperationException($"Faktura nie otrzymała numeru KSeF. {szczegoly}");
}
Krok($"NUMER KSeF: {invoiceStatus.KsefNumber}");

// --- 6. Zamknięcie sesji i UPO -------------------------------------------------------------------------
await ksefClient.CloseOnlineSessionAsync(session.ReferenceNumber, accessToken);
Krok("Zamknięto sesję, oczekiwanie na UPO...");

string? upo = null;
for (proba = 0; proba < 60 && upo is null; proba++)
{
    await Task.Delay(2000);
    try
    {
        upo = await ksefClient.GetSessionInvoiceUpoByKsefNumberAsync(session.ReferenceNumber, invoiceStatus.KsefNumber, accessToken, CancellationToken.None);
    }
    catch (Exception ex) when (ex is not KsefProductionBlockedException)
    {
        // UPO jeszcze niegotowe - ponawiamy
    }
}

if (upo is not null)
{
    File.WriteAllText(Path.Combine(outputDir, "upo.xml"), upo, Encoding.UTF8);
    Krok("Pobrano UPO.");
}
else
{
    Krok("UWAGA: nie udało się pobrać UPO w zadanym czasie.");
}

var podsumowanie = $"""
    Spike KSeF - środowisko TESTOWE ({KsefEnvironments.GetBaseUri(KsefEnvironment.Test)})
    Data:              {DateTime.Now:yyyy-MM-dd HH:mm:ss}
    NIP testowy:       {nip}
    Sesja:             {session.ReferenceNumber}
    Faktura (ref.):    {sent.ReferenceNumber}
    Numer KSeF:        {invoiceStatus.KsefNumber}
    UPO:               {(upo is null ? "brak" : "upo.xml")}
    Czas całkowity:    {stopwatch.Elapsed:mm\:ss}
    """;
File.WriteAllText(Path.Combine(outputDir, "podsumowanie.txt"), podsumowanie, Encoding.UTF8);
Console.WriteLine();
Console.WriteLine(podsumowanie);
Console.WriteLine($"Wyniki zapisano w: {outputDir}");

/// <summary>Generator losowych, poprawnych (z sumą kontrolną) numerów NIP do testów.</summary>
static class TestNip
{
    private static readonly int[] Wagi = [6, 5, 7, 2, 3, 4, 5, 6, 7];

    public static string Random()
    {
        while (true)
        {
            var cyfry = new int[9];
            cyfry[0] = System.Random.Shared.Next(1, 10);
            cyfry[1] = System.Random.Shared.Next(1, 10);
            for (var i = 2; i < 9; i++) cyfry[i] = System.Random.Shared.Next(0, 10);

            var suma = 0;
            for (var i = 0; i < 9; i++) suma += cyfry[i] * Wagi[i];
            var kontrolna = suma % 11;
            if (kontrolna == 10) continue;

            return string.Concat(cyfry) + kontrolna;
        }
    }
}

/// <summary>Przykładowa faktura (encja aplikacji) z różnymi stawkami, nabywcą B2B i płatnością przelewem.</summary>
static class SampleInvoice
{
    public static Invoice Create(string sellerNip)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var items = new List<InvoiceItem>
        {
            Item(1, "Usługa programistyczna", "godz.", 10, 150m, VatRate.Rate23),
            Item(2, "Książka „Wzorce projektowe”", "szt.", 1, 49.90m, VatRate.Rate5),
            Item(3, "Szkolenie zwolnione z VAT", "usł.", 1, 500m, VatRate.Exempt)
        };
        var totals = InvoiceCalculator.Calculate(items.Select(i => new LineInput(i.Quantity, i.UnitNetPrice, i.VatRate)));

        return new Invoice
        {
            Number = $"FV/{Random.Shared.Next(1000, 9999)}/{today:MM}/{today:yyyy}",
            IssueDate = today,
            IssuePlace = "Warszawa",
            Currency = "PLN",
            PaymentMethod = PaymentMethod.Transfer,
            DueDate = today.AddDays(14),
            BankAccountNumber = "61109010140000071219812874",
            ExemptionBasis = "art. 43 ust. 1 pkt 29 lit. c ustawy o VAT",
            Seller = new InvoiceParty
            {
                Name = "Pracownia Testowa (dane testowe)", Nip = sellerNip,
                Address = new Address { Street = "ul. Testowa 1", PostalCode = "00-001", City = "Warszawa", CountryCode = "PL" }
            },
            Buyer = new InvoiceParty
            {
                Name = "Kontrahent Testowy S.A.", Nip = "1234563218",
                Address = new Address { Street = "ul. Polna 5", PostalCode = "30-001", City = "Kraków", CountryCode = "PL" }
            },
            Items = items,
            TotalNet = totals.TotalNet, TotalVat = totals.TotalVat, TotalGross = totals.TotalGross
        };
    }

    private static InvoiceItem Item(int position, string name, string unit, decimal qty, decimal price, VatRate rate)
    {
        var line = InvoiceCalculator.CalculateLine(new LineInput(qty, price, rate));
        return new InvoiceItem
        {
            Position = position, Name = name, Unit = unit, Quantity = qty, UnitNetPrice = price, VatRate = rate,
            NetAmount = line.NetAmount, VatAmount = line.VatAmount, GrossAmount = line.GrossAmount
        };
    }
}
