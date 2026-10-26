using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Замени localhost на имя своего сервера из SSMS.
// Например: localhost\SQLEXPRESS.
// Благодаря @ обратную косую черту удваивать не нужно.
string connectionString =
    @"Server=DESKTOP-5Q6T8JB;Database=07.10PR3;Trusted_Connection=True;TrustServerCertificate=True;";

// Регистрируем контекст: ASP.NET Core будет передавать его в endpoints.
builder.Services.AddDbContext<Pr3DbContext>(options =>
    options.UseSqlServer(connectionString));

// Сервис для создания и проверки хешей паролей.
builder.Services.AddScoped<PasswordHasher<DbUser>>();

// Учебный вариант: ключ создаётся при запуске приложения.
// После перезапуска API нужно заново войти и получить токен.
var key = new SymmetricSecurityKey(
    RandomNumberGenerator.GetBytes(32));

const string issuer = "Pr3";
const string audience = "Pr3Client";

// Настраиваем проверку JWT, которые будут приходить от клиента.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Сохраняем названия claims: sub, name, role.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,

            ValidateAudience = true,
            ValidAudience = audience,

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,

            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,

            NameClaimType = "name",
            RoleClaimType = "role"
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Сначала определяем пользователя, затем проверяем права.
app.UseAuthentication();
app.UseAuthorization();

// ВХОД: нужен для получения токена перед защищёнными запросами.
app.MapPost("/api/auth/login", async (
    LoginRequest request,
    Pr3DbContext db,
    PasswordHasher<DbUser> hasher) =>
{
    if (string.IsNullOrWhiteSpace(request.Login) ||
        string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest("Введите логин и пароль");
    }

    string login = request.Login.Trim();

    var user = await db.Users
        .FirstOrDefaultAsync(u => u.Login == login);

    if (user == null)
    {
        return Results.Unauthorized();
    }

    // Проверяем введённый пароль по сохранённому хешу.
    var passwordResult = hasher.VerifyHashedPassword(
        user,
        user.HashPass,
        request.Password);

    if (passwordResult == PasswordVerificationResult.Failed)
    {
        return Results.Unauthorized();
    }

    // Название роли получаем из БД, а не от клиента.
    string? role = await db.Roles
        .Where(r => r.Id == user.RoleId)
        .Select(r => r.Name)
        .FirstOrDefaultAsync();

    if (role == null)
    {
        return Results.Problem("Не найдена роль пользователя");
    }

    // Claims — сведения о пользователе, которые попадут в JWT.
    var claims = new[]
    {
        new Claim("sub", user.Id.ToString()),
        new Claim("name", user.Login),
        new Claim("role", role)
    };

    var token = new JwtSecurityToken(
        issuer: issuer,
        audience: audience,
        claims: claims,
        expires: DateTime.UtcNow.AddHours(1),
        signingCredentials: new SigningCredentials(
            key,
            SecurityAlgorithms.HmacSha256));

    string tokenText = new JwtSecurityTokenHandler()
        .WriteToken(token);

    return Results.Ok(new
    {
        access_token = tokenText,
        token_type = "Bearer",
        userId = user.Id,
        role
    });
})
.AllowAnonymous();

// 1. ПОЛУЧЕНИЕ ЗАКАЗОВ ПО СТАТУСУ.
app.MapGet("/api/orders/by-status/{statusId}", async (
    int statusId,
    Pr3DbContext db) =>
{
    // statusId берётся из адреса запроса.
    bool statusExists = await db.Statuses
        .AnyAsync(s => s.Id == statusId);

    if (!statusExists)
    {
        return Results.NotFound("Статус не найден");
    }

    var orders = await db.Orders
        .Where(o => o.StatusId == statusId)
        .OrderByDescending(o => o.CreatedAt)
        .Select(o => new
        {
            o.Id,
            o.CreatedAt,
            o.StatusId,
            o.UserId
        })
        .ToListAsync();

    return Results.Ok(orders);
})
.RequireAuthorization();

app.MapGet("/api/orders/by-user/{userId}", async (
    int userId,
    Pr3DbContext db) =>
{
    bool userExists = await db.Users
        .AnyAsync(u => u.Id == userId);

    if (!userExists)
    {
        return Results.NotFound("Покупатель не найден");
    }

    var orders = await db.Orders
        .Where(o => o.UserId == userId)
        .OrderByDescending(o => o.CreatedAt)
        .Select(o => new
        {
            o.Id,
            o.CreatedAt,
            o.StatusId,
            o.UserId
        })
        .ToListAsync();

    return Results.Ok(orders);
})
.RequireAuthorization();

