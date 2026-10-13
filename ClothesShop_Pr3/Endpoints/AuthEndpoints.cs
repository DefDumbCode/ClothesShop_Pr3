using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ClothesShop_Pr3.Contracts;
using ClothesShop_Pr3.Data;
using ClothesShop_Pr3.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace ClothesShop_Pr3.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/register", async Task<IResult> (RegisterRequest request, ShopDbContext db, IPasswordHasher<ShopUser> hasher) =>
        {
            if (string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrWhiteSpace(request.Password) ||
                string.IsNullOrWhiteSpace(request.Phone) || string.IsNullOrWhiteSpace(request.Address) ||
                request.Password.Length < 8 || request.Login.Length > 50 || request.Phone.Length > 20 || request.Address.Length > 250)
                return Results.BadRequest(new { message = "Заполните поля. Пароль от 8 символов." });

            string login = request.Login.Trim();

            if (await db.Users.AnyAsync(u => u.Login == login))
                return Results.Conflict(new { message = "Такой логин уже занят" });

            var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == "Пользователь");

            if (role == null)
                return Results.Problem("В базе отсутствует роль Пользователь");

            var user = new ShopUser
            {
                Login = login,
                Phone = request.Phone.Trim(),
                Address = request.Address.Trim(),
                RoleID = role.ID
            };

            user.HashPass = hasher.HashPassword(user, request.Password);
            db.Users.Add(user);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                return Results.Conflict(new { message = "Не удалось сохранить аккаунт. Проверьте уникальность логина." });
            }

            return Results.Json(new { id = user.ID, login = user.Login, role = role.Name }, statusCode: StatusCodes.Status201Created);
        }).AllowAnonymous();

        app.MapPost("/api/auth/login", async Task<IResult> (LoginRequest request, ShopDbContext db, IPasswordHasher<ShopUser> hasher, IConfiguration config) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Login == request.Login);

            if (user == null || string.IsNullOrEmpty(request.Password))
                return Results.Unauthorized();

            var verification = hasher.VerifyHashedPassword(user, user.HashPass, request.Password);

            if (verification == PasswordVerificationResult.Failed)
                return Results.Unauthorized();

            var role = await db.Roles.FirstOrDefaultAsync(r => r.ID == user.RoleID);

            if (role == null)
                return Results.Problem("Не найдена роль пользователя");

            var claims = new[]
            {
                new Claim("sub", user.ID.ToString()),
                new Claim("name", user.Login),
                new Claim("role", role.Name)
            };

            string jwtKey = config["Jwt:Key"]!;
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var token = new JwtSecurityToken(
                issuer: config["Jwt:Issuer"],
                audience: config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddHours(2),
                signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

            return Results.Ok(new
            {
                access_token = new JwtSecurityTokenHandler().WriteToken(token),
                token_type = "Bearer",
                role = role.Name
            });
        }).AllowAnonymous();
    }
}
