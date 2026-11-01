using System.Net.Http.Headers;
using System.Text;
using Interview.Api.Contracts;
using Interview.Api.Data;
using Interview.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Interview.Api.Services;

// Этот процесс НЕ запускает Python, dotnet или shell. Он обращается к отдельному runner.
public sealed class RunWorker(IServiceScopeFactory scopes, IHttpClientFactory clients, IConfiguration config, ILogger<RunWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(config["Runner:BaseUrl"]) || string.IsNullOrWhiteSpace(config["Runner:ApiKey"])) return;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Единственный worker: незавершённые задания предыдущего процесса не запускаем повторно.
            var interrupted = await db.Runs.Where(x => x.Status == "running").ToListAsync(stoppingToken);
            foreach (var run in interrupted) { run.Status = "failed"; run.Summary = "Сервис был перезапущен во время проверки."; run.CompletedAt = DateTime.UtcNow; }
            await db.SaveChangesAsync(stoppingToken);
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await ExecuteOne(stoppingToken)) await Task.Delay(1000, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "Ошибка обработки очереди запусков"); await Task.Delay(3000, stoppingToken); }
        }
    }

    private async Task<bool> ExecuteOne(CancellationToken cancellation)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var run = await db.Runs.Where(x => x.Status == "queued").OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(cancellation);
        if (run == null) return false;
        run.Status = "running"; await db.SaveChangesAsync(cancellation);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(180));
            var runnerCancellation = timeout.Token;
            var assignment = await db.RoomTasks.SingleAsync(x => x.RoomId == run.RoomId && x.TaskId == run.TaskId, cancellation);
            var task = DtoService.Deserialize<TaskDto>(assignment.TaskSnapshot);
            var runnerRequest = new RunnerRequest(run.Id, run.Language, run.Code, task.TimeLimitMs, task.MemoryLimitMb,
                task.Tests.Select((test, index) => new RunnerTest(index, test.Input)).ToArray());
            var client = clients.CreateClient("runner");
            // Только URL из конфигурации оператора. Ни один браузерный параметр не задаёт адрес runner.
            using var request = new HttpRequestMessage(HttpMethod.Post, "execute") { Content = JsonContent.Create(runnerRequest) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config["Runner:ApiKey"]);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, runnerCancellation);
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync(runnerCancellation);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(chunk, runnerCancellation)) > 0)
            {
                if (buffer.Length + count > 1024 * 1024) throw new InvalidOperationException("Слишком большой ответ runner.");
                await buffer.WriteAsync(chunk.AsMemory(0, count), cancellation);
            }
            var result = DtoService.Deserialize<RunnerResult>(Encoding.UTF8.GetString(buffer.ToArray()));
            if (result.Tests == null || result.Tests.Length != task.Tests.Length || result.Tests.Select(x => x.Position).Distinct().Count() != task.Tests.Length
                || result.Tests.Any(x => x.Position < 0 || x.Position >= task.Tests.Length)) throw new InvalidOperationException("Неполный ответ runner.");
            var visibleOutput = new StringBuilder();
            foreach (var testResult in result.Tests.OrderBy(x => x.Position))
            {
                var test = task.Tests[testResult.Position];
                var passed = !testResult.TimedOut && testResult.ErrorKind == null && testResult.ExitCode == 0 && Normalize(testResult.Output) == Normalize(test.Expected);
                run.Tests.Add(new CodeRunTest
                {
                    RunId = run.Id,
                    Position = testResult.Position,
                    Passed = passed,
                    ErrorKind = testResult.ErrorKind,
                    DurationMs = Math.Max(0, testResult.DurationMs),
                    MemoryKb = Math.Max(0, testResult.MemoryKb)
                });
                // Вывод скрытого теста может содержать его вход: не раскрываем его кандидату.
                if (!test.Hidden && visibleOutput.Length < 32768) visibleOutput.Append((testResult.Output ?? "")[..Math.Min(testResult.Output?.Length ?? 0, 32768 - visibleOutput.Length)]);
            }
            run.Output = visibleOutput.ToString(); run.AllPassed = run.Tests.All(x => x.Passed); run.Status = "completed";
            run.Summary = $"Пройдено {run.Tests.Count(x => x.Passed)} из {run.Tests.Count} тестов.";
            // Если решение отправили до окончания проверки, связываем точный неизменяемый снимок.
            if (run.AllPassed)
            {
                var submissions = await db.Submissions.Where(x => x.RoomId == run.RoomId && x.TaskId == run.TaskId && x.DocumentVersion == run.DocumentVersion && x.Language == run.Language).ToListAsync(cancellation);
                submissions.ForEach(x => x.VerifiedRunId = run.Id);
            }
        }
        catch (Exception error) when (error is not OperationCanceledException || !cancellation.IsCancellationRequested)
        {
            run.Status = "failed"; run.Summary = "Не удалось выполнить проверку в изолированном сервисе.";
            logger.LogWarning("Runner не завершил запуск {RunId}: {ErrorType}", run.Id, error.GetType().Name);
        }
        run.CompletedAt = DateTime.UtcNow;
        await scope.ServiceProvider.GetRequiredService<SessionRecorder>().Append(run.RoomId, "RunCompleted", null, new { run.Id, run.Status, run.Summary }, run.TaskId);
        await scope.ServiceProvider.GetRequiredService<RoomConnections>().Changed(run.RoomId);
        return true;
    }

    private static string Normalize(string? text) => (text ?? "").Replace("\r\n", "\n").TrimEnd('\r', '\n');
}