app.MapPost("/api/auth/register", async (
    RegisterRequest request,
    Pr3DbContext db,
    PasswordHasher<DbUser> hasher) =>
{
    if (string.IsNullOrWhiteSpace(request.Login) ||
        string.IsNullOrWhiteSpace(request.Password) ||
        string.IsNullOrWhiteSpace(request.Phone) ||
        string.IsNullOrWhiteSpace(request.Address))
    {
        return Results.BadRequest("Заполните все поля");
    }

    string login = request.Login.Trim();
    string phone = request.Phone.Trim();
    string address = request.Address.Trim();

    if (login.Length > 50 ||
        phone.Length > 20 ||
        address.Length > 250)
    {
        return Results.BadRequest(
            "Максимальная длина: логин — 50, телефон — 20, адрес — 250 символов");
    }

    if (request.Password.Length < 8)
    {
        return Results.BadRequest(
            "Пароль должен содержать минимум 8 символов");
    }

    bool loginExists = await db.Users
        .AnyAsync(u => u.Login == login);

    if (loginExists)
    {
        return Results.Conflict("Логин уже занят");
    }

    // Находим обычную роль в справочнике.
    // Клиент не может самостоятельно выбрать роль менеджера.
    var role = await db.Roles
        .FirstOrDefaultAsync(r => r.Name == "Пользователь");

    if (role == null)
    {
        return Results.Problem(
            "В таблице Roles отсутствует роль Пользователь");
    }

    var user = new DbUser
    {
        Login = login,
        Phone = phone,
        Address = address,
        RoleId = role.Id
    };

    // Исходный пароль в БД не сохраняем.
    user.HashPass = hasher.HashPassword(user, request.Password);

    db.Users.Add(user);

    try
    {
        // Выполняет INSERT. SQL Server автоматически назначает ID.
        await db.SaveChangesAsync();
    }
    catch (DbUpdateException ex)
        when (ex.InnerException is SqlException sql &&
              (sql.Number == 2601 || sql.Number == 2627))
    {
        // Защита от одновременной регистрации одинакового логина.
        return Results.Conflict("Логин уже занят");
    }

    // Возвращаем только безопасные поля, без HashPass.
    return Results.Json(new
    {
        user.Id,
        user.Login,
        user.Phone,
        user.Address,
        user.RoleId
    }, statusCode: StatusCodes.Status201Created);
})
.AllowAnonymous();

// 4. ИЗМЕНЕНИЕ СТАТУСА ЗАКАЗА МЕНЕДЖЕРОМ.
app.MapPatch("/api/orders/{orderId}/status", async (
    int orderId,
    ChangeStatusRequest request,
    Pr3DbContext db) =>
{
    // orderId приходит из адреса, request — из JSON в Body.
    var order = await db.Orders
        .FirstOrDefaultAsync(o => o.Id == orderId);

    if (order == null)
    {
        return Results.NotFound("Заказ не найден");
    }

    bool statusExists = await db.Statuses
        .AnyAsync(s => s.Id == request.StatusId);

    if (!statusExists)
    {
        return Results.BadRequest("Такого статуса не существует");
    }

    // Сначала меняем объект в памяти.
    order.StatusId = request.StatusId;

    // Затем сохраняем изменение в SQL Server.
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        order.Id,
        order.CreatedAt,
        order.StatusId,
        order.UserId
    });
})
.RequireAuthorization(policy =>
    policy.RequireAuthenticatedUser().RequireRole("Менеджер"));

app.Run();

// КЛАССЫ ДАННЫХ ЗАПРОСОВ.
// ASP.NET Core заполняет их из JSON, отправленного клиентом.

public class LoginRequest
{
    public string Login { get; set; } = "";
    public string Password { get; set; } = "";
}

public class RegisterRequest
{
    public string Login { get; set; } = "";
    public string Password { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
}

public class ChangeStatusRequest
{
    public int StatusId { get; set; }
}

// КЛАССЫ, СООТВЕТСТВУЮЩИЕ ТАБЛИЦАМ БД.
// Здесь только таблицы, необходимые для этих функций.

public class DbUser
{
    public int Id { get; set; }
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public string Login { get; set; } = "";
    public string HashPass { get; set; } = "";
    public int RoleId { get; set; }
}

public class DbOrder
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public int StatusId { get; set; }
    public int UserId { get; set; }
}

public class DbRole
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class DbStatus
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

// КОНТЕКСТ БАЗЫ ДАННЫХ.

public class Pr3DbContext : DbContext
{
    public Pr3DbContext(DbContextOptions<Pr3DbContext> options)
        : base(options)
    {
    }

    public DbSet<DbUser> Users => Set<DbUser>();
    public DbSet<DbOrder> Orders => Set<DbOrder>();
    public DbSet<DbRole> Roles => Set<DbRole>();
    public DbSet<DbStatus> Statuses => Set<DbStatus>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Указываем точные названия таблиц и колонок из pr3.sql.
        modelBuilder.Entity<DbUser>(entity =>
        {
            entity.ToTable("Users", "dbo");
            entity.HasKey(u => u.Id);

            entity.Property(u => u.Id)
                .HasColumnName("ID")
                .UseIdentityColumn();

            entity.Property(u => u.RoleId).HasColumnName("RoleID");
            entity.Property(u => u.Login).HasMaxLength(50).IsRequired();
            entity.Property(u => u.Phone).HasMaxLength(20).IsRequired();
            entity.Property(u => u.Address).HasMaxLength(250).IsRequired();
            entity.Property(u => u.HashPass).IsRequired();

            entity.HasIndex(u => u.Login).IsUnique();
        });

        modelBuilder.Entity<DbOrder>(entity =>
        {
            entity.ToTable("Orders", "dbo");
            entity.HasKey(o => o.Id);

            entity.Property(o => o.Id)
                .HasColumnName("ID")
                .UseIdentityColumn();

            entity.Property(o => o.StatusId).HasColumnName("StatusID");
            entity.Property(o => o.UserId).HasColumnName("UserID");
            entity.Property(o => o.CreatedAt).HasColumnType("datetime");
        });

        modelBuilder.Entity<DbRole>(entity =>
        {
            entity.ToTable("Roles", "dbo");
            entity.HasKey(r => r.Id);

            entity.Property(r => r.Id)
                .HasColumnName("ID")
                .UseIdentityColumn();

            entity.Property(r => r.Name).HasMaxLength(50).IsRequired();
        });

        modelBuilder.Entity<DbStatus>(entity =>
        {
            entity.ToTable("Status", "dbo");
            entity.HasKey(s => s.Id);

            entity.Property(s => s.Id)
                .HasColumnName("ID")
                .UseIdentityColumn();

            entity.Property(s => s.Name).HasMaxLength(50).IsRequired();
        });
    }
}