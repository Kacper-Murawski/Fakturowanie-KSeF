namespace FakturowanieKSeF.Models;

/// <summary>
/// Licznik wiadomości e-mail na dzień (czasu polskiego) dla klucza limitu: firmy (<c>company:{id}</c>) albo całej
/// aplikacji (<c>global</c>). Zwiększany atomowo jednym poleceniem SQL - równoległe żądania nie przekroczą limitu
/// (D53, SEC-4). Historia wiadomości zostaje w <see cref="EmailMessage"/>.
/// </summary>
public class EmailQuota
{
    public string Key { get; set; } = string.Empty;

    public DateOnly Day { get; set; }

    public int Count { get; set; }
}
