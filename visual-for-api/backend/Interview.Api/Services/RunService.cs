using Interview.Api.Contracts;
using Interview.Api.Data;
using Interview.Api.Infrastructure;
using Interview.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Interview.Api.Services;

public sealed class RunService(AppDbContext db, IConfiguration config, AccessService access, SessionRecorder recorder)
{
    public bool Enabled => !string.IsNullOrWhiteSpace(config["Runner:BaseUrl"]) && !string.IsNullOrWhiteSpace(config["Runner:ApiKey"]);

    // Квота резервируется в той же транзакции, что и запись задания в очередь.
    public async Task<CodeRun> Create(AppUser actor, Guid roomId, VersionRequest request)
    {
        AccessService.Active(await access.Room(actor, roomId));
        ApiError.Require(Enabled, "Сервис выполнения пока не подключён. Настройте отдельный изолированный runner.", 503);
        await using var transaction = await db.Database.BeginTransactionAsync();
        
        await DatabaseLocks.Acquire(db, "run-queue");
        await DatabaseLocks.Acquire(db, "quota:" + actor.Id);
        await DatabaseLocks.Acquire(db, "room:" + roomId);
        
        var room = await access.Room(actor, roomId); 
        AccessService.Active(room);
        var document = await access.Document(roomId, request.TaskId);

        ApiError.Require(document.Version == request.DocumentVersion, "Сначала сохраните актуальную версию кода.", 409);
        ApiError.Require(actor.RunQuota > 0, "Запуски кода запрещены квотой.", 429);
        ApiError.Require(await db.Runs.CountAsync(x => x.Status == "queued" || x.Status == "running") < 100, "Очередь запусков заполнена. Повторите позже.", 429);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var usage = await db.DailyRunUsages.SingleOrDefaultAsync(x => x.UserId == actor.Id && x.UtcDate == today);
        if (usage == null) 
        { 
            usage = new DailyRunUsage { UserId = actor.Id, UtcDate = today }; 
            db.DailyRunUsages.Add(usage); 
        }
        ApiError.Require(usage.ReservedRuns < actor.RunQuota, "Суточная квота запусков исчерпана.", 429);
        ApiError.Require(
            await db.Runs.CountAsync(x => x.UserId == actor.Id && (x.Status == "queued" || x.Status == "running")) < 2,
            "Дождитесь предыдущих запусков.",
            429);
        usage.ReservedRuns++;
        var run = new CodeRun { RoomId = roomId, TaskId = request.TaskId, UserId = actor.Id, DocumentVersion = document.Version, Language = document.Language, Code = document.Code };
        db.Runs.Add(run); access.Audit(actor, "code-run.queue", run.Id);
        
        await recorder.Append(roomId, "RunQueued", actor, new { run.Id, run.DocumentVersion }, request.TaskId);
        await transaction.CommitAsync();

        return run;
    }
}
