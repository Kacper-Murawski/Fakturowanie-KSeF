using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using FakturowanieKSeF.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace FakturowanieKSeF.Client.Components;

/// <summary>
/// Walidator formularza oparty na tych samych atrybutach DataAnnotations co API (projekt Contracts),
/// ale - w odróżnieniu od standardowego DataAnnotationsValidator - schodzący do obiektów zagnieżdżonych
/// (np. adres) i elementów list (pozycje faktury). Dodatkowo potrafi przypiąć do pól formularza błędy
/// zwrócone przez serwer (np. "Items[1].Quantity" albo konflikt unikalności).
/// </summary>
public sealed class DeepValidator : ComponentBase, IDisposable
{
    [CascadingParameter] private EditContext EditContext { get; set; } = null!;

    private ValidationMessageStore _store = null!;
    private EditContext? _subscribed;

    protected override void OnParametersSet()
    {
        if (_subscribed == EditContext) return;
        Unsubscribe();
        _store = new ValidationMessageStore(EditContext);
        EditContext.OnValidationRequested += OnValidationRequested;
        EditContext.OnFieldChanged += OnFieldChanged;
        _subscribed = EditContext;
    }

    private void OnValidationRequested(object? sender, ValidationRequestedEventArgs e)
    {
        _store.Clear();
        ValidateObject(EditContext.Model, new HashSet<object>(ReferenceEqualityComparer.Instance));
        EditContext.NotifyValidationStateChanged();
    }

    private void OnFieldChanged(object? sender, FieldChangedEventArgs e)
    {
        var field = e.FieldIdentifier;
        _store.Clear(field);

        var property = field.Model.GetType().GetProperty(field.FieldName);
        if (property is not null)
        {
            var results = new List<ValidationResult>();
            var context = new ValidationContext(field.Model) { MemberName = field.FieldName };
            Validator.TryValidateProperty(property.GetValue(field.Model), context, results);
            foreach (var r in results) _store.Add(field, r.ErrorMessage ?? "Nieprawidłowa wartość.");
        }

        EditContext.NotifyValidationStateChanged();
    }

    private void ValidateObject(object model, HashSet<object> visited)
    {
        if (!visited.Add(model)) return;

        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        foreach (var result in results)
        {
            var members = result.MemberNames.Any() ? result.MemberNames : [string.Empty];
            foreach (var member in members) _store.Add(new FieldIdentifier(model, member), result.ErrorMessage ?? "Nieprawidłowa wartość.");
        }

        foreach (var property in model.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || property.PropertyType == typeof(string) || property.PropertyType.IsValueType)
                continue;

            switch (property.GetValue(model))
            {
                case IEnumerable list when property.PropertyType.IsGenericType:
                    foreach (var item in list)
                        if (item is not null && IsOwnType(item.GetType())) ValidateObject(item, visited);
                    break;
                case { } child when IsOwnType(child.GetType()):
                    ValidateObject(child, visited);
                    break;
            }
        }
    }

    private static bool IsOwnType(Type type) => type.Namespace?.StartsWith("FakturowanieKSeF") == true;

    /// <summary>
    /// Przypina błędy serwera do pól formularza. Ścieżki typu "Address.PostalCode" lub "Items[1].Quantity"
    /// są rozwiązywane względem modelu formularza; błędy bez pola trafiają do podsumowania.
    /// </summary>
    public void ShowServerErrors(ApiProblem problem)
    {
        _store.Clear();
        foreach (var (path, messages) in problem.Errors)
        {
            var field = Resolve(EditContext.Model, path);
            foreach (var message in messages) _store.Add(field, message);
        }
        EditContext.NotifyValidationStateChanged();
    }

    private static FieldIdentifier Resolve(object model, string path)
    {
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var current = model;

        for (var i = 0; i < segments.Length; i++)
        {
            var (name, index) = ParseSegment(segments[i]);
            var property = current.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property is null) return new FieldIdentifier(current, string.Empty);

            var isLast = i == segments.Length - 1;
            if (isLast && index is null) return new FieldIdentifier(current, property.Name);

            var value = property.GetValue(current);
            if (index is not null)
                value = value is IList list && index < list.Count ? list[index.Value] : null;

            if (value is null) return new FieldIdentifier(current, property.Name);
            if (isLast) return new FieldIdentifier(value, string.Empty);
            current = value;
        }

        return new FieldIdentifier(model, string.Empty);
    }

    private static (string name, int? index) ParseSegment(string segment)
    {
        var bracket = segment.IndexOf('[');
        if (bracket < 0) return (segment, null);
        var index = int.TryParse(segment[(bracket + 1)..^1], out var n) ? n : (int?)null;
        return (segment[..bracket], index);
    }

    private void Unsubscribe()
    {
        if (_subscribed is null) return;
        _subscribed.OnValidationRequested -= OnValidationRequested;
        _subscribed.OnFieldChanged -= OnFieldChanged;
    }

    public void Dispose() => Unsubscribe();
}
