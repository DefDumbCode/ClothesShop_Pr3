using ClothesShop_Pr3;
using ClothesShop_Pr3.DTO;
using ClothesShop_Pr3.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});

builder.Services.AddDbContext<pr3Context>();

var hasher = new PasswordHasher<User>();

// Add services to the container.

var key = new SymmetricSecurityKey(
    RandomNumberGenerator.GetBytes(32));

var validation = new TokenValidationParameters
{
    ValidateIssuer = true,
    ValidIssuer = "JwtDemo",

    ValidateAudience = true,
    ValidAudience = "JwtDemoApi",

    ValidateIssuerSigningKey = true,
    IssuerSigningKey = key,

    ValidateLifetime = true,
    ClockSkew = TimeSpan.Zero,

    NameClaimType = "name",
    RoleClaimType = "role"
};

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = validation;
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/api/auth/reg", async (pr3Context context, RegistrationRequest request) =>
{
    if (context.Users.FirstOrDefault(u => u.Login == request.Login) != null)
    {
        return Results.BadRequest(new { message = "Аккаунт с таким логином уже существует" });
    }

    if (request.Password != request.PasswordRep)
    {
        return Results.BadRequest(new { message = "Пароли не совпадают" });
    }

    if (string.IsNullOrWhiteSpace(request.Password) ||
    string.IsNullOrWhiteSpace(request.PasswordRep) ||
    string.IsNullOrWhiteSpace(request.Login) ||
    string.IsNullOrWhiteSpace(request.Phone) ||
    string.IsNullOrWhiteSpace(request.Address))
    {
        return Results.BadRequest(new { message = "Поля не могут быть пустыми" });
    }

    User newAccount = new User()
    {
        Login = request.Login,
        RoleId = context.Roles.FirstOrDefault(r => r.Name == "Пользователь")?.Id ?? 1,
        Phone = request.Phone,
        Address = request.Address
    };
    newAccount.HashPass = hasher.HashPassword(newAccount, request.Password);

    context.Users.Add(newAccount);
    context.SaveChanges();
    return Results.Created($"api/users/{newAccount.Id}", newAccount);

}).AllowAnonymous();

// авторизация
app.MapPost("/api/auth/login", (pr3Context context, LoginRequest request) =>
{
    var account = context.Users.Include(u => u.Role).FirstOrDefault(
        a => a.Login == request.Login);

    if (account == null ||
        string.IsNullOrWhiteSpace(request.Password))
        return Results.Challenge();

    var result = hasher.VerifyHashedPassword(
        account, account.HashPass, request.Password);

    if (result == PasswordVerificationResult.Failed)
        return Results.Challenge();

    return Results.Ok(new
    {
        access_token = CreateToken(account, context),
        token_type = "Bearer"
    });
}).AllowAnonymous();

app.MapPost("/api/order", async (pr3Context context, List<OrderRequest> request, ClaimsPrincipal user) =>
{
    if (!int.TryParse(user.FindFirst("sub")?.Value, out int u))
    {
        return Results.BadRequest("Аккаунт не найден");
    }

        Order newOrder = new Order
        {
            CreatedAt = DateTime.Now,
            StatusId = 1,
            UserId = u
        };

    context.Orders.Add(newOrder);
    await context.SaveChangesAsync();

    foreach(var req in request)
    {
        OrderClothe beb = new OrderClothe
        {
            OrderId = newOrder.Id,
            ClothesSizeId = req.ClotheID,
            Quantity = req.Quantity
        };

        context.OrderClothes.Add(beb);
        
    }
    await context.SaveChangesAsync();

    OrderDto? newOrderDto = context.Orders.Where(no => no.Id == newOrder.Id).Select(no => new OrderDto(
        no.User.Login,
        no.Status.Name,
        no.OrderClothes.Select(oc => new ClotheDto(
            oc.ClothesSize.Clothes.ClothingName,
            oc.ClothesSize.Clothes.Description,
            oc.ClothesSize.Clothes.SaleQuantity,
            oc.ClothesSize.Clothes.Type.TypeName,
            oc.ClothesSize.Clothes.Price,
            new ClothesSizeDto(
                oc.ClothesSize.Size.SizeMark,
                oc.ClothesSize.Size.SizeNumber,
                oc.ClothesSize.Quantity))).ToList(),
        no.CreatedAt)).ToList().FirstOrDefault();

    return Results.Created($"api/order/{newOrder.Id}", newOrderDto);
}).RequireAuthorization();

