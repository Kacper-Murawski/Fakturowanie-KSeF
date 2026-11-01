using FakturowanieKSeF.Models;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FakturowanieKSeF.Api.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser>, IDataProtectionKeyContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
        // Kaskady wykonywane dopiero przy zapisie - dzięki temu ApplyAuditFields zdąży zamienić usunięcie
        // na miękkie, zanim EF oznaczy zależne encje (np. pozycje faktury) do fizycznego usunięcia.
        ChangeTracker.CascadeDeleteTiming = Microsoft.EntityFrameworkCore.ChangeTracking.CascadeTiming.OnSaveChanges;
        ChangeTracker.DeleteOrphansTiming = Microsoft.EntityFrameworkCore.ChangeTracking.CascadeTiming.OnSaveChanges;
    }

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<KsefSubmission> KsefSubmissions => Set<KsefSubmission>();
    public DbSet<ReceivedInvoice> ReceivedInvoices => Set<ReceivedInvoice>();
    public DbSet<Payment> Payments => Set<Payment>();

    /// <summary>Klucze ASP.NET Core Data Protection (szyfrowanie tokenów KSeF, ciasteczka sesji) - D23.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ---------------- Company ----------------
        builder.Entity<Company>(e =>
        {
            // 1 konto = 1 firma (D4); indeks częściowy, żeby po miękkim usunięciu można było założyć nową
            e.HasIndex(c => c.OwnerId).IsUnique().HasFilter("\"IsDeleted\" = false");

            e.HasOne(c => c.Owner)
                .WithMany()
                .HasForeignKey(c => c.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasQueryFilter(c => !c.IsDeleted);
        });

        // ---------------- Customer ----------------
        builder.Entity<Customer>(e =>
        {
            // Ten sam NIP nie może wystąpić dwa razy w książce adresowej jednej firmy
            e.HasIndex(c => new { c.CompanyId, c.Nip })
                .IsUnique()
                .HasFilter("\"Nip\" IS NOT NULL AND \"IsDeleted\" = false");

            e.HasOne(c => c.Company)
                .WithMany(c => c.Customers)
                .HasForeignKey(c => c.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasQueryFilter(c => !c.IsDeleted);
        });

        // ---------------- Invoice ----------------
        builder.Entity<Invoice>(e =>
        {
            // Numer faktury unikalny w ramach firmy
            e.HasIndex(i => new { i.CompanyId, i.Number })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");

            e.HasIndex(i => i.KsefNumber).IsUnique().HasFilter("\"KsefNumber\" IS NOT NULL");
            e.HasIndex(i => new { i.CompanyId, i.IssueDate });

            e.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(i => i.PaymentMethod).HasConversion<string>().HasMaxLength(20);
            e.Property(i => i.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(i => i.CorrectionType).HasConversion<string>().HasMaxLength(20);

            // Korekta wskazuje fakturę korygowaną; faktury z korektami nie wolno usunąć
            e.HasOne(i => i.CorrectedInvoice)
                .WithMany(i => i.Corrections)
                .HasForeignKey(i => i.CorrectedInvoiceId)
                .OnDelete(DeleteBehavior.Restrict);

            // Kopie danych stron - kolumny Seller_* i Buyer_* w tabeli Invoices
            e.OwnsOne(i => i.Seller, s => s.OwnsOne(p => p.Address));
            e.OwnsOne(i => i.Buyer, b => b.OwnsOne(p => p.Address));

            // Faktur nie wolno usuwać kaskadowo razem z firmą lub kontrahentem (dokumenty podatkowe)
            e.HasOne(i => i.Company)
                .WithMany(c => c.Invoices)
                .HasForeignKey(i => i.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(i => i.Customer)
                .WithMany()
                .HasForeignKey(i => i.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasQueryFilter(i => !i.IsDeleted);
        });

        // ---------------- InvoiceItem ----------------
        builder.Entity<InvoiceItem>(e =>
        {
            e.HasIndex(i => new { i.InvoiceId, i.Position }).IsUnique();
            e.Property(i => i.VatRate).HasConversion<string>().HasMaxLength(20);

            // Pozycje są częścią faktury (agregat) - usuwane razem z nią
            e.HasOne(i => i.Invoice)
                .WithMany(i => i.Items)
                .HasForeignKey(i => i.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);

            // Spójny filtr z fakturą (pozycje usuniętej faktury też są niewidoczne)
            e.HasQueryFilter(i => !i.Invoice.IsDeleted);
        });

        // ---------------- KsefSubmission ----------------
        builder.Entity<KsefSubmission>(e =>
        {
            e.HasIndex(s => s.InvoiceId);
            e.HasIndex(s => s.InvoiceReferenceNumber);
            e.HasIndex(s => s.CompletedAt); // wyszukiwanie wysyłek w toku przez usługę w tle

            // Historii wysyłek do KSeF nie wolno usuwać razem z fakturą
            e.HasOne(s => s.Invoice)
                .WithMany(i => i.KsefSubmissions)
                .HasForeignKey(s => s.InvoiceId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasQueryFilter(s => !s.Invoice.IsDeleted);
        });

        // ---------------- ReceivedInvoice ----------------
        builder.Entity<ReceivedInvoice>(e =>
        {
            e.HasIndex(r => new { r.CompanyId, r.KsefNumber }).IsUnique();
            e.HasIndex(r => new { r.CompanyId, r.IssueDate });

            e.HasOne(r => r.Company)
                .WithMany(c => c.ReceivedInvoices)
                .HasForeignKey(r => r.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasQueryFilter(r => !r.Company.IsDeleted);
        });

        // ---------------- Payment ----------------
        builder.Entity<Payment>(e =>
        {
            // Płatność dotyczy dokładnie jednej faktury: wystawionej albo otrzymanej
            e.ToTable(t => t.HasCheckConstraint("CK_Payments_OneInvoice",
                "(\"InvoiceId\" IS NULL) <> (\"ReceivedInvoiceId\" IS NULL)"));
            e.HasIndex(p => p.CompanyId);

            e.HasOne(p => p.Company).WithMany().HasForeignKey(p => p.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.Invoice).WithMany(i => i.Payments).HasForeignKey(p => p.InvoiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.ReceivedInvoice).WithMany(r => r.Payments).HasForeignKey(p => p.ReceivedInvoiceId).OnDelete(DeleteBehavior.Restrict);

            e.HasQueryFilter(p => !p.Company.IsDeleted);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyAuditFields();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyAuditFields();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Automatycznie ustawia znaczniki czasu encji oraz zamienia fizyczne usunięcie encji
    /// <see cref="BaseEntity"/> na miękkie (IsDeleted = true).
    /// </summary>
    private void ApplyAuditFields()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = null;
                    break;

                case EntityState.Modified:
                    entry.Property(e => e.CreatedAt).IsModified = false;
                    entry.Entity.UpdatedAt = now;
                    break;

                case EntityState.Deleted:
                    entry.State = EntityState.Modified;
                    entry.Entity.IsDeleted = true;
                    entry.Entity.UpdatedAt = now;
                    break;
            }
        }
    }
}
