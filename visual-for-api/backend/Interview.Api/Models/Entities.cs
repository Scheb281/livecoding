using Microsoft.AspNetCore.Identity;

namespace Interview.Api.Models;

// Identity самостоятельно хранит хеши паролей и данные для входа.
public class AppUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = "candidate";
    public bool IsActive { get; set; } = true;
    public int RunQuota { get; set; } = 30;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class BankTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AuthorId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Topic { get; set; } = "";
    public string Difficulty { get; set; } = "easy";
    public string Company { get; set; } = "";
    public string[] Tags { get; set; } = [];
    public string Language { get; set; } = "Python";
    public string StarterCode { get; set; } = "";
    public int TimeLimitMs { get; set; } = 2000;
    public int MemoryLimitMb { get; set; } = 128;
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<TaskTest> Tests { get; set; } = [];
}

public class TaskTest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    public int Position { get; set; }
    public string Input { get; set; } = "";
    public string Expected { get; set; } = "";
    public bool Hidden { get; set; }
}

public class Rubric
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<RubricCriterion> Criteria { get; set; } = [];
}

public class RubricCriterion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RubricId { get; set; }
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public int MaxScore { get; set; } = 5;
    public decimal Weight { get; set; } = 1m;
    public int Position { get; set; }
}

public class Room
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string InterviewerId { get; set; } = "";
    public string? CandidateId { get; set; }
    public string Title { get; set; } = "";
    public string CandidateName { get; set; } = "";
    public string Status { get; set; } = "draft";
    public Guid RubricId { get; set; }
    public string RubricSnapshot { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int DurationMinutes { get; set; } = 90;
    public DateTime? SessionExpiresAt { get; set; }
    public int NextEventSequence { get; set; }
}

public class RoomTask
{
    public Guid RoomId { get; set; }
    public Guid TaskId { get; set; }
    public int Position { get; set; }
    public string TaskSnapshot { get; set; } = "{}";
}

// Секретные токены никогда не храним открытым текстом в базе.
public class Invitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RoomId { get; set; }
    public string TokenHash { get; set; } = "";
    public string CreatedById { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
}

public class PasswordSetup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
}

public class CodeDocument
{
    public Guid RoomId { get; set; }
    public Guid TaskId { get; set; }
    public string Language { get; set; } = "";
    public string Code { get; set; } = "";
    public int Version { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class CodeEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RoomId { get; set; }
    public Guid TaskId { get; set; }
    public string AuthorId { get; set; } = "";
    public Guid MutationId { get; set; }
    public int Version { get; set; }
    public int? SessionSequence { get; set; }
    public string Language { get; set; } = "";
    public string Code { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RoomId { get; set; }
    public string AuthorId { get; set; } = "";
    public string Author { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Hint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RoomId { get; set; }
    public Guid TaskId { get; set; }
    public string AuthorId { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class HintUsage
{
    public Guid HintId { get; set; }
    public string CandidateId { get; set; } = "";
    public string MarkedById { get; set; } = "";
    public DateTime UsedAt { get; set; } = DateTime.UtcNow;
}

public class Evaluation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RoomId { get; set; }
    public string InterviewerId { get; set; } = "";
    public string Comment { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<EvaluationScore> Scores { get; set; } = [];
}

public class EvaluationScore
{
    public Guid EvaluationId { get; set; }
    public string CriterionKey { get; set; } = "";
    public int Score { get; set; }
}

public class Submission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RoomId { get; set; }
    public Guid TaskId { get; set; }
    public string CandidateId { get; set; } = "";
    public int DocumentVersion { get; set; }
    public string Language { get; set; } = "";
    public string Code { get; set; } = "";
    public Guid? VerifiedRunId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CodeRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RoomId { get; set; }
    public Guid TaskId { get; set; }
    public string UserId { get; set; } = "";
    public int DocumentVersion { get; set; }
    public string Language { get; set; } = "";
    public string Code { get; set; } = "";
    public string Status { get; set; } = "queued";
    public bool AllPassed { get; set; }
    public string Output { get; set; } = "";
    public string Summary { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public List<CodeRunTest> Tests { get; set; } = [];
}

public class CodeRunTest
{
    public Guid RunId { get; set; }
    public int Position { get; set; }
    public bool Passed { get; set; }
    public int DurationMs { get; set; }
    public int MemoryKb { get; set; }
    public string? ErrorKind { get; set; }
}

public class DailyRunUsage
{
    public string UserId { get; set; } = "";
    public DateOnly UtcDate { get; set; }
    public int ReservedRuns { get; set; }
}

public class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? ActorId { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string? EntityId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class SessionEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RoomId { get; set; }
    public int Sequence { get; set; }
    public Guid? TaskId { get; set; }
    public string? ActorId { get; set; }
    public string Author { get; set; } = "Система";
    public string Type { get; set; } = "";
    public string Visibility { get; set; } = "participants";
    public string Payload { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
