using Microsoft.EntityFrameworkCore;

namespace StockTimeMachine;

public class StockTimeMachineDbContext : DbContext
{
    public StockTimeMachineDbContext(DbContextOptions<StockTimeMachineDbContext> options)
        : base(options)
    {
    }

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<SecFiling> SecFilings => Set<SecFiling>();
    public DbSet<NewsArticle> NewsArticles => Set<NewsArticle>();
    public DbSet<ArticleEmbedding> ArticleEmbeddings => Set<ArticleEmbedding>();
    public DbSet<PricePoint> PricePoints => Set<PricePoint>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Company>(e =>
        {
            e.HasKey(c => c.Symbol);
            e.Property(c => c.Symbol).HasMaxLength(10);
            e.Property(c => c.Name).HasMaxLength(200);
            e.Property(c => c.Cik).HasMaxLength(20);
            e.Property(c => c.Exchange).HasMaxLength(20);
            e.Property(c => c.Sector).HasMaxLength(100);
            e.Property(c => c.Industry).HasMaxLength(200);
        });

        modelBuilder.Entity<SecFiling>(e =>
        {
            e.HasKey(f => f.AccessionNumber);
            e.Property(f => f.AccessionNumber).HasMaxLength(30);
            e.Property(f => f.FormType).HasMaxLength(20);
            e.Property(f => f.CompanySymbol).HasMaxLength(10);
            e.Property(f => f.Url).HasMaxLength(500);
            e.Property(f => f.Summary).HasMaxLength(2000);
            e.HasIndex(f => new { f.CompanySymbol, f.FiledAt });
        });

        modelBuilder.Entity<NewsArticle>(e =>
        {
            e.HasKey(n => n.Id);
            e.Property(n => n.Id).HasMaxLength(100);
            e.Property(n => n.Title).HasMaxLength(500);
            e.Property(n => n.Description).HasMaxLength(2000);
            e.Property(n => n.Source).HasMaxLength(100);
            e.Property(n => n.Url).HasMaxLength(500);
            e.Property(n => n.CompanySymbol).HasMaxLength(10);
            // Scores persist with cached rows: sentiment divergence and the
            // dispersion term read from cache, so dropping scores at the DB
            // boundary silently zeroed them (every window read "unknown").
            e.Property(n => n.SentimentScore).HasPrecision(18, 4);
            e.HasIndex(n => new { n.CompanySymbol, n.PublishedAt });
        });

        modelBuilder.Entity<ArticleEmbedding>(e =>
        {
            e.HasKey(x => x.ArticleId);
            e.Property(x => x.ArticleId).HasMaxLength(100);
            e.Property(x => x.Model).HasMaxLength(100);
        });

        modelBuilder.Entity<PricePoint>(e =>
        {
            e.HasKey(p => new { p.CompanySymbol, p.Date });
            e.Property(p => p.CompanySymbol).HasMaxLength(10);
            // Prices must survive SQL Server round-trips exactly; the default
            // decimal(18,2) would silently round them.
            e.Property(p => p.Open).HasPrecision(18, 4);
            e.Property(p => p.High).HasPrecision(18, 4);
            e.Property(p => p.Low).HasPrecision(18, 4);
            e.Property(p => p.Close).HasPrecision(18, 4);
            e.HasIndex(p => new { p.CompanySymbol, p.Date });
        });
    }
}