app.MapGet("/api/me", (ClaimsPrincipal user) =>
{
    return Results.Ok(new
    {
        login = user.Identity?.Name,
        role = user.FindFirst("role")?.Value
    });
}).RequireAuthorization();

// страница администратора (менеджера)
app.MapGet("/api/admin", () =>
{
    return Results.Ok("Привет, менеджер! Красивые ножки");
}).RequireAuthorization(policy => policy.RequireAuthenticatedUser().RequireRole("Менеджер"));

// вывод всех вещей
app.MapGet("/api/clothes", (pr3Context context) =>
{
    List<ClothesDto> tuff = context.Clothes.Select(c => new ClothesDto(
        c.ClothingName,
        c.Description,
        c.SaleQuantity,
        c.Type.TypeName,
        c.Price,
        c.ClothesSizes.Select(cs => new ClothesSizeDto(
            cs.Size.SizeMark,
            cs.Size.SizeNumber,
            cs.Quantity
            )).ToList(),
        c.IsAvailable
    )).ToList();
    return Results.Json(tuff);
});

// поиск одежды по названию
app.MapGet("/api/clothes/{search}", (pr3Context context, string? search) =>
{
    List<ClothesDto> tuff = context.Clothes.Select(c => new ClothesDto(
        c.ClothingName,
        c.Description,
        c.SaleQuantity,
        c.Type.TypeName,
        c.Price,
        c.ClothesSizes.Select(cs => new ClothesSizeDto(
            cs.Size.SizeMark,
            cs.Size.SizeNumber,
            cs.Quantity
            )).ToList(),
        c.IsAvailable
    )).ToList();

    if (search != null)
    {
        tuff = tuff.Where(t => t.ClothingName.ToLower().Contains(search.ToLower())).ToList();
    }
    return Results.Json(tuff);
});

// получить заказы с определенным статусом
app.MapGet("/api/orders/by-status/{statusId}", async (pr3Context context, int statusId) =>
{
    Status? status = await context.Statuses
        .FirstOrDefaultAsync(s => s.Id == statusId);

    if (status == null)
    {
        return Results.NotFound(new { message = "Статус не найден" });
    }

    List<OrderDto> orders = context.Orders.Where(o => o.Status.Id == statusId).Select(o => new OrderDto(
            o.User.Login,
            o.Status.Name,
            o.OrderClothes.Select(oc => new ClotheDto(
                    oc.ClothesSize.Clothes.ClothingName,
                    oc.ClothesSize.Clothes.Description,
                    oc.ClothesSize.Clothes.SaleQuantity,
                    oc.ClothesSize.Clothes.Type.TypeName,
                    oc.ClothesSize.Clothes.Price,
                    new ClothesSizeDto(
                            oc.ClothesSize.Size.SizeMark,
                            oc.ClothesSize.Size.SizeNumber,
                            oc.ClothesSize.Quantity
                        )
                )).ToList(),
            o.CreatedAt
        )).ToList();

    return Results.Json(orders);
}).RequireAuthorization();


// получить заказы определенного пользователя
app.MapGet("/api/orders/by-user/{userId}", async (pr3Context context, int userId) =>
{
    User? user = await context.Users
        .FirstOrDefaultAsync(u => u.Id == userId);

    if (user == null)
    {
        return Results.NotFound(new { message = "Покупатель не найден" });
    }

    List<OrderDto> orders = context.Orders.Where(o => o.User.Id == userId).Select(o => new OrderDto(
            o.User.Login,
            o.Status.Name,
            o.OrderClothes.Select(oc => new ClotheDto(
                    oc.ClothesSize.Clothes.ClothingName,
                    oc.ClothesSize.Clothes.Description,
                    oc.ClothesSize.Clothes.SaleQuantity,
                    oc.ClothesSize.Clothes.Type.TypeName,
                    oc.ClothesSize.Clothes.Price,
                    new ClothesSizeDto(
                            oc.ClothesSize.Size.SizeMark,
                            oc.ClothesSize.Size.SizeNumber,
                            oc.ClothesSize.Quantity
                        )
                )).ToList(),
            o.CreatedAt
        )).ToList();

    return Results.Json(orders);
}).RequireAuthorization();



