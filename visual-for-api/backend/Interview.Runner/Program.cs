using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.RateLimiting;

namespace Interview.Runner;

public class RunnerProgram
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1024 * 1024);
        builder.Services.AddSingleton<DockerSandbox>();
        builder.Services.AddRateLimiter(options => { options.RejectionStatusCode = 429; options.AddFixedWindowLimiter("jobs", limiter => { limiter.PermitLimit = 20; limiter.Window = TimeSpan.FromMinutes(1); limiter.QueueLimit = 0; }); });
        var app = builder.Build();
        var key = app.Configuration["Runner:ApiKey"];
        if (string.IsNullOrWhiteSpace(key) || key.Length < 32) throw new InvalidOperationException("Задайте Runner:ApiKey длиной минимум 32 символа через секреты или Runner__ApiKey.");
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/execute"))
            {
                var supplied = context.Request.Headers.Authorization.ToString();
                var expected = "Bearer " + key;
                if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(supplied)), SHA256.HashData(Encoding.UTF8.GetBytes(expected)))) { context.Response.StatusCode = 401; return; }
            }
            await next();
        });
        app.UseRateLimiter();
        app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));
        app.MapPost("/execute", async (SandboxJob job, DockerSandbox sandbox, HttpContext context) =>
        {
            if (!SandboxJob.Valid(job)) return Results.BadRequest(new { detail = "Некорректный запрос sandbox." });
            try { return Results.Ok(await sandbox.Execute(job, context.RequestAborted)); }
            catch (InvalidOperationException) { return Results.Json(new { detail = "Docker или подготовленный образ sandbox недоступен." }, statusCode: 503); }
        }).RequireRateLimiting("jobs");
        await app.RunAsync();
    }
}

public record SandboxJob(Guid RunId, string Language, string Code, int TimeLimitMs, int MemoryLimitMb, SandboxInput[] Tests)
{
    public static readonly string[] Languages = ["Python", "JavaScript", "TypeScript", "C++", "C#", "Go", "Java"];
    public static bool Valid(SandboxJob job) => 
        job != null && Languages.Contains(job.Language) && job.Code != null && job.Code.Length <= 20000
        && job.TimeLimitMs is >= 100 and <= 10000 && job.MemoryLimitMb is >= 32 and <= 1024 && job.Tests is { Length: >= 1 and <= 50 }
        && job.Tests.All(test => test != null && test.Input != null && test.Input.Length <= 10000 && test.Position >= 0)
        && job.Tests.Select(test => test.Position).Distinct().Count() == job.Tests.Length;
}
public record SandboxInput(int Position, string Input);
public record SandboxResult(SandboxTestResult[] Tests);
public record SandboxTestResult(int Position, string Output, int ExitCode, bool TimedOut, int DurationMs, int MemoryKb, string? ErrorKind = null);
