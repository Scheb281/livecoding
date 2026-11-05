using System.ComponentModel.DataAnnotations;

namespace Interview.Api.Contracts;

public record RegisterRequest([Required, StringLength(100)] string Name, [Required, EmailAddress, StringLength(254)] string Email, [Required, StringLength(128, MinimumLength = 8)] string Password);
public record LoginRequest([Required, EmailAddress] string Email, [Required, StringLength(128)] string Password);
public record RedeemRequest([Required, StringLength(200)] string Token, [Required, StringLength(100)] string Name);
public record SetupRequest([Required, StringLength(200)] string Token, [Required, StringLength(128, MinimumLength = 8)] string Password);
public record UserDto(string Id, string Name, string Email, string Role, bool Active, int RunQuota, Guid? RoomId = null);
public record TestDto(Guid? Id, string Input, string Expected, bool Hidden);
public record TaskDto(Guid Id, string Title, string Description, string Topic, string Difficulty, string Company, string[] Tags, string Language, string StarterCode, int TimeLimitMs, int MemoryLimitMb, TestDto[] Tests);
public record TaskRequest([Required, StringLength(200)] string Title, [Required, StringLength(10000)] string Description, [Required, StringLength(100)] string Topic, [Required] string Difficulty, [StringLength(200)] string Company, [Required, MaxLength(20)] string[] Tags, [Required] string Language, [Required, StringLength(20000)] string StarterCode, [Range(100, 10000)] int TimeLimitMs, [Range(32, 1024)] int MemoryLimitMb, [Required, MinLength(1), MaxLength(50)] TestDto[] Tests);
public record CriterionDto([Required, StringLength(50)] string Id, [Required, StringLength(100)] string Label, [Range(1, 100)] int MaxScore, decimal Weight = 1m);
public record RubricDto(Guid Id, string Name, CriterionDto[] Criteria);
public record RubricRequest([Required, StringLength(200)] string Name, [Required, MinLength(1), MaxLength(10)] CriterionDto[] Criteria);
public record RoomRequest([Required, StringLength(200)] string Title, [Required, StringLength(100)] string CandidateName, [Required, MinLength(1), MaxLength(10)] Guid[] TaskIds, Guid RubricId, [Range(5, 240)] int DurationMinutes = 90);
public record DocumentDto(Guid TaskId, string Language, string Code, int Version);
public record DocumentUpdate(Guid TaskId, string Language, string Code, int BaseVersion, Guid MutationId);
public record MessageRequest([Required, StringLength(2000)] string Text);
public record HintRequest(Guid TaskId, [Required, StringLength(4000)] string Text);
public record HintUsageRequest(Guid HintId);
public record EvaluationRequest(Guid RubricId, [Required] Dictionary<string, int> Scores, [StringLength(5000)] string Comment);
public record VersionRequest(Guid TaskId, int DocumentVersion);
public record CreateUserRequest([Required, StringLength(100)] string Name, [Required, EmailAddress, StringLength(254)] string Email, [Required] string Role);
public record UpdateUserRequest(bool? Active, [Range(0, 10000)] int? RunQuota);

public record RunnerRequest(Guid RunId, string Language, string Code, int TimeLimitMs, int MemoryLimitMb, RunnerTest[] Tests);
public record RunnerTest(int Position, string Input);
public record RunnerResult(RunnerTestResult[] Tests);
public record RunnerTestResult(int Position, string Output, int ExitCode, bool TimedOut, int DurationMs, int MemoryKb, string? ErrorKind = null);
public record CursorUpdate(Guid TaskId, int LineNumber, int Column);
public record EditorControl(string? ConnectionId, string? UserId, string? Name);
