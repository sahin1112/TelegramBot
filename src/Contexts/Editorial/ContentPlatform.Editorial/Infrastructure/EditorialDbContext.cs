using ContentPlatform.Editorial.Domain;
using Microsoft.EntityFrameworkCore;

namespace ContentPlatform.Editorial.Infrastructure;

public sealed class EditorialDbContext(DbContextOptions<EditorialDbContext> options) : DbContext(options)
{
    public const string Schema = "editorial";

    public DbSet<ContentItem> ContentItems => Set<ContentItem>();
    public DbSet<ContentRevision> ContentRevisions => Set<ContentRevision>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);

        b.Entity<ContentItem>(e =>
        {
            e.ToTable("content_items");
            e.HasKey(x => x.Id);
            e.Property(x => x.SourceHash).IsRequired().HasMaxLength(128);
            e.HasIndex(x => x.SourceHash).IsUnique();            // dedup
            e.Property(x => x.CreatedByRef).IsRequired().HasMaxLength(200);
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
            e.Property(x => x.Title).IsRequired().HasMaxLength(300);
            e.Property(x => x.ShortX).HasMaxLength(400);
            e.Property(x => x.Tags)
                .HasConversion(
                    v => string.Join('\n', v),
                    v => v.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList());
            e.HasIndex(x => new { x.ContentItemId, x.RevisionNumber }).IsUnique();
        });

        b.Entity<MediaAsset>(e =>
        {
            e.ToTable("media_assets");
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Url).IsRequired().HasMaxLength(1000);
        });
    }
}
