using System.Security.Claims;
using System.Threading.RateLimiting;
using Interview.Api.Data;
using Interview.Api.Hubs;
using Interview.Api.Infrastructure;
using Interview.Api.Models;
using Interview.Api.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 256 * 1024);
var connection = builder.Configuration.GetConnectionString("Database");
if (string.IsNullOrWhiteSpace(connection) && !builder.Environment.IsEnvironment("Testing"))
    throw new InvalidOperationException("Задайте ConnectionStrings:Database через dotnet user-secrets или ConnectionStrings__Database. Затем выполните dotnet run -- --migrate.");
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connection ?? "Host=localhost;Database=interview;Username=postgres"));
builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireDigit = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Stores.SchemaVersion = IdentitySchemaVersions.Version2;
}).AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "interview.session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing") ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = false;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
    options.Events.OnValidatePrincipal = async context =>
    {
        var manager = context.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();
        var account = await manager.GetUserAsync(context.Principal!);
        var stamp = context.Principal!.FindFirstValue("AspNet.Identity.SecurityStamp");
        if (account is not { IsActive: true } || account.SecurityStamp != stamp) context.RejectPrincipal();
    };
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-Token";
    options.Cookie.Name = "interview.csrf";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing") ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.AddAuthorization();
builder.Services.AddScoped<CsrfFilter>();
builder.Services.AddControllers(options => options.Filters.Add<CsrfFilter>());
builder.Services.AddSignalR(options => { options.MaximumReceiveMessageSize = 256 * 1024; options.EnableDetailedErrors = false; });
builder.Services.AddScoped<AccessService>();
builder.Services.AddScoped<DtoService>();
builder.Services.AddScoped<RunService>();
builder.Services.AddSingleton<RoomConnections>();
builder.Services.AddHostedService<RunWorker>();
builder.Services.AddHttpClient("runner", client =>
{
    var baseUrl = builder.Configuration["Runner:BaseUrl"];
    if (!string.IsNullOrWhiteSpace(baseUrl))
    {
        var uri = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        if (uri.Scheme is not ("http" or "https")) throw new InvalidOperationException("Runner:BaseUrl должен быть HTTP(S)-адресом собственного sandbox-сервиса.");
        client.BaseAddress = uri;
    }
    client.Timeout = TimeSpan.FromSeconds(45);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 200, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = async (context, cancellation) => await context.HttpContext.Response.WriteAsJsonAsync(new { detail = "Слишком много запросов. Повторите через минуту." }, cancellation);
});

var app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    if (!context.Request.Path.Equals("/api/auth/csrf")) context.Response.Headers.CacheControl = "no-store";
    try { await next(); }
    catch (Exception error) when (!context.Response.HasStarted)
    {
        var (status, detail) = error switch
        {
            ApiError apiError => (apiError.Status, apiError.Message),
            DbUpdateConcurrencyException => (409, "Данные уже изменены другим запросом."),
            DbUpdateException update when update.InnerException is Npgsql.PostgresException postgres && postgres.SqlState == "23505" => (409, "Такая запись уже существует."),
            _ => (500, "Не удалось обработать запрос. Проверьте доступность базы и журнал сервера.")
        };
        if (status == 500) app.Logger.LogError(error, "Ошибка API");
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { detail });
    }
});
// SameSite не заменяет проверку Origin для WebSocket с cookie-аутентификацией.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/hubs"))
    {
        var origin = context.Request.Headers.Origin.ToString();
        var allowed = builder.Configuration.GetSection("Security:AllowedOrigins").Get<string[]>() ?? [];
        var sameOrigin = $"{context.Request.Scheme}://{context.Request.Host}";
        if ((origin.Length > 0 && origin != sameOrigin && !allowed.Contains(origin, StringComparer.OrdinalIgnoreCase))
            || (context.WebSockets.IsWebSocketRequest && origin.Length == 0))
        { context.Response.StatusCode = 403; return; }
    }
    await next();
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.MapHub<InterviewHub>("/hubs/interviews", options => options.CloseOnAuthenticationExpiration = true);
app.MapGet("/health/live", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapGet("/health/ready", async (AppDbContext db) => await db.Database.CanConnectAsync()
    ? Results.Ok(new { status = "ready" }) : Results.Json(new { status = "database-unavailable" }, statusCode: 503)).AllowAnonymous();

if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    if (args.Contains("--migrate")) await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    await DatabaseSetup.SeedRoles(scope.ServiceProvider);
    if (args.Contains("--bootstrap-admin")) await DatabaseSetup.BootstrapAdmin(scope.ServiceProvider, app.Configuration);
    if (args.Contains("--migrate") || args.Contains("--bootstrap-admin")) return;
}
await app.RunAsync();

public partial class Program { }