// (менеджеру) изменить статус заказа
app.MapPatch("/api/orders/", async (pr3Context context, ChangeOrderStatus request) =>
{
    Order? order = await context.Orders.FirstOrDefaultAsync(o => o.Id == request.OrderId);

    if (order == null)
    {
        return Results.NotFound(new { message = "Заказ не найден" });
    }

    Status? status = context.Statuses
        .FirstOrDefault(s => s.Id == request.StatusId);

    if (status == null)
    {
        return Results.BadRequest(new { message = "Статус не существует" });
    }

    order.StatusId = request.StatusId;

    await context.SaveChangesAsync();

    return Results.Json(context.Orders.Where(o => o.Id == order.Id)
        .Select(o => new OrderDto(
            o.User.Login,
            o.Status.Name,
            o.OrderClothes.Select(oc => new ClotheDto(
                    oc.ClothesSize.Clothes.ClothingName,
                    oc.ClothesSize.Clothes.Description,
                    oc.ClothesSize.Clothes.SaleQuantity,
                    oc.ClothesSize.Clothes.Type.TypeName,
                    oc.ClothesSize.Clothes.Price,
                    new ClothesSizeDto(
                            oc.ClothesSize.Size.SizeMark,
                            oc.ClothesSize.Size.SizeNumber,
                            oc.ClothesSize.Quantity
                        )
                )).ToList(),
            o.CreatedAt
        )).FirstOrDefault());
}).RequireAuthorization(policy =>
    policy.RequireAuthenticatedUser().RequireRole("Менеджер"));

// (менеджеру) изменение количества вещей опрделенного размера
app.MapPatch("/api/clothes/sizes/{id}", async (pr3Context context, int id, ChangeClotheSizeQuantity request) =>
{
    ClothesSize? clothesSize = await context.ClothesSizes
        .FirstOrDefaultAsync(cs => cs.Id == id);
    
    if(clothesSize == null)
    {
        return Results.NotFound(new { message = "Товар не найден" });
    }

    clothesSize.Quantity = request.Quantity;
    await context.SaveChangesAsync();
    return Results.Json(context.ClothesSizes
        .Where(cs => cs.Id == clothesSize.Id)
        .Select(cs => new ClothesSizeDto(
            cs.Size.SizeMark,
            cs.Size.SizeNumber,
            cs.Quantity
        )).FirstOrDefault());

}).RequireAuthorization(policy => policy.RequireAuthenticatedUser().RequireRole("Менеджер"));

// (менеджеру) изменение достпности для продажи
app.MapPatch("/api/clothes/{id}/availability", async (pr3Context context, int id, ChangeClotheAvailabilityRequest request) =>
{
    Clothe? clothe= context.Clothes.FirstOrDefault(c => c.Id == id);

    if (clothe == null)
    {
        return Results.NotFound(new { message = "Товар не найден" });
    }

    clothe.IsAvailable = request.IsAvailable;
    await context.SaveChangesAsync();

    return Results.Json(context.Clothes
        .Where(c => c.Id == clothe.Id)
        .Select(c => new ClothesDto(
            c.ClothingName,
            c.Description,
            c.SaleQuantity,
            c.Type.TypeName,
            c.Price,
            c.ClothesSizes.Select(cs => new ClothesSizeDto(
                cs.Size.SizeMark,
                cs.Size.SizeNumber,
                cs.Quantity
                )).ToList(),
            c.IsAvailable
    )).FirstOrDefault());
}).RequireAuthorization(policy => policy.RequireAuthenticatedUser().RequireRole("Менеджер"));

app.Run();


string CreateToken(User user, pr3Context context)
{
    var claims = new[]
    {
        new Claim("sub", user.Id.ToString()),
        new Claim("name", user.Login),
        new Claim("role", user.Role.Name.ToString())
    };

    var token = new JwtSecurityToken(
        issuer: "JwtDemo",
        audience: "JwtDemoApi",
        claims: claims,
        expires: DateTime.UtcNow.AddMinutes(15),
        signingCredentials: new SigningCredentials(
            key, SecurityAlgorithms.HmacSha256));

    return new JwtSecurityTokenHandler().WriteToken(token);
}