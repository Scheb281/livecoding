using Interview.Api.Contracts;
using Interview.Api.Data;
using Interview.Api.Infrastructure;
using Interview.Api.Models;
using Interview.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Interview.Api.Controllers;

[ApiController, Route("api/rooms/{id:guid}"), Authorize]
public class SolutionsController(AppDbContext db, AccessService access, DtoService dto, RoomConnections connections, RunService runs, SessionRecorder recorder) : ControllerBase
{
    [HttpPost("runs")]
    public async Task<object> Run(Guid id, VersionRequest request)
    {
        var actor = await access.Actor(User); await access.Room(actor, id);
        return DtoService.Run(await runs.Create(actor, id, request));
    }

    [HttpGet("runs/{runId:guid}")]
    public async Task<object> RunStatus(Guid id, Guid runId)
    {
        var actor = await access.Actor(User); await access.Room(actor, id);
        var run = await db.Runs.Include(x => x.Tests).SingleOrDefaultAsync(x => x.Id == runId && x.RoomId == id) ?? throw new ApiError(404, "Запуск не найден.");
        return DtoService.Run(run);
    }

    [HttpPost("submissions")]
    public async Task<object> Submit(Guid id, VersionRequest request)
    {
        var actor = await access.Actor(User); AccessService.Role(actor, "candidate");
        await using var transaction = await db.Database.BeginTransactionAsync();
        await DatabaseLocks.Acquire(db, "room:" + id);
        var room = await access.Room(actor, id); AccessService.Active(room);
        var document = await access.Document(id, request.TaskId);
        ApiError.Require(document.Version == request.DocumentVersion, "Сначала сохраните актуальную версию кода.", 409);
        var existing = await db.Submissions.SingleOrDefaultAsync(x => x.RoomId == id && x.TaskId == request.TaskId && x.CandidateId == actor.Id && x.DocumentVersion == request.DocumentVersion);
        if (existing == null)
        {
            var verified = await db.Runs.Where(x => x.RoomId == id && x.TaskId == request.TaskId && x.DocumentVersion == document.Version && x.Language == document.Language && x.Status == "completed" && x.AllPassed)
                .OrderByDescending(x => x.CreatedAt).Select(x => (Guid?)x.Id).FirstOrDefaultAsync();
            existing = new Submission { RoomId = id, TaskId = request.TaskId, CandidateId = actor.Id, DocumentVersion = document.Version, Language = document.Language, Code = document.Code, VerifiedRunId = verified };
            db.Submissions.Add(existing); access.Audit(actor, "solution.submit", existing.Id); await recorder.Append(id, "SolutionSubmitted", actor, new { existing.Id, existing.DocumentVersion }, request.TaskId);
        }
        await transaction.CommitAsync(); await connections.Changed(id);
        return await dto.Room(room, actor);
    }
}
