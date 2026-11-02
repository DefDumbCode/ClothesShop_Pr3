using ClothesShop_Pr3;
using ClothesShop_Pr3.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

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
        RoleId = context.Roles.FirstOrDefault(r => r.Name == "Manager")?.Id ?? 1,
        Phone = request.Phone,
        Address = request.Address
    };
    newAccount.HashPass = hasher.HashPassword(newAccount, request.Password);

    context.Users.Add(newAccount);
    context.SaveChanges();
    return Results.Created($"api/users/{newAccount.Id}", newAccount);

}).AllowAnonymous();

app.MapPost("/api/auth/login", (pr3Context context, LoginRequest request) =>
{
    var account = context.Users.FirstOrDefault(
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
        access_token = CreateToken(account),
        token_type = "Bearer"
    });
}).AllowAnonymous();

app.MapGet("/api/me", (ClaimsPrincipal user) =>
{
    return Results.Ok(new
    {
        login = user.Identity?.Name,
        role = user.FindFirst("role")?.Value
    });
}).RequireAuthorization();

app.MapGet("/api/admin", () =>
{
    return Results.Ok("������ ��������������");
}).RequireAuthorization(policy => policy.RequireAuthenticatedUser().RequireRole("Admin"));


app.MapGet("/api/clothes", (pr3Context context) =>
{
    List<Clothe> tuff = context.Clothes.ToList();
    return Results.Json(tuff);
});

app.MapGet("/api/clothes/{search}", (pr3Context context, string? search) =>
{
    List<Clothe> tuff = context.Clothes.ToList();
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

    var orders = await context.Orders
        .Where(o => o.StatusId == statusId)
        .Select(o => new
        {
            o.Id,
            o.CreatedAt,
            o.StatusId,
            o.UserId
        })
        .ToListAsync();

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

    var orders = await context.Orders
        .Where(o => o.UserId == userId)
        .Select(o => new
        {
            o.Id,
            o.CreatedAt,
            o.StatusId,
            o.UserId
        })
        .ToListAsync();

    return Results.Json(orders);
}).RequireAuthorization();



// (менеджеру) изменить статус заказа

app.MapPatch("/api/orders/{id}", async (pr3Context context, int id, int statusId) =>
{
    Order? order = await context.Orders
        .FirstOrDefaultAsync(o => o.Id == id);

    if (order == null)
    {
        return Results.NotFound(new { message = "Заказ не найден" });
    }

    Status? status = await context.Statuses
        .FirstOrDefaultAsync(s => s.Id == statusId);

    if (status == null)
    {
        return Results.BadRequest(new { message = "Статус не существует" });
    }

    order.StatusId = statusId;

    await context.SaveChangesAsync();

    return Results.Json(new
    {
        order.Id,
        order.CreatedAt,
        order.StatusId,
        order.UserId
    });
}).RequireAuthorization(policy =>
    policy.RequireAuthenticatedUser().RequireRole("Менеджер"));

app.MapPut("/api/clothes", async (pr3Context context, ClothesSize newClothesSize) =>
{
    ClothesSize? clothesSize = context.ClothesSizes.FirstOrDefault(cs => cs.Id == newClothesSize.Id);
    
    if(clothesSize == null)
    {
        return Results.NotFound(new { message = "Товар не найден" });
    }

    clothesSize.Quantity = newClothesSize.Quantity;
    await context.SaveChangesAsync();
    return Results.Json(clothesSize);
}).RequireAuthorization(policy => policy.RequireAuthenticatedUser().RequireRole("Менеджер"));


app.MapPatch("/api/clothes/{id}", async (pr3Context context, int id, bool isAvailable) =>
{
    Clothe? clothe= context.Clothes.FirstOrDefault(c => c.Id == id);

    if (clothe == null)
    {
        return Results.NotFound(new { message = "Товар не найден" });
    }

    clothe.IsAvailable = isAvailable;
    await context.SaveChangesAsync();
    return Results.Json(clothe);
}).RequireAuthorization(policy => policy.RequireAuthenticatedUser().RequireRole("Менеджер"));

app.Run();


string CreateToken(User user)
{
    var claims = new[]
    {
        new Claim("sub", user.Id.ToString()),
        new Claim("name", user.Login),
        new Claim("role", user.Role.Name)
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
public record LoginRequest(
    string Login,
    string Password);

public record RegistrationRequest(
    string Login,
    string Password,
    string PasswordRep,
    string Phone,
    string Address);