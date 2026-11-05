using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Interview.Api.Data;
using Interview.Api.Models;
using Interview.Api.Services;
using Interview.Api.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Interview.Api.Tests;

public sealed class ApiFactory(bool runnerEnabled = false) : WebApplicationFactory<Program>
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"interview-api-tests-{Guid.NewGuid():N}.db");
    private readonly string? postgres = Environment.GetEnvironmentVariable("INTERVIEW_TEST_POSTGRES");
    private readonly string schema = "test_" + Guid.NewGuid().ToString("N");
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = "Host=unused;Database=unused",
            ["Runner:BaseUrl"] = runnerEnabled ? "http://runner.test/" : "",
            ["Runner:ApiKey"] = runnerEnabled ? "test-only-key" : ""
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options =>
            {
                if (postgres == null) options.UseSqlite($"Data Source={databasePath};Pooling=False;Default Timeout=10");
                else
                {
                    var connection = new NpgsqlConnectionStringBuilder(postgres) { SearchPath = schema };
                    if (connection.Database != "interview_tests") throw new InvalidOperationException("Для тестов допустима только отдельная БД interview_tests.");
                    options.UseNpgsql(connection.ConnectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema));
                }
            });
            var worker = services.FirstOrDefault(x => x.ImplementationType == typeof(RunWorker));
            if (worker != null) services.Remove(worker);
            if (runnerEnabled) services.AddHttpClient("runner").ConfigurePrimaryHttpMessageHandler(() => new FakeSandboxHandler());
        });
    }

    public async Task Initialize()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (postgres == null) await db.Database.EnsureCreatedAsync();
        else
        {
            await db.Database.OpenConnectionAsync();
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"CREATE SCHEMA \"{schema}\""; // schema состоит только из локального GUID, не из HTTP-ввода.
            await command.ExecuteNonQueryAsync();
            await db.Database.MigrateAsync();
        }
        await DatabaseSetup.SeedRoles(scope.ServiceProvider);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        foreach (var role in new[] { "admin", "teamlead", "interviewer" })
        {
            var account = new AppUser { UserName = role + "@example.test", Email = role + "@example.test", DisplayName = role, Role = role, RunQuota = 10 };
            var result = await users.CreateAsync(account, "TestPassword123!");
            if (!result.Succeeded) throw new Exception(string.Join(";", result.Errors.Select(x => x.Description)));
            await users.AddToRoleAsync(account, role);
        }
    }

    public TestSession Session() => new(CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false }));

    public override async ValueTask DisposeAsync()
    {
        if (postgres != null)
        {
            await using var cleanup = new NpgsqlConnection(postgres); await cleanup.OpenAsync();
            await using var command = cleanup.CreateCommand(); command.CommandText = $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE"; await command.ExecuteNonQueryAsync();
        }
        await base.DisposeAsync();
        File.Delete(databasePath);
        File.Delete(databasePath + "-shm");
        File.Delete(databasePath + "-wal");
    }
}

// Не выполняет код: проверяет контракт worker и фильтрацию результатов скрытых тестов.
public sealed class FakeSandboxHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization?.Parameter != "test-only-key") throw new InvalidOperationException("Нет ключа runner.");
        var run = await request.Content!.ReadFromJsonAsync<RunnerRequest>(cancellationToken);
        var results = run!.Tests.Select(x => new RunnerTestResult(x.Position, x.Position == 0 ? "5\n" : "PRIVATE_EXPECTED\n", 0, false, 10, 1024)).ToArray();
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new RunnerResult(results)) };
    }
}

// Явная cookie-банка позволяет тем же сеансом подключить HTTP API и SignalR.
public sealed class TestSession(HttpClient client) : IDisposable
{
    private readonly CookieContainer cookies = new();
    private string? csrf;
    public string CookieHeader => cookies.GetCookieHeader(client.BaseAddress!);

    public async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null, bool protect = true)
    {
        if (method != HttpMethod.Get && protect && csrf == null)
        {
            var tokenResponse = await Send(HttpMethod.Get, "/api/auth/csrf");
            tokenResponse.EnsureSuccessStatusCode();
            csrf = (await Json(tokenResponse)).GetProperty("token").GetString();
        }
        using var request = new HttpRequestMessage(method, path);
        if (CookieHeader.Length > 0) request.Headers.Add("Cookie", CookieHeader);
        if (method != HttpMethod.Get && protect) request.Headers.Add("X-CSRF-Token", csrf);
        if (body != null) request.Content = JsonContent.Create(body);
        var response = await client.SendAsync(request);
        if (response.Headers.TryGetValues("Set-Cookie", out var headers))
            foreach (var header in headers) cookies.SetCookies(client.BaseAddress!, header);
        if (response.IsSuccessStatusCode && (path.StartsWith("/api/auth/") && method != HttpMethod.Get || path == "/api/invitations/redeem")) csrf = null;
        return response;
    }

    public async Task<JsonElement> Get(string path)
    {
        var response = await Send(HttpMethod.Get, path); await Success(response); return await Json(response);
    }

    public async Task<JsonElement> Post(string path, object? body = null)
    {
        var response = await Send(HttpMethod.Post, path, body); await Success(response); return await Json(response);
    }

    public Task<JsonElement> Login(string role) => Post("/api/auth/login", new { email = role + "@example.test", password = "TestPassword123!" });
    public Task<JsonElement> Register(string email = "candidate@example.test") => Post("/api/auth/register/candidate", new { name = "Кандидат", email, password = "TestPassword123!", role = "admin" });
    public static async Task<JsonElement> Json(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    private static async Task Success(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode) throw new Exception($"HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }
    public void Dispose() => client.Dispose();
}
