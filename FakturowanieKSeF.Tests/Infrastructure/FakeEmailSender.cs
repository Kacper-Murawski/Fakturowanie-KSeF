using FakturowanieKSeF.Api.Services.Email;

namespace FakturowanieKSeF.Tests.Infrastructure;

/// <summary>Atrapa serwera poczty: zapamiętuje wysłane wiadomości, może symulować awarię.</summary>
public sealed class FakeEmailSender : IEmailSender
{
    private readonly List<OutgoingEmail> _sent = [];

    /// <summary>Awaria serwera poczty dla kolejnych wysyłek (null - wysyłka działa).</summary>
    public Exception? Failure { get; set; }

    public IReadOnlyList<OutgoingEmail> Sent { get { lock (_sent) return _sent.ToList(); } }

    public Task SendAsync(OutgoingEmail email, CancellationToken ct = default)
    {
        if (Failure is not null) throw Failure;
        lock (_sent) _sent.Add(email);
        return Task.CompletedTask;
    }

    /// <summary>Wiadomości do wskazanego odbiorcy.</summary>
    public List<OutgoingEmail> To(string address) => Sent.Where(m => m.To == address).ToList();
}
