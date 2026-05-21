using InventoryApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace InventoryApi.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var product = modelBuilder.Entity<Product>();
        product.HasKey(p => p.Id);
        product.Property(p => p.Name).IsRequired().HasMaxLength(200);
        product.Property(p => p.Sku).IsRequired().HasMaxLength(64);
        product.HasIndex(p => p.Sku).IsUnique();
        product.Property(p => p.Price).HasPrecision(12, 2);
        product.Property(p => p.Category).HasConversion<string>().HasMaxLength(20);
        product.HasIndex(p => p.Category);
    }
}
