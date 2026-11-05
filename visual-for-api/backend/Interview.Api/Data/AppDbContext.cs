using System.Text.Json;
using Interview.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Interview.Api.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Interview.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options, FieldCipher cipher) : IdentityDbContext<AppUser, IdentityRole, string>(options)
{
    public DbSet<BankTask> Tasks => Set<BankTask>();
    public DbSet<TaskTest> TaskTests => Set<TaskTest>();
    public DbSet<Rubric> Rubrics => Set<Rubric>();
    public DbSet<RubricCriterion> RubricCriteria => Set<RubricCriterion>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<RoomTask> RoomTasks => Set<RoomTask>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<PasswordSetup> PasswordSetups => Set<PasswordSetup>();
    public DbSet<CodeDocument> Documents => Set<CodeDocument>();
    public DbSet<CodeEvent> CodeEvents => Set<CodeEvent>();
    public DbSet<ChatMessage> Messages => Set<ChatMessage>();
    public DbSet<Hint> Hints => Set<Hint>();
    public DbSet<HintUsage> HintUsages => Set<HintUsage>();
    public DbSet<Evaluation> Evaluations => Set<Evaluation>();
    public DbSet<EvaluationScore> EvaluationScores => Set<EvaluationScore>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<CodeRun> Runs => Set<CodeRun>();
    public DbSet<CodeRunTest> RunTests => Set<CodeRunTest>();
    public DbSet<DailyRunUsage> DailyRunUsages => Set<DailyRunUsage>();
    public DbSet<AuditEvent> Audit => Set<AuditEvent>();
    public DbSet<SessionEvent> SessionEvents => Set<SessionEvent>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.Entity<AppUser>().Property(x => x.DisplayName).HasMaxLength(100);
        model.Entity<AppUser>().Property(x => x.Role).HasMaxLength(20);
        model.Entity<AppUser>().HasIndex(x => x.NormalizedEmail).IsUnique();
        model.Entity<AppUser>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_Users_Quota", "\"RunQuota\" >= 0 AND \"RunQuota\" <= 10000");
            t.HasCheckConstraint("CK_Users_Role", "\"Role\" IN ('candidate','interviewer','teamlead','admin')");
        });
        model.Entity<BankTask>().ToTable("Tasks", t =>
        {
            t.HasCheckConstraint("CK_Tasks_Limits", "\"TimeLimitMs\" BETWEEN 100 AND 10000 AND \"MemoryLimitMb\" BETWEEN 32 AND 1024");
            t.HasCheckConstraint("CK_Tasks_Difficulty", "\"Difficulty\" IN ('easy','medium','hard')");
        });
        model.Entity<BankTask>().Property(x => x.Title).HasMaxLength(200);
        model.Entity<BankTask>().Property(x => x.Description).HasMaxLength(10000);
        model.Entity<BankTask>().Property(x => x.Topic).HasMaxLength(100);
        model.Entity<BankTask>().Property(x => x.Company).HasMaxLength(200);
        model.Entity<BankTask>().Property(x => x.Difficulty).HasMaxLength(10);
        model.Entity<BankTask>().Property(x => x.StarterCode).HasMaxLength(20000);
        model.Entity<BankTask>().Property(x => x.Language).HasMaxLength(30);
        model.Entity<BankTask>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<BankTask>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.AuthorId);
        model.Entity<BankTask>().HasMany(x => x.Tests).WithOne().HasForeignKey(x => x.TaskId);
        model.Entity<TaskTest>().HasIndex(x => new { x.TaskId, x.Position }).IsUnique();
        model.Entity<TaskTest>().Property(x => x.Input).HasMaxLength(10000);
        model.Entity<TaskTest>().Property(x => x.Expected).HasMaxLength(10000);

        // SQLite используется только интеграционными тестами; основная схема — PostgreSQL.
        if (Database.ProviderName?.Contains("Sqlite") == true)
        {
            var tags = model.Entity<BankTask>().Property(x => x.Tags).HasConversion(
                x => JsonSerializer.Serialize(x, (JsonSerializerOptions?)null),
                x => JsonSerializer.Deserialize<string[]>(x, (JsonSerializerOptions?)null)!);
            tags.Metadata.SetValueComparer(new ValueComparer<string[]>(
                (a, b) => a!.SequenceEqual(b!), x => x.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)), x => x.ToArray()));
        }
        model.Entity<Rubric>().Property(x => x.Name).HasMaxLength(200);
        model.Entity<Rubric>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.OwnerId);
        model.Entity<Rubric>().HasMany(x => x.Criteria).WithOne().HasForeignKey(x => x.RubricId);
        model.Entity<RubricCriterion>().Property(x => x.Key).HasMaxLength(50);
        model.Entity<RubricCriterion>().Property(x => x.Label).HasMaxLength(100);
        model.Entity<RubricCriterion>().HasIndex(x => new { x.RubricId, x.Key }).IsUnique();
        model.Entity<RubricCriterion>().ToTable(t => t.HasCheckConstraint("CK_Rubric_MaxScore", "\"MaxScore\" BETWEEN 1 AND 100"));
        model.Entity<Room>().ToTable("Interviews", t =>
        {
            t.HasCheckConstraint("CK_Room_Status", "\"Status\" IN ('draft','waiting','active','completed')");
            t.HasCheckConstraint("CK_Room_Duration", "\"DurationMinutes\" BETWEEN 5 AND 240");
        });
        model.Entity<Room>().Property(x => x.Title).HasMaxLength(200);
        model.Entity<Room>().Property(x => x.CandidateName).HasMaxLength(100);
        model.Entity<Room>().Property(x => x.Status).HasMaxLength(20);
        model.Entity<Room>().Property(x => x.RubricSnapshot).HasColumnType("jsonb");
        model.Entity<Room>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.InterviewerId);
        model.Entity<Room>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.CandidateId);
        model.Entity<Room>().HasOne<Rubric>().WithMany().HasForeignKey(x => x.RubricId);
        model.Entity<Room>().HasIndex(x => new { x.InterviewerId, x.CreatedAt });
        model.Entity<Room>().HasIndex(x => x.CandidateId);
        model.Entity<RoomTask>().ToTable("InterviewTasks");
        model.Entity<RoomTask>().HasKey(x => new { x.RoomId, x.TaskId });
        model.Entity<RoomTask>().Property(x => x.TaskSnapshot).HasColumnType("jsonb");
        model.Entity<RoomTask>().HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId);
        model.Entity<RoomTask>().HasOne<BankTask>().WithMany().HasForeignKey(x => x.TaskId);
        model.Entity<RoomTask>().HasIndex(x => new { x.RoomId, x.Position }).IsUnique();
        model.Entity<Invitation>().Property(x => x.TokenHash).HasMaxLength(64);
        model.Entity<Invitation>().HasIndex(x => x.TokenHash).IsUnique();
        model.Entity<Invitation>().HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId);
        model.Entity<Invitation>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedById);
        model.Entity<PasswordSetup>().Property(x => x.TokenHash).HasMaxLength(64);
        model.Entity<PasswordSetup>().HasIndex(x => x.TokenHash).IsUnique();
        model.Entity<PasswordSetup>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId);

        model.Entity<CodeDocument>().ToTable("CodeDocuments", t => t.HasCheckConstraint("CK_Document_Version", "\"Version\" >= 0"));
        model.Entity<CodeDocument>().HasKey(x => new { x.RoomId, x.TaskId });
        model.Entity<CodeDocument>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<CodeDocument>().Property(x => x.Code).HasMaxLength(20000);
        model.Entity<CodeDocument>().HasOne<RoomTask>().WithMany().HasForeignKey(x => new { x.RoomId, x.TaskId });
        model.Entity<CodeEvent>().HasOne<RoomTask>().WithMany().HasForeignKey(x => new { x.RoomId, x.TaskId });
        model.Entity<CodeEvent>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.AuthorId);
        model.Entity<CodeEvent>().Property(x => x.Code).HasMaxLength(20000);
        model.Entity<CodeEvent>().HasIndex(x => new { x.RoomId, x.TaskId, x.Version }).IsUnique();
        model.Entity<CodeEvent>().HasIndex(x => new { x.RoomId, x.MutationId }).IsUnique();
        model.Entity<ChatMessage>().ToTable("ChatMessages");
        model.Entity<ChatMessage>().Property(x => x.Text).HasMaxLength(2000);
        model.Entity<ChatMessage>().HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId);
        model.Entity<ChatMessage>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.AuthorId);
        model.Entity<ChatMessage>().HasIndex(x => new { x.RoomId, x.CreatedAt });
        model.Entity<Hint>().Property(x => x.Text).HasMaxLength(4000);
        model.Entity<Hint>().HasOne<RoomTask>().WithMany().HasForeignKey(x => new { x.RoomId, x.TaskId });
        model.Entity<Hint>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.AuthorId);
        model.Entity<HintUsage>().HasKey(x => new { x.HintId, x.CandidateId });
        model.Entity<HintUsage>().HasOne<Hint>().WithMany().HasForeignKey(x => x.HintId);
        model.Entity<HintUsage>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.CandidateId);
        model.Entity<HintUsage>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.MarkedById);
        model.Entity<Evaluation>().Property(x => x.Comment).HasMaxLength(5000);
        model.Entity<Evaluation>().HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId);
        model.Entity<Evaluation>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.InterviewerId);
        model.Entity<Evaluation>().HasIndex(x => new { x.RoomId, x.InterviewerId }).IsUnique();
        model.Entity<Evaluation>().HasMany(x => x.Scores).WithOne().HasForeignKey(x => x.EvaluationId);
        model.Entity<EvaluationScore>().HasKey(x => new { x.EvaluationId, x.CriterionKey });
        model.Entity<EvaluationScore>().Property(x => x.CriterionKey).HasMaxLength(50);
        model.Entity<EvaluationScore>().ToTable(t => t.HasCheckConstraint("CK_Evaluation_Score", "\"Score\" BETWEEN 0 AND 100"));
        model.Entity<Submission>().HasOne<RoomTask>().WithMany().HasForeignKey(x => new { x.RoomId, x.TaskId });
        model.Entity<Submission>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.CandidateId);
        model.Entity<Submission>().HasOne<CodeRun>().WithMany().HasForeignKey(x => x.VerifiedRunId);
        model.Entity<Submission>().HasIndex(x => new { x.RoomId, x.TaskId, x.CandidateId, x.DocumentVersion }).IsUnique();
        model.Entity<CodeRun>().ToTable("CodeRuns", t => t.HasCheckConstraint("CK_Run_Status", "\"Status\" IN ('queued','running','completed','failed')"));
        model.Entity<CodeRun>().HasOne<RoomTask>().WithMany().HasForeignKey(x => new { x.RoomId, x.TaskId });
        model.Entity<CodeRun>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId);
        model.Entity<CodeRun>().HasIndex(x => new { x.UserId, x.CreatedAt });
        model.Entity<CodeRun>().HasIndex(x => new { x.Status, x.CreatedAt });
        model.Entity<CodeRun>().HasMany(x => x.Tests).WithOne().HasForeignKey(x => x.RunId);
        model.Entity<CodeRunTest>().ToTable("CodeRunTests");
        model.Entity<CodeRunTest>().HasKey(x => new { x.RunId, x.Position });
        model.Entity<DailyRunUsage>().HasKey(x => new { x.UserId, x.UtcDate });
        model.Entity<DailyRunUsage>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId);
        model.Entity<DailyRunUsage>().ToTable(t => t.HasCheckConstraint("CK_RunUsage_Count", "\"ReservedRuns\" >= 0"));
        model.Entity<AuditEvent>().ToTable("AuditEvents");
        model.Entity<AuditEvent>().Property(x => x.Action).HasMaxLength(200);
        model.Entity<AuditEvent>().Property(x => x.EntityId).HasMaxLength(100);
        model.Entity<AuditEvent>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.ActorId);
        model.Entity<AuditEvent>().HasIndex(x => x.CreatedAt);

        model.Entity<RubricCriterion>().Property(x => x.Weight).HasPrecision(8, 2).HasDefaultValue(1m);
        if (Database.ProviderName?.Contains("Sqlite") == true) model.Entity<RubricCriterion>().Property(x => x.Weight).HasConversion<double>().HasColumnType("REAL");
        model.Entity<RubricCriterion>().ToTable(t => t.HasCheckConstraint("CK_Criterion_Weight", "\"Weight\" > 0 AND \"Weight\" <= 100"));
        model.Entity<Room>().Property(x => x.DurationMinutes).HasDefaultValue(90);
        model.Entity<SessionEvent>().HasIndex(x => new { x.RoomId, x.Sequence }).IsUnique();
        model.Entity<SessionEvent>().HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId);
        model.Entity<SessionEvent>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.ActorId);
        model.Entity<SessionEvent>().Property(x => x.Type).HasMaxLength(50);
        model.Entity<SessionEvent>().Property(x => x.Visibility).HasMaxLength(20);

        // Шифруются содержимое кода, скрытые тесты, переписка, заметки и журнал.
        // Индексируемые email/роли и числовые версии остаются обычными полями.
        var protectedText = new ValueConverter<string, string>(value => cipher.Encrypt(value), value => cipher.Decrypt(value));
        var protectedFields = new Dictionary<Type, string[]>
        {
            [typeof(BankTask)] = [nameof(BankTask.StarterCode)],
            [typeof(TaskTest)] = [nameof(TaskTest.Input), nameof(TaskTest.Expected)],
            [typeof(Room)] = [nameof(Room.RubricSnapshot)],
            [typeof(RoomTask)] = [nameof(RoomTask.TaskSnapshot)],
            [typeof(CodeDocument)] = [nameof(CodeDocument.Code)],
            [typeof(CodeEvent)] = [nameof(CodeEvent.Code)],
            [typeof(Submission)] = [nameof(Submission.Code)],
            [typeof(CodeRun)] = [nameof(CodeRun.Code), nameof(CodeRun.Output)],
            [typeof(ChatMessage)] = [nameof(ChatMessage.Text)],
            [typeof(Hint)] = [nameof(Hint.Text)],
            [typeof(Evaluation)] = [nameof(Evaluation.Comment)],
            [typeof(SessionEvent)] = [nameof(SessionEvent.Payload)]
        };
        foreach (var (entityType, fields) in protectedFields)
            foreach (var field in fields)
            {
                var property = model.Entity(entityType).Property(field).HasConversion(protectedText).HasColumnType("text");
                property.Metadata.SetMaxLength(null);
            }

        // Не удаляем историю вместе с пользователем: учётную запись деактивируют.
        foreach (var foreignKey in model.Model.GetEntityTypes().SelectMany(x => x.GetForeignKeys()))
            foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
    }
}
