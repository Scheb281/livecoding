using System.Net;
using Interview.Api.Contracts;
using Interview.Api.Data;
using Interview.Api.Infrastructure;
using Interview.Api.Services;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Interview.Api.Tests;

public class CompletionTests
{
    [Fact]
    public async Task Cipher_Authenticates_Data_And_Legacy_Rows_Are_Encrypted_By_Backfill()
    {
        await using var factory = new ApiFactory(); await factory.Initialize();
        using var scope = factory.Services.CreateScope();
        var cipher = scope.ServiceProvider.GetRequiredService<FieldCipher>();
        var encrypted = cipher.Encrypt("private text");
        var tampered = encrypted.ToCharArray(); tampered[30] = tampered[30] == 'A' ? 'B' : 'A';
        Assert.Throws<System.Security.Cryptography.CryptographicException>(() => cipher.Decrypt(new string(tampered)));
        Assert.Equal("legacy text", cipher.Decrypt("legacy text"));
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await db.Users.Select(u => u.Id).FirstAsync();
        var task = new Interview.Api.Models.BankTask { AuthorId = userId, Title = "Тест", Description = "Тест", Topic = "Тест", StarterCode = "print(5)" };
        db.Tasks.Add(task); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "UPDATE \"Tasks\" SET \"StarterCode\" = 'legacy text'"; await command.ExecuteNonQueryAsync();
        await EncryptionBackfill.Run(db);
        command.CommandText = "SELECT \"StarterCode\" FROM \"Tasks\" LIMIT 1";
        Assert.StartsWith("protected:v1:", (string)(await command.ExecuteScalarAsync())!);
        Assert.Equal("legacy text", (await db.Tasks.SingleAsync()).StarterCode);
    }

    private static HubConnection Connect(ApiFactory factory, TestSession session) => new HubConnectionBuilder().WithUrl("http://localhost/hubs/interviews", options =>
    {
        options.Transports = HttpTransportType.LongPolling; options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
        options.Headers["Cookie"] = session.CookieHeader; options.Headers["Origin"] = "http://localhost:5173";
    }).Build();

    [Fact]
    public void Weights_Are_Normalized_And_Invalid_Criteria_Are_Not_Scored()
    {
        var rubric = new RubricDto(Guid.NewGuid(), "Оценка", [new("algorithm", "Алгоритм", 5, 3), new("code", "Код", 10, 1)]);
        Assert.Equal(75m, ScoringService.Calculate(rubric, new Dictionary<string, int> { ["algorithm"] = 5, ["code"] = 0 }));
        Assert.Null(ScoringService.Calculate(rubric, new Dictionary<string, int>()));
    }

