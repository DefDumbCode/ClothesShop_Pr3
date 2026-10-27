using ClothesShop_Pr3;
using ClothesShop_Pr3.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;

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
        return Results.BadRequest(new { message = "Пользователь с таким логином уже существует" });
    }

    if (request.Password != request.PasswordRep)
    {
        return Results.BadRequest(new { message = "Пароли не совпадают" });
    }

    if (string.IsNullOrWhiteSpace(request.Password) ||
    string.IsNullOrWhiteSpace(request.PasswordRep) ||
    string.IsNullOrWhiteSpace(request.Login))
    {
        return Results.BadRequest(new { message = "Обнаружены пустые данные" });
    }

    User newAccount = new User()
    {
        Login = request.Login,
        RoleId = context.Roles.FirstOrDefault(r => r.Name == "Пользователь").Id,
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
    return Results.Ok("Раздел администратора");
}).RequireAuthorization(policy => policy.RequireAuthenticatedUser().RequireRole("Admin"));

app.MapGet("/clothes", (pr3Context context) =>
{
    List<Clothe> stuff = context.Clothes.ToList();
    return stuff;
});

app.MapGet("/clothes", (pr3Context context, string search) =>
{
    List<Clothe> tuff = context.Clothes.ToList();
    tuff = tuff.Where(u => u.ClothingName.Contains(search)).ToList();
    return tuff;
});


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

internal record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
