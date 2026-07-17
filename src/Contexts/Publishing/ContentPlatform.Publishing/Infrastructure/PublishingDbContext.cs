using ContentPlatform.Publishing.Domain;
using Microsoft.EntityFrameworkCore;

namespace ContentPlatform.Publishing.Infrastructure;

public sealed class PublishingDbContext(DbContextOptions<PublishingDbContext> options) : DbContext(options)
{
    public const string Schema = "publishing";

    public DbSet<Publication> Publications => Set<Publication>();
    public DbSet<DeliveryAttempt> DeliveryAttempts => Set<DeliveryAttempt>();
    public DbSet<UsageRecord> UsageRecords => Set<UsageRecord>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);

        b.Entity<Publication>(e =>
        {
            e.ToTable("publications");
            e.HasKey(x => x.Id);
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.TargetRef).IsRequired().HasMaxLength(128);
            e.Property(x => x.PayloadJson).IsRequired().HasColumnType("nvarchar(max)");
            e.HasIndex(x => new { x.ContentItemId, x.Channel, x.TargetRef }).IsUnique(); // idempotency
            e.HasIndex(x => new { x.Status, x.Attempts });                                // retry sorgusu
            e.HasIndex(x => new { x.Status, x.ScheduledAt });                             // planlı gönderim sorgusu
            e.HasIndex(x => new { x.CategoryId, x.Status });                              // kategori slot hesabı
            e.HasMany(x => x.DeliveryAttempts).WithOne().HasForeignKey(a => a.PublicationId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.DeliveryAttempts).HasField("_attempts").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        b.Entity<UsageRecord>(e =>
        {
            e.ToTable("usage_records");
            e.HasKey(x => x.Id);
            e.Property(x => x.Provider).HasMaxLength(32);
            e.Property(x => x.Operation).HasMaxLength(32);
            e.Property(x => x.CostUsd).HasColumnType("decimal(18,6)");
            e.HasIndex(x => x.CreatedAt);
        });

        b.Entity<DeliveryAttempt>(e =>
        {
            e.ToTable("delivery_attempts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(16);
        });

        // Tüm Guid birincil anahtarlar uygulamada üretilir (Entity.Id = Guid.NewGuid()); EF üretmesin.
        // Aksi halde tracked parent'a navigation'la eklenen yeni child INSERT yerine UPDATE'lenir (0 satır → hata).
        foreach (var key in b.Model.GetEntityTypes().Select(t => t.FindPrimaryKey()))
            if (key is { Properties.Count: 1 } && key.Properties[0].ClrType == typeof(Guid))
                key.Properties[0].ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
    }
}
