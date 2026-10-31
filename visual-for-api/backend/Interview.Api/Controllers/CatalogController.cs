using Interview.Api.Contracts;
using Interview.Api.Data;
using Interview.Api.Infrastructure;
using Interview.Api.Models;
using Interview.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Interview.Api.Controllers;

[ApiController, Route("api"), Authorize]
public class CatalogController(AppDbContext db, AccessService access) : ControllerBase
{
    private async Task<AppUser> Staff()
    {
        var actor = await access.Actor(User);
        AccessService.Role(actor, "interviewer", "teamlead");
        return actor;
    }

    [HttpGet("tasks")]
    public async Task<object> Tasks()
    {
        await Staff();
        return (await db.Tasks.Include(x => x.Tests).OrderByDescending(x => x.CreatedAt).ToListAsync()).Select(DtoService.Task);
    }

    [HttpPost("tasks")]
    public async Task<object> CreateTask(TaskRequest request)
    {
        var actor = await Staff();
        var task = new BankTask { AuthorId = actor.Id };
        ApplyTask(task, request);
        db.Tasks.Add(task);
        access.Audit(actor, "task.create", task.Id);
        await db.SaveChangesAsync();
        return DtoService.Task(task);
    }

    [HttpPut("tasks/{id:guid}")]
    public async Task<object> UpdateTask(Guid id, TaskRequest request)
    {
        var actor = await Staff();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await DatabaseLocks.Acquire(db, "task:" + id);
        var task = await db.Tasks.Include(x => x.Tests).SingleOrDefaultAsync(x => x.Id == id) ?? throw new ApiError(404, "Задача не найдена.");
        ApiError.Require(task.AuthorId == actor.Id || actor.Role == "teamlead", "Изменять чужие задачи может только тимлид.", 403);
        // Сначала удаляем прежние позиции, чтобы не конфликтовали уникальные индексы.
        db.TaskTests.RemoveRange(task.Tests);
        await db.SaveChangesAsync();
        task.Tests.Clear();
        ApplyTask(task, request);
        db.TaskTests.AddRange(task.Tests);
        task.Version++;
        access.Audit(actor, "task.update", id);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return DtoService.Task(task);
    }

    private static void ApplyTask(BankTask task, TaskRequest request)
    {
        ApiError.Require(DtoService.Languages.Contains(request.Language), "Язык не поддерживается.");
        ApiError.Require(new[] { "easy", "medium", "hard" }.Contains(request.Difficulty), "Неизвестная сложность.");
        ApiError.Require(request.Tags.All(x => !string.IsNullOrWhiteSpace(x) && x.Length <= 100), "Проверьте теги задачи.");
        ApiError.Require(request.Tests.All(x => x != null && x.Input != null && x.Expected != null && x.Input.Length <= 10000 && x.Expected.Length <= 10000), "Слишком большой или некорректный тест.");
        task.Title = request.Title.Trim(); task.Description = request.Description; task.Topic = request.Topic.Trim();
        task.Difficulty = request.Difficulty; task.Company = request.Company?.Trim() ?? "";
        task.Tags = request.Tags.Select(x => x.Trim()).Distinct().ToArray(); task.Language = request.Language;
        task.StarterCode = request.StarterCode; task.TimeLimitMs = request.TimeLimitMs; task.MemoryLimitMb = request.MemoryLimitMb;
        task.Tests = request.Tests.Select((test, index) => new TaskTest { TaskId = task.Id, Position = index, Input = test.Input, Expected = test.Expected, Hidden = test.Hidden }).ToList();
    }

    [HttpGet("rubrics")]
    public async Task<object> Rubrics()
    {
        await Staff();
        return (await db.Rubrics.Include(x => x.Criteria).OrderBy(x => x.CreatedAt).ToListAsync()).Select(DtoService.Rubric);
    }

    [HttpPost("rubrics")]
    public async Task<object> CreateRubric(RubricRequest request)
    {
        var actor = await Staff(); AccessService.Role(actor, "teamlead");
        ApiError.Require(request.Criteria.All(x => x != null && !string.IsNullOrWhiteSpace(x.Id) && x.Id.Length <= 50 && !string.IsNullOrWhiteSpace(x.Label) && x.Label.Length <= 100 && x.MaxScore is >= 1 and <= 100), "Проверьте критерии рубрики.");
        ApiError.Require(request.Criteria.Select(x => x.Id).Distinct().Count() == request.Criteria.Length, "Критерии не должны повторяться.");
        var rubric = new Rubric { OwnerId = actor.Id, Name = request.Name.Trim() };
        rubric.Criteria = request.Criteria.Select((criterion, index) => new RubricCriterion { RubricId = rubric.Id, Key = criterion.Id, Label = criterion.Label, MaxScore = criterion.MaxScore, Position = index }).ToList();
        db.Rubrics.Add(rubric); access.Audit(actor, "rubric.create", rubric.Id);
        await db.SaveChangesAsync();
        return DtoService.Rubric(rubric);
    }
}
