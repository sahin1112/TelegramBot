using ContentPlatform.Site.Domain;
using Microsoft.EntityFrameworkCore;

namespace ContentPlatform.Site.Infrastructure;

public sealed class SiteDbContext(DbContextOptions<SiteDbContext> options) : DbContext(options)
{
    public const string Schema = "site";

    public DbSet<BlogPost> BlogPosts => Set<BlogPost>();
    public DbSet<Comment> Comments => Set<Comment>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);

        b.Entity<BlogPost>(e =>
        {
            e.ToTable("blog_posts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Slug).IsRequired().HasMaxLength(140);
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => x.ContentItemId).IsUnique();          // idempotency
            e.HasIndex(x => x.PublishedAt);
            e.Property(x => x.Title).IsRequired().HasMaxLength(300);
            e.Property(x => x.MetaDescription).HasMaxLength(320);
            e.Property(x => x.BodyHtml).IsRequired();
            e.Property(x => x.CoverImageUrl).HasMaxLength(1000);
            e.Property(x => x.CoverImageAlt).HasMaxLength(300);
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
        });

        b.Entity<Comment>(e =>
        {
            e.ToTable("comments");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.BlogPostId, x.Status });
            e.HasIndex(x => x.Status);
            e.Property(x => x.AuthorName).IsRequired().HasMaxLength(80);
            e.Property(x => x.AuthorEmail).HasMaxLength(200);
            e.Property(x => x.Body).IsRequired().HasMaxLength(4000);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.IpHash).IsRequired().HasMaxLength(64);
        });

        // Tüm Guid birincil anahtarlar uygulamada üretilir (Entity.Id = Guid.NewGuid()); EF üretmesin.
        // Aksi halde tracked parent'a navigation'la eklenen yeni child INSERT yerine UPDATE'lenir (0 satır → hata).
        foreach (var key in b.Model.GetEntityTypes().Select(t => t.FindPrimaryKey()))
            if (key is { Properties.Count: 1 } && key.Properties[0].ClrType == typeof(Guid))
                key.Properties[0].ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
    }
}
