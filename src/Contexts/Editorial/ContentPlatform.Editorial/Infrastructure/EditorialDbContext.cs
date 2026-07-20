using ContentPlatform.Editorial.Domain;
using Microsoft.EntityFrameworkCore;

namespace ContentPlatform.Editorial.Infrastructure;

public sealed class EditorialDbContext(DbContextOptions<EditorialDbContext> options) : DbContext(options)
{
    public const string Schema = "editorial";

    public DbSet<ContentItem> ContentItems => Set<ContentItem>();
    public DbSet<ContentRevision> ContentRevisions => Set<ContentRevision>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<ContentAuditEntry> ContentAudit => Set<ContentAuditEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);

        b.Entity<ContentItem>(e =>
        {
            e.ToTable("content_items");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();   // Id domain'de (Guid.NewGuid) atanır; EF üretmez.
            e.Property(x => x.SourceHash).IsRequired().HasMaxLength(128);
            e.HasIndex(x => x.SourceHash).IsUnique();            // dedup
            e.Property(x => x.CreatedByRef).IsRequired().HasMaxLength(1000); // "ingestion:<uzun Google News URL>" sığsın (truncation hatası düzeltmesi)
            e.Property(x => x.RawTitle).HasMaxLength(500);
            e.HasIndex(x => new { x.EditorialStatus, x.MediaStatus });
            e.Property(x => x.Origin).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.ImageSource).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.RiskLevel).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.EditorialStatus).HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.MediaStatus).HasConversion<string>().HasMaxLength(24);
            e.HasMany(x => x.Revisions).WithOne().HasForeignKey(r => r.ContentItemId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Revisions).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.HasMany(x => x.Media).WithOne().HasForeignKey(m => m.ContentItemId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Media).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        b.Entity<ContentRevision>(e =>
        {
            e.ToTable("content_revisions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();   // ← "0 satır etkilendi" hatasının kök çözümü:
            // Id boş değil (domain'de atanıyor); EF bunu "var olan kayıt" sanıp UPDATE etmesin, INSERT etsin.
            e.Property(x => x.Title).IsRequired().HasMaxLength(300);
            e.Property(x => x.ShortX).HasMaxLength(400);
            e.Property(x => x.Tags)
                .HasConversion(
                    v => string.Join('\n', v),
                    v => v.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList(),
                    // ValueComparer: koleksiyon değişiklikleri öğe bazında izlensin (log'daki
                    // "collection type with a value converter but with no value comparer" uyarısının çözümü)
                    new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<string>>(
                        (a, b) => (a ?? new List<string>()).SequenceEqual(b ?? new List<string>()),
                        v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
                        v => v.ToList()));
            e.HasIndex(x => new { x.ContentItemId, x.RevisionNumber }).IsUnique();
        });

        b.Entity<MediaAsset>(e =>
        {
            e.ToTable("media_assets");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Url).IsRequired().HasMaxLength(1000);
        });

        b.Entity<ContentAuditEntry>(e =>
        {
            e.ToTable("content_audit");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => new { x.ContentItemId, x.CreatedAt });
            e.Property(x => x.Event).HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.ActorType).HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.ActorRef).IsRequired().HasMaxLength(1000); // "ingestion:<uzun Google News URL>" sığsın (truncation hatası düzeltmesi)
            e.Property(x => x.Detail).HasMaxLength(1000);
        });
    }
}
