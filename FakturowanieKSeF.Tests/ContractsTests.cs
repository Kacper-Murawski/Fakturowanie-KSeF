using FakturowanieKSeF.Api.Services;
using FakturowanieKSeF.Contracts.Calculation;
using FakturowanieKSeF.Contracts.Validation;

namespace FakturowanieKSeF.Tests;

public class NipValidatorTests
{
    [Theory]
    [InlineData("5260250274")]       // poprawny NIP
    [InlineData("526-025-02-74")]    // z myślnikami
    [InlineData("526 025 02 74")]    // ze spacjami
    [InlineData("PL5260250274")]     // z prefiksem kraju
    [InlineData("1234563218")]
    public void PoprawnyNip_JestAkceptowany(string nip) => Assert.True(NipValidator.IsValid(nip));

    [Theory]
    [InlineData("5260250275")]       // zła cyfra kontrolna
    [InlineData("526025027")]        // za krótki
    [InlineData("52602502744")]      // za długi
    [InlineData("52602502A4")]       // litera
    [InlineData("")]
    [InlineData(null)]
    public void NiepoprawnyNip_JestOdrzucany(string? nip) => Assert.False(NipValidator.IsValid(nip));

    [Fact]
    public void Normalizacja_UsuwaSeparatoryIPrefiks() =>
        Assert.Equal("5260250274", NipValidator.Normalize(" pl526-025-02 74 "));
}

public class InvoiceCalculatorTests
{
    [Fact]
    public void Pozycja_NettoZaokraglaneDoGroszy_VatOdNetto()
    {
        var line = InvoiceCalculator.CalculateLine(new LineInput(3, 33.333m, VatRate.Rate23));

        Assert.Equal(100.00m, line.NetAmount);   // 99,999 -> 100,00
        Assert.Equal(23.00m, line.VatAmount);
        Assert.Equal(123.00m, line.GrossAmount);
    }

    [Fact]
    public void ZaokraglenieGroszy_JestPolowkoweOdZera()
    {
        Assert.Equal(0.01m, InvoiceCalculator.RoundMoney(0.005m));
        Assert.Equal(2.35m, InvoiceCalculator.RoundMoney(2.345m)); // bankowe dałoby 2,34
    }

    [Fact]
    public void VatFaktury_LiczonyOdSumyNettoWStawce_NieSumaVatPozycji()
    {
        // 3 pozycje po 0,10 zł netto w stawce 23%: VAT każdej pozycji = 0,02 (0,023), suma pozycji = 0,06,
        // ale VAT od sumy netto 0,30 zł = 0,069 -> 0,07. Wiążąca jest kwota per stawka (P_14_1).
        var totals = InvoiceCalculator.Calculate(
        [
            new LineInput(1, 0.10m, VatRate.Rate23),
            new LineInput(1, 0.10m, VatRate.Rate23),
            new LineInput(1, 0.10m, VatRate.Rate23)
        ]);

        Assert.Equal(0.30m, totals.TotalNet);
        Assert.Equal(0.07m, totals.TotalVat);
        Assert.Equal(0.37m, totals.TotalGross);
        Assert.Equal(0.06m, totals.Lines.Sum(l => l.VatAmount));
    }

    [Fact]
    public void Podsumowanie_GrupujePoStawkach()
    {
        var totals = InvoiceCalculator.Calculate(
        [
            new LineInput(2, 100m, VatRate.Rate23),
            new LineInput(1, 50m, VatRate.Rate8),
            new LineInput(1, 10m, VatRate.Exempt),
            new LineInput(1, 100m, VatRate.Rate23)
        ]);

        Assert.Equal(3, totals.ByVatRate.Count);
        var s23 = totals.ByVatRate.Single(s => s.VatRate == VatRate.Rate23);
        Assert.Equal((300m, 69m), (s23.Net, s23.Vat));
        var s8 = totals.ByVatRate.Single(s => s.VatRate == VatRate.Rate8);
        Assert.Equal((50m, 4m), (s8.Net, s8.Vat));
        var zw = totals.ByVatRate.Single(s => s.VatRate == VatRate.Exempt);
        Assert.Equal((10m, 0m), (zw.Net, zw.Vat));

        Assert.Equal(360m, totals.TotalNet);
        Assert.Equal(73m, totals.TotalVat);
        Assert.Equal(433m, totals.TotalGross);
    }

    [Fact]
    public void NumerFaktury_MaFormatFvNumerMiesiacRok() =>
        Assert.Equal("FV/7/03/2026", InvoiceNumbering.Format(7, new DateOnly(2026, 3, 15)));
}
