using Interview.Api.Contracts;
using Interview.Api.Data;
using Interview.Api.Infrastructure;
using Interview.Api.Models;
using Interview.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Interview.Api.Controllers;

[ApiController, Route("api/rooms"), Authorize]
public class RoomsController(AppDbContext db, AccessService access, DtoService dto, RoomConnections connections, SessionRecorder recorder) : ControllerBase
{
    [HttpGet]
    public async Task<object> List()
    {
        var actor = await access.Actor(User); AccessService.Role(actor, "candidate", "interviewer");
        var rooms = await db.Rooms.Where(x => actor.Role == "candidate" ? x.CandidateId == actor.Id : x.InterviewerId == actor.Id).OrderByDescending(x => x.CreatedAt).ToListAsync();
        var result = new List<object>();
        foreach (var room in rooms) result.Add(await dto.Room(room, actor));
        return result;
    }

    [HttpPost]
    public async Task<object> Create(RoomRequest request)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var actor = await access.Actor(User); AccessService.Role(actor, "interviewer");
        ApiError.Require(request.TaskIds.Distinct().Count() == request.TaskIds.Length, "Одна задача указана несколько раз.");
        var tasks = await db.Tasks.Include(x => x.Tests).Where(x => request.TaskIds.Contains(x.Id)).ToListAsync();
        ApiError.Require(tasks.Count == request.TaskIds.Length, "Некоторые задачи не найдены.");
        var rubric = await db.Rubrics.Include(x => x.Criteria).SingleOrDefaultAsync(x => x.Id == request.RubricId) ?? throw new ApiError(404, "Рубрика не найдена.");
        var room = new Room { Title = request.Title.Trim(), CandidateName = request.CandidateName.Trim(), InterviewerId = actor.Id, RubricId = rubric.Id, RubricSnapshot = DtoService.Serialize(DtoService.Rubric(rubric)), DurationMinutes = request.DurationMinutes };
        db.Rooms.Add(room);
        foreach (var (taskId, position) in request.TaskIds.Select((id, index) => (id, index)))
        {
            var task = tasks.Single(x => x.Id == taskId);
            db.RoomTasks.Add(new RoomTask { RoomId = room.Id, TaskId = task.Id, Position = position, TaskSnapshot = DtoService.Serialize(DtoService.Task(task)) });
            db.Documents.Add(new CodeDocument { RoomId = room.Id, TaskId = task.Id, Language = task.Language, Code = task.StarterCode });
        }
        access.Audit(actor, "room.create", room.Id); await db.SaveChangesAsync();
        await recorder.Append(room.Id, "RoomCreated", actor, new { room.Title, room.DurationMinutes });
        await transaction.CommitAsync();
        return await dto.Room(room, actor);
    }

    [HttpGet("{id:guid}")]
    public async Task<object> Get(Guid id)
    {
        var actor = await access.Actor(User); return await dto.Room(await access.Room(actor, id), actor);
    }

    [HttpGet("{id:guid}/documents/{taskId:guid}")]
    public async Task<object> Document(Guid id, Guid taskId)
    {
        await access.Room(await access.Actor(User), id); return DtoService.Document(await access.Document(id, taskId));
    }

    [HttpPost("{id:guid}/open")]
    public async Task<object> Open(Guid id)
    {
        var actor = await access.Actor(User); AccessService.Role(actor, "interviewer");
        await using var transaction = await db.Database.BeginTransactionAsync();
        await DatabaseLocks.Acquire(db, "room:" + id);
        var room = await access.Room(actor, id);
        ApiError.Require(room.Status == "draft", "Комната уже открыта.", 409);
        room.Status = "waiting";
        await recorder.Append(id, "RoomOpened", actor, new { room.Status });
        await transaction.CommitAsync(); await connections.Changed(id);
        return await dto.Room(room, actor);
    }

    [HttpPost("{id:guid}/start")]
    public async Task<object> Start(Guid id)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await DatabaseLocks.Acquire(db, "room:" + id);
        var actor = await access.Actor(User); AccessService.Role(actor, "interviewer");
        var room = await access.Room(actor, id);
        ApiError.Require(room.Status is "draft" or "waiting", "Запустить можно только подготовленную комнату.", 409);
        room.Status = "active"; room.StartedAt = DateTime.UtcNow;
        room.SessionExpiresAt = room.StartedAt.Value.AddMinutes(room.DurationMinutes);
        access.Audit(actor, "room.start", id);
        await recorder.Append(id, "SessionStarted", actor, new { room.StartedAt, room.SessionExpiresAt });
        await transaction.CommitAsync(); await connections.Changed(id);
        return await dto.Room(room, actor);
    }

    [HttpPost("{id:guid}/complete")]
    public async Task<object> Complete(Guid id)
    {
        var actor = await access.Actor(User); AccessService.Role(actor, "interviewer");
        await using var transaction = await db.Database.BeginTransactionAsync();
        await DatabaseLocks.Acquire(db, "room:" + id);
        var room = await access.Room(actor, id); AccessService.Active(room);
        room.Status = "completed"; room.CompletedAt = DateTime.UtcNow;
        access.Audit(actor, "room.complete", id); await recorder.Append(id, "SessionFinished", actor, new { room.CompletedAt }); await transaction.CommitAsync();
        await connections.Changed(id); return await dto.Room(room, actor);
    }

    [HttpPost("{id:guid}/invitations")]
    public async Task<object> Invite(Guid id)
    {
        var actor = await access.Actor(User); AccessService.Role(actor, "interviewer");
        await using var transaction = await db.Database.BeginTransactionAsync();
        await DatabaseLocks.Acquire(db, "room:" + id);
        var room = await access.Room(actor, id); AccessService.Invitable(room);
        ApiError.Require(room.CandidateId == null, "Кандидат уже подключён к комнате.", 409);
        // Новая ссылка отменяет все предыдущие неиспользованные ссылки комнаты.
        var previous = await db.Invitations.Where(x => x.RoomId == id && x.ConsumedAt == null).ToListAsync();
        previous.ForEach(x => x.ExpiresAt = DateTime.UtcNow);
        var token = AuthController.NewToken();
        var invitation = new Invitation { RoomId = id, CreatedById = actor.Id, TokenHash = AuthController.HashToken(token), ExpiresAt = DateTime.UtcNow.AddHours(1) };
        db.Invitations.Add(invitation); access.Audit(actor, "invitation.create", id);
        await db.SaveChangesAsync(); await transaction.CommitAsync();
        return new { Token = token, invitation.ExpiresAt };
    }

    [HttpPost("{id:guid}/messages")]
    public async Task<object> Message(Guid id, MessageRequest request)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await DatabaseLocks.Acquire(db, "room:" + id);
        var actor = await access.Actor(User); var room = await access.Room(actor, id); AccessService.Active(room);
        db.Messages.Add(new ChatMessage { RoomId = id, AuthorId = actor.Id, Author = actor.DisplayName, Text = request.Text.Trim() });
        await recorder.Append(id, "ChatMessage", actor, new { Text = request.Text.Trim() });
        await transaction.CommitAsync(); await connections.Changed(id); return await dto.Room(room, actor);
    }

    [HttpPost("{id:guid}/hints")]
    public async Task<object> Hint(Guid id, HintRequest request)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await DatabaseLocks.Acquire(db, "room:" + id);
        var actor = await access.Actor(User); AccessService.Role(actor, "interviewer");
        var room = await access.Room(actor, id); AccessService.Active(room); await access.Document(id, request.TaskId);
        var hint = new Hint { RoomId = id, TaskId = request.TaskId, AuthorId = actor.Id, Text = request.Text.Trim() };
        db.Hints.Add(hint); access.Audit(actor, "hint.create", hint.Id);
        await recorder.Append(id, "HintAdded", actor, new { hint.Id, hint.Text }, request.TaskId);
        await transaction.CommitAsync(); await connections.Changed(id); return await dto.Room(room, actor);
    }

    [HttpPost("{id:guid}/hint-usage")]
    public async Task<object> HintUsage(Guid id, HintUsageRequest request)
    {
        var actor = await access.Actor(User);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await DatabaseLocks.Acquire(db, "hint:" + request.HintId);
        var room = await access.Room(actor, id); AccessService.Active(room);
        ApiError.Require(room.CandidateId != null, "Кандидат пока не подключён.", 409);
        ApiError.Require(await db.Hints.AnyAsync(x => x.Id == request.HintId && x.RoomId == id), "Подсказка не найдена.", 404);
        if (!await db.HintUsages.AnyAsync(x => x.HintId == request.HintId && x.CandidateId == room.CandidateId))
        {
            db.HintUsages.Add(new HintUsage { HintId = request.HintId, CandidateId = room.CandidateId!, MarkedById = actor.Id });
            access.Audit(actor, "hint.used", request.HintId); await recorder.Append(id, "HintUsed", actor, new { request.HintId });
        }
        await transaction.CommitAsync(); await connections.Changed(id); return await dto.Room(room, actor);
    }

    [HttpPost("{id:guid}/evaluations")]
    public async Task<object> Evaluate(Guid id, EvaluationRequest request)
    {
        var actor = await access.Actor(User); AccessService.Role(actor, "interviewer");
        await using var transaction = await db.Database.BeginTransactionAsync();
        await DatabaseLocks.Acquire(db, "room:" + id);
        var room = await access.Room(actor, id);
        var rubric = DtoService.Deserialize<RubricDto>(room.RubricSnapshot);
        ApiError.Require(request.RubricId == room.RubricId && request.Scores.Count == rubric.Criteria.Length
            && rubric.Criteria.All(x => request.Scores.TryGetValue(x.Id, out var score) && score >= 0 && score <= x.MaxScore), "Оценки не соответствуют рубрике комнаты.");
        var evaluation = await db.Evaluations.Include(x => x.Scores).SingleOrDefaultAsync(x => x.RoomId == id && x.InterviewerId == actor.Id);
        if (evaluation == null) { evaluation = new Evaluation { RoomId = id, InterviewerId = actor.Id }; db.Evaluations.Add(evaluation); }
        else { db.EvaluationScores.RemoveRange(evaluation.Scores); await db.SaveChangesAsync(); evaluation.Scores.Clear(); }
        evaluation.Comment = request.Comment ?? "";
        evaluation.Scores = request.Scores.Select(x => new EvaluationScore { EvaluationId = evaluation.Id, CriterionKey = x.Key, Score = x.Value }).ToList();
        access.Audit(actor, "evaluation.save", evaluation.Id);
        await recorder.Append(id, "EvaluationSaved", actor, new { request.Scores, request.Comment, FinalScore = ScoringService.Calculate(rubric, request.Scores) }, visibility: "staff");
        await transaction.CommitAsync();
        await connections.Changed(id); return await dto.Room(room, actor);
    }
}
