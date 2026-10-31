using System.Text.Json;
using Interview.Api.Contracts;
using Interview.Api.Data;
using Interview.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Interview.Api.Services;

public sealed class DtoService(AppDbContext db)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static readonly HashSet<string> Languages = ["Python", "JavaScript", "TypeScript", "C++", "C#", "Go", "Java"];
    public static string Serialize<T>(T data) => JsonSerializer.Serialize(data, Json);
    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Json)!;
    public static DocumentDto Document(CodeDocument document) => new(document.TaskId, document.Language, document.Code, document.Version);
    public static TaskDto Task(BankTask task) => new(task.Id, task.Title, task.Description, task.Topic, task.Difficulty,
        task.Company, task.Tags, task.Language, task.StarterCode, task.TimeLimitMs, task.MemoryLimitMb,
        task.Tests.OrderBy(x => x.Position).Select(x => new TestDto(x.Id, x.Input, x.Expected, x.Hidden)).ToArray());
    public static RubricDto Rubric(Rubric rubric) => new(rubric.Id, rubric.Name,
        rubric.Criteria.OrderBy(x => x.Position).Select(x => new CriterionDto(x.Key, x.Label, x.MaxScore)).ToArray());

    public async Task<UserDto> User(AppUser user)
    {
        var roomId = user.Role == "candidate" ? await db.Rooms.Where(x => x.CandidateId == user.Id)
            .OrderByDescending(x => x.CreatedAt).Select(x => (Guid?)x.Id).FirstOrDefaultAsync() : null;
        return new(user.Id, user.DisplayName, user.Email ?? "", user.Role, user.IsActive, user.RunQuota, roomId);
    }

    // Закрытые тесты и оценки не попадают в JSON кандидата даже при прямом запросе API.
    public async Task<object> Room(Room room, AppUser actor)
    {
        var assignments = await db.RoomTasks.Where(x => x.RoomId == room.Id).OrderBy(x => x.Position).ToListAsync();
        var tasks = assignments.Select(x => Deserialize<TaskDto>(x.TaskSnapshot)).Select(task => actor.Role == "candidate"
            ? task with { Tests = task.Tests.Where(x => !x.Hidden).ToArray() } : task).ToArray();
        var messages = await db.Messages.Where(x => x.RoomId == room.Id).OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync();
        var hints = await db.Hints.Where(x => x.RoomId == room.Id).OrderBy(x => x.CreatedAt).ToListAsync();
        var usages = await db.HintUsages.Where(x => hints.Select(hint => hint.Id).Contains(x.HintId)).ToListAsync();
        var evaluations = actor.Role == "candidate" ? [] : await db.Evaluations.Include(x => x.Scores).Where(x => x.RoomId == room.Id).ToListAsync();
        var submissions = await db.Submissions.Where(x => x.RoomId == room.Id).OrderBy(x => x.CreatedAt)
            .Select(x => new { x.Id, x.TaskId, x.DocumentVersion, x.CreatedAt }).ToListAsync();
        return new
        {
            room.Id,
            room.Title,
            room.CandidateName,
            room.Status,
            room.CreatedAt,
            TaskIds = tasks.Select(x => x.Id),
            Tasks = tasks,
            Rubric = Deserialize<RubricDto>(room.RubricSnapshot),
            Messages = messages.OrderBy(x => x.CreatedAt).Select(x => new { x.Id, x.Author, x.Text, x.CreatedAt }),
            Hints = hints.Select(x => new { x.Id, x.TaskId, x.Text, x.CreatedAt, UsedAt = usages.FirstOrDefault(u => u.HintId == x.Id)?.UsedAt }),
            Evaluations = evaluations.Select(x => new { x.Id, Author = "Интервьюер", Scores = x.Scores.ToDictionary(s => s.CriterionKey, s => s.Score), x.Comment }),
            Submissions = submissions,
            CanEditCode = actor.Role == "candidate" && room.CandidateId == actor.Id && room.Status == "active",
            CanInterview = actor.Role == "interviewer" && room.InterviewerId == actor.Id
        };
    }

    public static object Run(CodeRun run) => new
    {
        run.Id,
        run.Status,
        run.Summary,
        run.Output,
        Tests = run.Tests.OrderBy(x => x.Position).Select(x => new { Id = x.Position, x.Passed })
    };
}
