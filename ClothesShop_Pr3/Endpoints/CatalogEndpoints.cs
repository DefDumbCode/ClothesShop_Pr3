
using ClothesShop_Pr3.Data;
using Microsoft.EntityFrameworkCore;

namespace ClothesShop_Pr3.Endpoints;

public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this WebApplication app)
    {
        app.MapGet("/api/clothes", async (ShopDbContext db) =>
        {
            var clothes = await db.Clothes.ToListAsync();

            return Results.Ok(clothes);
        });

        app.MapGet("/api/clothes/search", async (string name, ShopDbContext db) =>
        {
            var clothes = await db.Clothes
                .Where(x => x.ClothingName.Contains(name))
                .ToListAsync();

            return Results.Ok(clothes);
        });
    }
}