    [Fact]
    public async Task Encryption_Control_Cursors_Recording_And_Expiry_Work_Together()
    {
        await using var factory = new ApiFactory(); await factory.Initialize();
        using var lead = factory.Session(); await lead.Login("teamlead");
        var rubric = await lead.Post("/api/rubrics", new { name = "Оценка", criteria = new[] { new { id = "algorithm", label = "Алгоритм", maxScore = 5, weight = 3 } } });
        using var interviewer = factory.Session(); await interviewer.Login("interviewer");
        var task = await interviewer.Post("/api/tasks", new { title = "Задача", description = "Условие", topic = "Тема", difficulty = "easy", company = "", tags = Array.Empty<string>(), language = "Python", starterCode = "print('initial')", timeLimitMs = 1000, memoryLimitMb = 128, tests = new[] { new { input = "", expected = "ok", hidden = false } } });
        var taskId = task.GetProperty("id").GetGuid();
        var room = await interviewer.Post("/api/rooms", new { title = "Комната", candidateName = "Кандидат", taskIds = new[] { taskId }, rubricId = rubric.GetProperty("id").GetGuid(), durationMinutes = 5 });
        var roomId = room.GetProperty("id").GetGuid();
        await interviewer.Post($"/api/rooms/{roomId}/open");
        var invite = await interviewer.Post($"/api/rooms/{roomId}/invitations");
        using var candidate = factory.Session(); await candidate.Register();
        await candidate.Post("/api/invitations/redeem", new { token = invite.GetProperty("token").GetString(), name = "Кандидат" });
        Assert.Equal("waiting", (await candidate.Get($"/api/rooms/{roomId}")).GetProperty("status").GetString());
        await interviewer.Post($"/api/rooms/{roomId}/start");
        await using var writer = Connect(factory, candidate); await writer.StartAsync(); await writer.InvokeAsync<EditorControl>("JoinInterview", roomId);
        await using var viewer = Connect(factory, interviewer); await viewer.StartAsync(); await viewer.InvokeAsync<EditorControl>("JoinInterview", roomId);
        var cursor = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        viewer.On<System.Text.Json.JsonElement>("CursorUpdated", _ => cursor.TrySetResult(true));
        await writer.InvokeAsync("UpdateCursor", roomId, new CursorUpdate(taskId, 1, 1));
        Assert.True(await cursor.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await writer.InvokeAsync<DocumentDto>("UpdateCode", roomId, new DocumentUpdate(taskId, "Python", "print('candidate')", 0, Guid.NewGuid()));
        await viewer.InvokeAsync<EditorControl>("TakeEditorControl", roomId);
        await Assert.ThrowsAsync<HubException>(() => writer.InvokeAsync<DocumentDto>("UpdateCode", roomId, new DocumentUpdate(taskId, "Python", "stale", 1, Guid.NewGuid())));
        await viewer.InvokeAsync<DocumentDto>("UpdateCode", roomId, new DocumentUpdate(taskId, "Python", "print('interviewer')", 1, Guid.NewGuid()));
        await viewer.InvokeAsync("ReleaseEditorControl", roomId);
        await writer.InvokeAsync<EditorControl>("TakeEditorControl", roomId);
        await interviewer.Post($"/api/rooms/{roomId}/evaluations", new { rubricId = rubric.GetProperty("id").GetGuid(), scores = new { algorithm = 4 }, comment = "PRIVATE_COMMENT" });
        var events = await candidate.Get($"/api/rooms/{roomId}/events");
        Assert.DoesNotContain("PRIVATE_COMMENT", events.GetRawText());
        Assert.DoesNotContain("EvaluationSaved", events.GetRawText());
        var codeSequence = events.GetProperty("items").EnumerateArray().First(e => e.GetProperty("type").GetString() == "CodeChanged").GetProperty("sequence").GetInt32();
        var replay = await candidate.Get($"/api/rooms/{roomId}/recording?atSequence={codeSequence}");
        Assert.Equal("print('candidate')", replay.GetProperty("documents")[0].GetProperty("code").GetString());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var connection = db.Database.GetDbConnection(); await connection.OpenAsync();
            await using var command = connection.CreateCommand(); command.CommandText = "SELECT \"Code\" FROM \"CodeDocuments\" LIMIT 1";
            var raw = (string)(await command.ExecuteScalarAsync())!;
            Assert.StartsWith("protected:v1:", raw); Assert.DoesNotContain("interviewer", raw);
            Assert.Equal("print('interviewer')", (await db.Documents.SingleAsync()).Code);
            var savedRoom = await db.Rooms.SingleAsync(); savedRoom.SessionExpiresAt = DateTime.UtcNow.AddSeconds(-1); await db.SaveChangesAsync();
        }
        Assert.Equal("completed", (await candidate.Get($"/api/rooms/{roomId}")).GetProperty("status").GetString());
        await Assert.ThrowsAsync<HubException>(() => writer.InvokeAsync<DocumentDto>("UpdateCode", roomId, new DocumentUpdate(taskId, "Python", "late", 2, Guid.NewGuid())));
        Assert.Equal(HttpStatusCode.Conflict, (await candidate.Send(HttpMethod.Post, $"/api/rooms/{roomId}/runs", new { taskId, documentVersion = 2 })).StatusCode);
    }
}
