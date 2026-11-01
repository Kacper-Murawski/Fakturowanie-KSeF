using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;

namespace FakturowanieKSeF.Client.Components;

/// <summary>
/// Pole tekstowe aktualizujące model przy każdym naciśnięciu klawisza (zdarzenie <c>oninput</c>),
/// a nie dopiero po opuszczeniu pola (<c>onchange</c>, jak standardowy <see cref="InputText"/>).
/// <para>
/// Powód: przy <c>onchange</c> poprawienie błędnego pola i od razu kliknięcie „Zapisz” powoduje, że komunikat błędu
/// znika w chwili kliknięcia, układ strony przesuwa się i kliknięcie trafia obok przycisku - formularz
/// „nie reaguje”. Wykryte w teście E2E (decyzja D22). Walidacja na bieżąco usuwa komunikat już podczas pisania.
/// </para>
/// </summary>
public sealed class InputTextLive : InputText
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "input");
        builder.AddMultipleAttributes(1, AdditionalAttributes);
        builder.AddAttribute(2, "class", CssClass);
        builder.AddAttribute(3, "value", CurrentValueAsString);
        builder.AddAttribute(4, "oninput", EventCallback.Factory.CreateBinder<string?>(this, v => CurrentValueAsString = v, CurrentValueAsString));
        builder.SetUpdatesAttributeName("value");
        builder.AddElementReferenceCapture(5, r => Element = r);
        builder.CloseElement();
    }
}
