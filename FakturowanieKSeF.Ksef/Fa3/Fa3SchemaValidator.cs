using System.Reflection;
using System.Xml;
using System.Xml.Schema;

namespace FakturowanieKSeF.Ksef.Fa3;

/// <summary>Wynik walidacji XML względem schematu FA(3).</summary>
public sealed record Fa3ValidationResult(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Walidacja XML faktury względem oficjalnego schematu FA(3) przed wysyłką do KSeF.
/// Schematy są osadzone w aplikacji (zasoby): import typów z crd.gov.pl jest przekierowywany na lokalną kopię,
/// dzięki czemu walidacja nie wymaga dostępu do internetu i jest odporna na niedostępność serwerów MF.
/// Wykrywa też wymagania techniczne KSeF wykraczające poza XSD (znacznik BOM).
/// </summary>
public sealed class Fa3SchemaValidator
{
    private const string MainSchema = "Fa3.Schemas.schemat_FA3_v1-0E.xsd";

    /// <summary>Katalog schematów typów MF, z którego schemat FA(3) importuje definicje (adres z pliku XSD).</summary>
    private const string TypesBaseUrl = "http://crd.gov.pl/xml/schematy/dziedzinowe/mf/2022/01/05/eD/DefinicjeTypy/";

    /// <summary>Schematy bazowe MF osadzone w aplikacji (CIRFMF/ksef-docs: faktury/schemy/FA/bazowe).</summary>
    private static readonly HashSet<string> EmbeddedTypeSchemas =
        ["StrukturyDanych_v10-0E.xsd", "ElementarneTypyDanych_v10-0E.xsd", "KodyKrajow_v10-0E.xsd"];

    // Zbiór schematów jest kompilowany raz (kosztowna operacja) i współdzielony - XmlSchemaSet po kompilacji
    // jest bezpieczny do równoległego odczytu podczas walidacji.
    private static readonly Lazy<XmlSchemaSet> Schemas = new(LoadSchemas);

    public Fa3ValidationResult Validate(byte[] xml)
    {
        var errors = new List<string>();

        if (xml.Length >= 3 && xml[0] == 0xEF && xml[1] == 0xBB && xml[2] == 0xBF)
            errors.Add("Plik zawiera znacznik BOM - KSeF wymaga kodowania UTF-8 bez BOM.");

        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = Schemas.Value,
            DtdProcessing = DtdProcessing.Prohibit, // ochrona przed atakami XXE
            XmlResolver = null
        };
        settings.ValidationEventHandler += (_, e) =>
            errors.Add($"Wiersz {e.Exception.LineNumber}, kolumna {e.Exception.LinePosition}: {e.Message}");

        try
        {
            using var reader = XmlReader.Create(new MemoryStream(xml), settings);
            while (reader.Read()) { }
        }
        catch (XmlException ex)
        {
            errors.Add($"Niepoprawny XML: {ex.Message}");
        }

        return new Fa3ValidationResult(errors);
    }

    private static XmlSchemaSet LoadSchemas()
    {
        var set = new XmlSchemaSet { XmlResolver = new EmbeddedSchemaResolver() };
        using var main = OpenResource(MainSchema);
        set.Add(null, XmlReader.Create(main, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit }, "embedded://" + MainSchema));
        set.Compile();
        return set;
    }

    private static Stream OpenResource(string name) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
        ?? throw new InvalidOperationException($"Brak osadzonego schematu {name}.");

    /// <summary>
    /// Przekierowuje odwołania do schematów typów MF (crd.gov.pl) na kopie osadzone w aplikacji.
    /// Każdy inny adres jest blokowany - schemat nie może pobrać niczego z sieci ani z dysku.
    /// </summary>
    private sealed class EmbeddedSchemaResolver : XmlResolver
    {
        public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            var uri = absoluteUri.AbsoluteUri;
            var fileName = uri.StartsWith(TypesBaseUrl, StringComparison.Ordinal) ? uri[TypesBaseUrl.Length..] : null;

            return fileName is not null && EmbeddedTypeSchemas.Contains(fileName)
                ? OpenResource("Fa3.Schemas." + fileName)
                : throw new XmlException($"Niedozwolone odwołanie do zewnętrznego zasobu: {absoluteUri}");
        }
    }
}
