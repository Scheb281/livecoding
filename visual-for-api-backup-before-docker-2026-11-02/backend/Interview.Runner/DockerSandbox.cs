using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Interview.Runner;

// Сервис доверенный и имеет доступ к Docker. Код кандидата получает только одноразовый контейнер.
public sealed class DockerSandbox(IConfiguration config)
{
    private readonly SemaphoreSlim capacity = new(1, 1);
    private readonly string image = config["Runner:SandboxImage"] ?? "visual-interview-sandbox:local";
    public async Task<SandboxResult> Execute(SandboxJob job, CancellationToken cancellation)
    {
        await capacity.WaitAsync(cancellation);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromSeconds(180));
            var results = new List<SandboxTestResult>();
            // Каждый тест получает новый контейнер: скрытый ввод не остаётся для следующего теста.
            foreach (var test in job.Tests) results.Add(await ExecuteTest(job, test, deadline.Token));
            return new(results.ToArray());
        }
        finally { capacity.Release(); }
    }

    public static string[] CreateArguments(string name, string image, int memory) => [
        "create", "--pull=never", "--name", name, "--label", "interview.sandbox=true", "--interactive",
        "--network", "none", "--read-only", "--cap-drop", "ALL", "--security-opt", "no-new-privileges:true",
        "--pids-limit", "64", "--cpus", "1", "--memory", $"{Math.Max(768, memory)}m", "--memory-swap", $"{Math.Max(768, memory)}m",
        "--ulimit", "nofile=256:256", "--ulimit", "core=0:0", "--log-driver", "none", "--user", "65532:65532",
        "--tmpfs", "/work:rw,exec,nosuid,nodev,size=192m,uid=65532,gid=65532,mode=0700",
        "--tmpfs", "/tmp:rw,nosuid,nodev,noexec,size=32m,mode=1777", "--workdir", "/work",
        "--env", "HOME=/work", "--env", "DOTNET_CLI_HOME=/work/.dotnet", "--env", "DOTNET_CLI_TELEMETRY_OPTOUT=1",
        "--env", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1", "--env", "DOTNET_GENERATE_ASPNET_CERTIFICATE=false",
        "--entrypoint", "python3", image, "/opt/interview/shim.py"
    ];

    private async Task<SandboxTestResult> ExecuteTest(SandboxJob job, SandboxInput test, CancellationToken cancellation)
    {
        var name = "interview-" + Guid.NewGuid().ToString("N");
        Process? attach = null;
        try
        {
            await Command(CreateArguments(name, image, job.MemoryLimitMb), cancellation);
            attach = Start(["start", "--attach", "--interactive", name]);
            using var attachTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            attachTimeout.CancelAfter(TimeSpan.FromMilliseconds(40000 + job.TimeLimitMs));
            // JSON идёт в stdin docker attach. Он никогда не становится командой хостового shell.
            var payload = JsonSerializer.Serialize(new { job.Language, job.Code, test.Input, job.TimeLimitMs, job.MemoryLimitMb }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await attach.StandardInput.WriteLineAsync(payload.AsMemory(), attachTimeout.Token);
            await attach.StandardInput.FlushAsync(attachTimeout.Token);
            var phase = await ReadLine(attach.StandardOutput, attachTimeout.Token);
            using var phaseJson = JsonDocument.Parse(phase);
            if (phaseJson.RootElement.GetProperty("phase").GetString() == "compiled")
            {
                // Компилятору доступно до 768 МБ, программе — строго лимит задачи.
                await Command(["update", "--memory", $"{job.MemoryLimitMb}m", "--memory-swap", $"{job.MemoryLimitMb}m", name], cancellation);
                await attach.StandardInput.WriteLineAsync("run");
                await attach.StandardInput.FlushAsync();
                phase = await ReadLine(attach.StandardOutput, attachTimeout.Token);
            }
            var result = JsonSerializer.Deserialize<ShimResult>(phase, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidOperationException("Нет результата sandbox.");
            return new(test.Position, result.Output ?? "", result.ExitCode, result.TimedOut, result.DurationMs, result.MemoryKb, result.ErrorKind);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return new(test.Position, "Превышено время проверки.", -1, true, job.TimeLimitMs, 0, "timeout"); }
        catch (Exception error) when (error is EndOfStreamException or JsonException) { return new(test.Position, "Процесс завершён лимитом памяти или ресурсов.", -1, false, 0, 0, "resource"); }
        finally
        {
            if (attach != null) { try { if (!attach.HasExited) attach.Kill(true); } catch (InvalidOperationException) { } attach.Dispose(); }
            // Cleanup выполняется даже при отмене HTTP-запроса. В контейнер нет host-монтов.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await Command(["rm", "--force", name], cleanup.Token); } catch { /* имя содержит только локальный GUID; orphan-очистка описана в README */ }
        }
    }

    private static Process Start(IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo("docker") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new InvalidOperationException("Docker CLI не запущен.");
    }
    private static async Task Command(string[] arguments, CancellationToken cancellation)
    {
        using var process = Start(arguments);
        // Docker-команды получают только фиксированные аргументы и локальный GUID.
        var stdout = Drain(process.StandardOutput, cancellation);
        var stderr = Drain(process.StandardError, cancellation);
        try { await process.WaitForExitAsync(cancellation); await Task.WhenAll(stdout, stderr); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        if (process.ExitCode != 0) throw new InvalidOperationException("Команда Docker не выполнена.");
    }
    private static async Task Drain(StreamReader reader, CancellationToken cancellation)
    {
        var buffer = new char[4096];
        while (await reader.ReadAsync(buffer.AsMemory(), cancellation) != 0) { }
    }
    private static async Task<string> ReadLine(StreamReader reader, CancellationToken cancellation)
    {
        var result = new StringBuilder(); var character = new char[1];
        while (await reader.ReadAsync(character.AsMemory(), cancellation) > 0)
        {
            if (character[0] == '\n') return result.ToString();
            if (result.Length >= 262144) throw new JsonException("Слишком большой ответ процесса.");
            result.Append(character[0]);
        }
        throw new EndOfStreamException();
    }
    private record ShimResult(string Phase, string Output, int ExitCode, bool TimedOut, int DurationMs, int MemoryKb, string? ErrorKind);
}
