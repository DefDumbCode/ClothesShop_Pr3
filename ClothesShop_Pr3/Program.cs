using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using ClothesShop_Pr3.Models;
using ClothesShop_Pr3.Models.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<pr3Context>();

var hasher = new PasswordHasher<User>();

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

app.MapPost("/api/auth/login", async (pr3Context context, LoginRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Login) ||
        string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest(new { message = "Введите логин и пароль" });
    }

    string login = request.Login.Trim();

    User? account = await context.Users
        .FirstOrDefaultAsync(u => u.Login == login);

    if (account == null)
    {
        return Results.Unauthorized();
    }

    var result = hasher.VerifyHashedPassword(
        account,
        account.HashPass,
        request.Password);

    if (result == PasswordVerificationResult.Failed)
    {
        return Results.Unauthorized();
    }

    Role? role = await context.Roles
        .FirstOrDefaultAsync(r => r.Id == account.RoleId);

    if (role == null)
    {
        return Results.Problem("Роль пользователя не найдена");
    }

    return Results.Ok(new
    {
        access_token = CreateToken(account, role.Name),
        token_type = "Bearer",
        userId = account.Id,
        role = role.Name
    });
}).AllowAnonymous();


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


// зарегать обычного пользователя

app.MapPost("/api/auth/reg", async (pr3Context context, RegistrationRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Login) ||
        string.IsNullOrWhiteSpace(request.Password) ||
        string.IsNullOrWhiteSpace(request.PasswordRep) ||
        string.IsNullOrWhiteSpace(request.Phone) ||
        string.IsNullOrWhiteSpace(request.Address))
    {
        return Results.BadRequest(new { message = "Поля не могут быть пустыми" });
    }

    if (request.Password != request.PasswordRep)
    {
        return Results.BadRequest(new { message = "Пароли не совпадают" });
    }

    if (request.Password.Length < 8)
    {
        return Results.BadRequest(new { message = "Пароль должен содержать минимум 8 символов" });
    }

    string login = request.Login.Trim();
    string phone = request.Phone.Trim();
    string address = request.Address.Trim();

    if (login.Length > 50 || phone.Length > 20 || address.Length > 250)
    {
        return Results.BadRequest(new
        {
            message = "Максимальная длина: логин — 50, телефон — 20, адрес — 250 символов"
        });
    }

    User? account = await context.Users
        .FirstOrDefaultAsync(u => u.Login == login);

    if (account != null)
    {
        return Results.BadRequest(new { message = "Аккаунт с таким логином уже существует" });
    }

    Role? role = await context.Roles
        .FirstOrDefaultAsync(r => r.Name == "Пользователь");

    if (role == null)
    {
        return Results.Problem("В БД отсутствует роль Пользователь");
    }

    User newAccount = new User()
    {
        Login = login,
        Phone = phone,
        Address = address,
        RoleId = role.Id
    };

    newAccount.HashPass = hasher.HashPassword(
        newAccount,
        request.Password);

    context.Users.Add(newAccount);

    try
    {
        await context.SaveChangesAsync();
    }
    catch (DbUpdateException ex)
        when (ex.InnerException is SqlException sql &&
              (sql.Number == 2601 || sql.Number == 2627))
    {
        return Results.BadRequest(new { message = "Аккаунт с таким логином уже существует" });
    }

    return Results.Json(new
    {
        newAccount.Id,
        newAccount.Login,
        newAccount.Phone,
        newAccount.Address,
        newAccount.RoleId
    }, statusCode: 201);

}).AllowAnonymous();


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

app.Run();
string CreateToken(User user, string role)
{
    var claims = new[]
    {
        new Claim("sub", user.Id.ToString()),
        new Claim("name", user.Login),
        new Claim("role", role)
    };

    var token = new JwtSecurityToken(
        issuer: "JwtDemo",
        audience: "JwtDemoApi",
        claims: claims,
        expires: DateTime.UtcNow.AddMinutes(15),
        signingCredentials: new SigningCredentials(
            key,
            SecurityAlgorithms.HmacSha256));

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