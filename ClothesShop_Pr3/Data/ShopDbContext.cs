using ClothesShop_Pr3.Models;
using Microsoft.EntityFrameworkCore;

namespace ClothesShop_Pr3.Data;

public class ShopDbContext(DbContextOptions<ShopDbContext> options) : DbContext(options)
{
    public DbSet<Clothes> Clothes => Set<Clothes>();
    public DbSet<ClothesSize> ClothesSizes => Set<ClothesSize>();
    public DbSet<ClothingType> ClothingTypes => Set<ClothingType>();
    public DbSet<Size> Sizes => Set<Size>();
    public DbSet<ShopUser> Users => Set<ShopUser>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<ShopOrder> Orders => Set<ShopOrder>();
    public DbSet<OrderClothes> OrderClothes => Set<OrderClothes>();
    public DbSet<OrderStatus> Statuses => Set<OrderStatus>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Clothes>().ToTable("Clothes").HasKey(x => x.ID);
        modelBuilder.Entity<ClothesSize>().ToTable("ClothesSize").HasKey(x => x.ID);
        modelBuilder.Entity<ClothingType>().ToTable("ClothingTypes").HasKey(x => x.ID);
        modelBuilder.Entity<Size>().ToTable("Sizes").HasKey(x => x.ID);
        modelBuilder.Entity<ShopUser>().ToTable("Users").HasKey(x => x.ID);
        modelBuilder.Entity<Role>().ToTable("Roles").HasKey(x => x.ID);
        modelBuilder.Entity<ShopOrder>().ToTable("Orders").HasKey(x => x.ID);
        modelBuilder.Entity<OrderClothes>().ToTable("OrderClothes").HasKey(x => x.ID);
        modelBuilder.Entity<OrderStatus>().ToTable("Status").HasKey(x => x.ID);

        modelBuilder.Entity<Clothes>().Property(x => x.Price).HasColumnType("decimal(10,2)");
        modelBuilder.Entity<OrderClothes>().Property(x => x.UnitPrice).HasColumnType("decimal(10,2)");
        modelBuilder.Entity<ShopUser>().HasIndex(x => x.Login).IsUnique();
        modelBuilder.Entity<ClothesSize>().HasIndex(x => new { x.ClothesID, x.SizeID }).IsUnique();
        modelBuilder.Entity<OrderClothes>().HasIndex(x => new { x.OrderID, x.ClothesSizeID }).IsUnique();
    }
}
