using Microsoft.EntityFrameworkCore;

namespace SchemaDoc.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<SavedConnection> Connections => Set<SavedConnection>();
    public DbSet<TableAnnotation> TableAnnotations => Set<TableAnnotation>();
    public DbSet<ColumnAnnotation> ColumnAnnotations => Set<ColumnAnnotation>();
    public DbSet<SchemaSnapshot> SchemaSnapshots => Set<SchemaSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // SavedConnection
        modelBuilder.Entity<SavedConnection>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Provider).HasConversion<string>();
        });

        // TableAnnotation — unique per connection+db+schema+table
        modelBuilder.Entity<TableAnnotation>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ConnectionName, x.DatabaseName, x.SchemaName, x.TableName })
             .IsUnique();
            e.HasMany(x => x.ColumnAnnotations)
             .WithOne(x => x.TableAnnotation)
             .HasForeignKey(x => x.TableAnnotationId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ColumnAnnotation
        modelBuilder.Entity<ColumnAnnotation>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TableAnnotationId, x.ColumnName }).IsUnique();
        });

        // SchemaSnapshot
        modelBuilder.Entity<SchemaSnapshot>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ConnectionId, x.DatabaseName });
            e.HasOne(x => x.Connection)
             .WithMany(x => x.Snapshots)
             .HasForeignKey(x => x.ConnectionId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
