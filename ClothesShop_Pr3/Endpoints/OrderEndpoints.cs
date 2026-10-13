using System.Security.Claims;
using ClothesShop_Pr3.Contracts;
using ClothesShop_Pr3.Data;
using ClothesShop_Pr3.Models;
using Microsoft.EntityFrameworkCore;

namespace ClothesShop_Pr3.Endpoints;

public static class OrderEndpoints
{
    public static void MapOrderEndpoints(this WebApplication app)
    {
        app.MapPost("/api/orders", async Task<IResult> (CreateOrderRequest request, ClaimsPrincipal principal, ShopDbContext db) =>
        {
            if (!int.TryParse(principal.FindFirst("sub")?.Value, out int userId))
                return Results.Unauthorized();

            if (request.Items == null || request.Items.Count == 0 || request.Items.Any(x => x.Quantity <= 0 || x.ClothesSizeId <= 0))
                return Results.BadRequest(new { message = "В заказе должны быть товары с положительным количеством" });

            var items = request.Items.GroupBy(x => x.ClothesSizeId)
                .Select(g => new { ClothesSizeId = g.Key, Quantity = g.Sum(x => (long)x.Quantity) })
                .ToList();

            if (items.Any(x => x.Quantity > int.MaxValue))
                return Results.BadRequest(new { message = "Слишком большое количество товара" });

            await using var transaction = await db.Database.BeginTransactionAsync();

            var order = new ShopOrder
            {
                UserID = userId,
                StatusID = 1,
                CreatedAt = DateTime.UtcNow
            };
            db.Orders.Add(order);
            await db.SaveChangesAsync();

            decimal total = 0;

            foreach (var item in items)
            {
                int count = checked((int)item.Quantity);
                var productData = await (from cs in db.ClothesSizes.AsNoTracking()
                                         join c in db.Clothes.AsNoTracking() on cs.ClothesID equals c.ID
                                         where cs.ID == item.ClothesSizeId
                                         select new { c.ID, c.Price, c.IsAvailable })
                    .FirstOrDefaultAsync();

                if (productData == null || !productData.IsAvailable)
                    return Results.BadRequest(new { message = $"Товар с размером {item.ClothesSizeId} недоступен" });

                int changed = await db.ClothesSizes
                    .Where(s => s.ID == item.ClothesSizeId && s.Quantity >= count)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.Quantity, s => s.Quantity - count));

                if (changed != 1)
                    return Results.BadRequest(new { message = $"Недостаточно товара с размером {item.ClothesSizeId}" });

                await db.Clothes.Where(c => c.ID == productData.ID)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.SaleQuantity, c => c.SaleQuantity + count));

                db.OrderClothes.Add(new OrderClothes
                {
                    OrderID = order.ID,
                    ClothesSizeID = item.ClothesSizeId,
                    Quantity = count,
                    UnitPrice = productData.Price
                });

                total += productData.Price * count;
            }

            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            return Results.Created($"/api/orders/{order.ID}", new { orderId = order.ID, userId, statusId = 1, total });
        }).RequireAuthorization();

        app.MapGet("/api/orders/{id:int}", async Task<IResult> (int id, ClaimsPrincipal principal, ShopDbContext db) =>
        {
            if (!int.TryParse(principal.FindFirst("sub")?.Value, out int currentUserId))
                return Results.Unauthorized();

            var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.ID == id);

            if (order == null)
                return Results.NotFound(new { message = "Заказ не найден" });

            if (order.UserID != currentUserId && !principal.IsInRole("Менеджер"))
                return Results.Forbid();

            var items = await (from oc in db.OrderClothes.AsNoTracking()
                               join cs in db.ClothesSizes.AsNoTracking() on oc.ClothesSizeID equals cs.ID
                               join c in db.Clothes.AsNoTracking() on cs.ClothesID equals c.ID
                               join s in db.Sizes.AsNoTracking() on cs.SizeID equals s.ID
                               where oc.OrderID == id
                               select new
                               {
                                   clothesSizeId = oc.ClothesSizeID,
                                   name = c.ClothingName,
                                   size = s.SizeMark,
                                   quantity = oc.Quantity,
                                   unitPrice = oc.UnitPrice,
                                   subtotal = oc.UnitPrice * oc.Quantity
                               }).ToListAsync();

            return Results.Ok(new
            {
                id = order.ID,
                userId = order.UserID,
                statusId = order.StatusID,
                createdAt = order.CreatedAt,
                items,
                total = items.Sum(x => x.subtotal)
            });
        }).RequireAuthorization();

        app.MapGet("/api/orders/status/{statusId:int}", async Task<IResult> (int statusId, ClaimsPrincipal principal, ShopDbContext db) =>
        {
            if (!int.TryParse(principal.FindFirst("sub")?.Value, out int userId))
                return Results.Unauthorized();

            var query = db.Orders.AsNoTracking().Where(o => o.StatusID == statusId);

            if (!principal.IsInRole("Менеджер"))
                query = query.Where(o => o.UserID == userId);

            var orders = await query.OrderByDescending(o => o.CreatedAt)
                .Select(o => new { id = o.ID, createdAt = o.CreatedAt, statusId = o.StatusID, userId = o.UserID })
                .ToListAsync();

            return Results.Ok(orders);
        }).RequireAuthorization();

        app.MapGet("/api/orders/user/{userId:int}", async Task<IResult> (int userId, ClaimsPrincipal principal, ShopDbContext db) =>
        {
            if (!int.TryParse(principal.FindFirst("sub")?.Value, out int currentUserId))
                return Results.Unauthorized();

            if (currentUserId != userId && !principal.IsInRole("Менеджер"))
                return Results.Forbid();

            var orders = await db.Orders.AsNoTracking().Where(o => o.UserID == userId)
                .OrderByDescending(o => o.CreatedAt)
                .Select(o => new { id = o.ID, createdAt = o.CreatedAt, statusId = o.StatusID, userId = o.UserID })
                .ToListAsync();

            return Results.Ok(orders);
        }).RequireAuthorization();

        app.MapPatch("/api/orders/{id:int}/status", async Task<IResult> (int id, ChangeStatusRequest request, ShopDbContext db) =>
        {
            if (!await db.Statuses.AnyAsync(s => s.ID == request.StatusId))
                return Results.BadRequest(new { message = "Такого статуса нет" });

            var order = await db.Orders.FirstOrDefaultAsync(o => o.ID == id);

            if (order == null)
                return Results.NotFound(new { message = "Заказ не найден" });

            order.StatusID = request.StatusId;
            await db.SaveChangesAsync();
            return Results.Ok(new { orderId = order.ID, statusId = order.StatusID });
        }).RequireAuthorization(policy => policy.RequireRole("Менеджер"));
    }
}
