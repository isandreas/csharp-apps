using Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Api.Data;

/// <summary>
/// Application database context backed by PostgreSQL.
/// </summary>
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Item> Items => Set<Item>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Item>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                  .ValueGeneratedNever(); // We set it in the model

            entity.Property(e => e.Name)
                  .IsRequired()
                  .HasMaxLength(256);

            entity.Property(e => e.Description)
                  .HasMaxLength(2000);

            entity.HasIndex(e => e.CreatedAt)
                  .HasDatabaseName("ix_items_created_at");

            entity.HasIndex(e => e.Name)
                  .HasDatabaseName("ix_items_name");
        });
    }
}
