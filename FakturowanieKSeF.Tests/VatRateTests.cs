using FakturowanieKSeF.Models;

namespace FakturowanieKSeF.Tests;

public class VatRateTests
{
    /// <summary>Wartości typu TStawkaPodatku z oficjalnej schemy FA(3) (schemat_FA(3)_v1-0E.xsd).</summary>
    private static readonly string[] KodyZeSchemyFa3 =
        ["23", "22", "8", "7", "5", "4", "3", "0 KR", "0 WDT", "0 EX", "zw", "oo", "np I", "np II"];

    [Fact]
    public void KazdaStawka_MaKodZgodnyZeSchemaFa3_IKazdyKodSchemyJestObslugiwany()
    {
        var kody = Enum.GetValues<VatRate>().Select(r => r.ToFaCode()).ToArray();

        Assert.Equal(KodyZeSchemyFa3.Order(), kody.Order());
    }

    [Theory]
    [InlineData(VatRate.Rate23, 0.23)]
    [InlineData(VatRate.Rate8, 0.08)]
    [InlineData(VatRate.Rate5, 0.05)]
    [InlineData(VatRate.Rate0Domestic, 0)]
    [InlineData(VatRate.Exempt, 0)]
    [InlineData(VatRate.ReverseCharge, 0)]
    [InlineData(VatRate.NotSubjectI, 0)]
    public void Stawka_MaPoprawnyProcent(VatRate rate, double oczekiwany)
    {
        Assert.Equal((decimal)oczekiwany, rate.GetPercentage());
    }
}
