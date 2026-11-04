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
using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using Microsoft.Extensions.DependencyInjection.Extensions;

//команды обслуживания флаги, а не пары «ключ/значение» конфигурации
var maintenanceCommands = new[] { "--migrate", "--bootstrap-admin", "--encrypt-existing-data", "--create-keyring-certificate", "--export-keyring", "--check-keyring" };
var builder = WebApplication.CreateBuilder(args.Where(argument => !maintenanceCommands.Contains(argument)).ToArray());
if (args.Contains("--create-keyring-certificate")) { KeyringCertificate.Create(builder.Configuration); return; }

//один и тот же долговечный key ring нужен cookie и зашифрованным полям БД

var keyDirectory = builder.Configuration["Security:KeysDirectory"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "visual-interview", "keys");

if (string.IsNullOrWhiteSpace(keyDirectory)) 
    throw new InvalidOperationException("Security:KeysDirectory должен содержать путь к закрытой папке ключей.");

var protection = builder.Services.AddDataProtection().SetApplicationName("visual-interview")
    .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));

var certificatePath = builder.Configuration["Security:KeyEncryptionCertificatePath"];

if (!string.IsNullOrWhiteSpace(certificatePath))
    protection.ProtectKeysWithCertificate(X509CertificateLoader.LoadPkcs12FromFile(certificatePath, builder.Configuration["Security:KeyEncryptionCertificatePassword"], X509KeyStorageFlags.EphemeralKeySet));
else if (OperatingSystem.IsWindows()) 
    protection.ProtectKeysWithDpapi();
else if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
    throw new InvalidOperationException("Для production задайте сертификат шифрования key ring: Security:KeyEncryptionCertificatePath.");

builder.Services.AddSingleton<FieldCipher>();

var trustedProxies = builder.Configuration.GetSection("Security:TrustedProxies").Get<string[]>() ?? [];

if (trustedProxies.Length > 0) builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    foreach (var proxy in trustedProxies)
    {
        if (!IPAddress.TryParse(proxy, out var address)) 
            throw new InvalidOperationException("TrustedProxies принимает только явные IP-адреса доверенного reverse proxy.");
        options.KnownIPNetworks.Add(new System.Net.IPNetwork(address, address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128));
    }
});

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 256 * 1024);

var connection = builder.Configuration.GetConnectionString("Database");

if (string.IsNullOrWhiteSpace(connection) && !builder.Environment.IsEnvironment("Testing"))
    throw new InvalidOperationException("Задайте ConnectionStrings:Database через dotnet user-secrets или ConnectionStrings__Database. Затем выполните dotnet run -- --migrate.");

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connection));


//нужен для управления структурами в базе данных, чтобы они нормально работали
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

//заменяем стандартный валидатор, чтобы приглашённый гость мог войти без email
//изначально вся валидация строгая. Этой строчкой мы заменяем его на более мягкий
builder.Services.Replace(ServiceDescriptor.Scoped<IUserValidator<AppUser>, GuestUserValidator>());
//настраиваем куки для проекта
builder.Services.ConfigureApplicationCookie(options =>
{
    //заменяем имя куки 
    options.Cookie.Name = "interview.session";
    //этой строчкой запрещаем обращаться к кукам через js код, для защиты от xxs атак или кражи куки
    options.Cookie.HttpOnly = true;
    //предотвращает CSRF-атаки, грубо говоря запрещает отправлять запросы на POST, UPDATE, DELETE и тд без предоставления CSRF ключа
    //CSRF-ключ - это ключ, который прикрепляется к сессии через куки-файл на компьютере и при запросах он всегда сверяется с браузером и если ключ валиден, то пропускает этот запрос
    options.Cookie.SameSite = SameSiteMode.Lax;
    //указывает, что куки надо передавать только по защищеному https протоколу
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing") ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    //время хранения куки
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    //значит, что в любом случае удалится кука, даже если пользователь на сайте 
    options.SlidingExpiration = false;
    //всегда выдает только чистый код, а не html страничку
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
    //это хук, который всегда срабатывает при обработке пользователя
    options.Events.OnValidatePrincipal = async context =>
    {
        var manager = context.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();
        var account = await manager.GetUserAsync(context.Principal!);
        var stamp = context.Principal!.FindFirstValue("AspNet.Identity.SecurityStamp");
        if (account is not { IsActive: true } || account.SecurityStamp != stamp)
            context.RejectPrincipal();
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
builder.Services.AddScoped<SessionRecorder>();
builder.Services.AddSingleton<RoomConnections>();
builder.Services.AddHostedService<RunWorker>();
builder.Services.AddHostedService<RoomExpiryWorker>();
builder.Services.AddHttpClient("runner", client =>
{
    var baseUrl = builder.Configuration["Runner:BaseUrl"];
    if (!string.IsNullOrWhiteSpace(baseUrl))
    {
        var uri = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        if (uri.Scheme is not ("http" or "https")) throw new InvalidOperationException("Runner:BaseUrl должен быть HTTP(S)-адресом собственного sandbox-сервиса.");
        if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing") && uri.Scheme != "https")
            throw new InvalidOperationException("В production соединение API с runner должно использовать HTTPS.");
        client.BaseAddress = uri;
    }
    client.Timeout = TimeSpan.FromSeconds(180);
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
// Экспорт и проверка ключей не запускают сервер и не обращаются к рабочей БД.
if (args.Contains("--export-keyring")) { KeyringExport.Run(app.Services, app.Configuration); return; }
if (args.Contains("--check-keyring")) { KeyringExport.Verify(app.Services, app.Configuration); return; }
if (trustedProxies.Length > 0) app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
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
    if (args.Contains("--encrypt-existing-data")) await EncryptionBackfill.Run(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    if (args.Contains("--migrate") || args.Contains("--bootstrap-admin") || args.Contains("--encrypt-existing-data")) return;
}
await app.RunAsync();
