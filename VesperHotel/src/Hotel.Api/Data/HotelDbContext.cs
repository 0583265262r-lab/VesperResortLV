using Hotel.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Hotel.Api.Data;

public class HotelUser : IdentityUser
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
}

public class HotelDbContext : IdentityDbContext<HotelUser>
{
    public HotelDbContext(DbContextOptions<HotelDbContext> options) : base(options) { }
    public DbSet<CatalogResource> Resources => Set<CatalogResource>();
    public DbSet<ResourceUnit> Units => Set<ResourceUnit>();
    public DbSet<ActivitySession> Sessions => Set<ActivitySession>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<IdempotencyEntry> IdempotencyEntries => Set<IdempotencyEntry>();
    public DbSet<OutboxEvent> Outbox => Set<OutboxEvent>();
    public DbSet<InboxEvent> Inbox => Set<InboxEvent>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditEntry> Audit => Set<AuditEntry>();
    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.Entity<IdentityUserLogin<string>>().Property(x => x.LoginProvider).HasMaxLength(128);
        model.Entity<IdentityUserLogin<string>>().Property(x => x.ProviderKey).HasMaxLength(128);
        model.Entity<IdentityUserToken<string>>().Property(x => x.LoginProvider).HasMaxLength(128);
        model.Entity<IdentityUserToken<string>>().Property(x => x.Name).HasMaxLength(128);
        model.Entity<CatalogResource>().ToTable("Resources");
        model.Entity<Booking>().ToTable("Bookings");
        model.Entity<Booking>().HasDiscriminator(x => x.Kind)
            .HasValue<RoomBooking>(ResourceKind.Room).HasValue<ActivityBooking>(ResourceKind.Activity)
            .HasValue<TransportationBooking>(ResourceKind.Transportation);
        model.Entity<CatalogResource>().HasMany(x => x.Units).WithOne().HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<CatalogResource>().HasMany(x => x.Sessions).WithOne().HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Booking>().HasOne<CatalogResource>().WithMany().HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Booking>().HasOne<HotelUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Booking>().HasOne<ResourceUnit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Booking>().HasOne<ActivitySession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Booking>().HasIndex(x => new { x.ResourceId, x.Status, x.StartUtc, x.InventoryEndUtc });
        model.Entity<Booking>().HasIndex(x => new { x.UserId, x.CreatedAtUtc });
        model.Entity<Booking>().HasIndex(x => x.BookingNumber).IsUnique();
        model.Entity<Booking>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<CatalogResource>().Property(x => x.OfferVersion).IsConcurrencyToken();
        model.Entity<ActivitySession>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<IdempotencyEntry>().Property(x => x.Key).HasMaxLength(120);
        model.Entity<IdempotencyEntry>().Property(x => x.Operation).HasMaxLength(80);
        model.Entity<IdempotencyEntry>().Property(x => x.UserId).HasMaxLength(128);
        model.Entity<IdempotencyEntry>().HasIndex(x => new { x.UserId, x.Operation, x.Key }).IsUnique();
        model.Entity<RefreshSession>().HasIndex(x => x.TokenHash).IsUnique();
        model.Entity<Notification>().HasIndex(x => x.EventId).IsUnique();
        model.Entity<OutboxEvent>().HasIndex(x => new { x.Status, x.NextAttemptUtc });
        model.Entity<CatalogResource>().HasIndex(x => x.Slug).IsUnique();
        // Bound ordinary strings for portable MySQL keys; JSON/snapshots remain long text.
        foreach (var entity in model.Model.GetEntityTypes())
        foreach (var property in entity.GetProperties())
        {
            if (property.ClrType != typeof(string) || property.GetMaxLength() != null) continue;
            var name = property.Name;
            if (name.EndsWith("Json") || name is "Message" or "OldValue" or "NewValue" or "LastError" or "Description") continue;
            property.SetMaxLength(name.EndsWith("Id") || name == "Id" ? 128 : 500);
        }
    }
}
