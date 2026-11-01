using System.Net;
using System.Text.Json;
using Interview.Api.Contracts;
using Interview.Api.Data;
using Interview.Api.Services;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Interview.Api.Tests;

public class ApiTests
{
    private static async Task<(Guid RoomId, Guid TaskId, Guid RubricId, string Token)> Prepare(ApiFactory factory)
    {
        using var lead = factory.Session(); await lead.Login("teamlead");
        var rubric = await lead.Post("/api/rubrics", new { name = "Рубрика", criteria = new[] { new { id = "algorithm", label = "Алгоритм", maxScore = 5 } } });
        using var interviewer = factory.Session(); await interviewer.Login("interviewer");
        var task = await interviewer.Post("/api/tasks", new
        {
            title = "Сумма",
            description = "Сложите числа",
            topic = "Алгоритмы",
            difficulty = "easy",
            company = "",
            tags = new[] { "числа" },
            language = "Python",
            starterCode = "print(5)",
            timeLimitMs = 2000,
            memoryLimitMb = 128,
            tests = new[] { new { input = "2 3", expected = "5", hidden = false }, new { input = "PRIVATE_INPUT", expected = "PRIVATE_EXPECTED", hidden = true } }
        });
        var room = await interviewer.Post("/api/rooms", new { title = "Интервью", candidateName = "Кандидат", taskIds = new[] { task.GetProperty("id").GetGuid() }, rubricId = rubric.GetProperty("id").GetGuid() });
        var roomId = room.GetProperty("id").GetGuid();
        await interviewer.Post($"/api/rooms/{roomId}/start");
        var invite = await interviewer.Post($"/api/rooms/{roomId}/invitations");
        return (roomId, task.GetProperty("id").GetGuid(), rubric.GetProperty("id").GetGuid(), invite.GetProperty("token").GetString()!);
    }

    private static HubConnection Connect(ApiFactory factory, TestSession session) => new HubConnectionBuilder().WithUrl("http://localhost/hubs/interviews", options =>
    {
        options.Transports = HttpTransportType.LongPolling;
        options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
        options.Headers["Cookie"] = session.CookieHeader;
        options.Headers["Origin"] = "http://localhost:5173";
    }).Build();

    [Fact]
    public async Task Registration_Csrf_Roles_And_Login()
    {
        await using var factory = new ApiFactory(); await factory.Initialize();
        using var candidate = factory.Session();
        var unprotected = await candidate.Send(HttpMethod.Post, "/api/auth/register/candidate", new { name = "Кандидат", email = "x@example.test", password = "TestPassword123!" }, false);
        Assert.Equal(HttpStatusCode.BadRequest, unprotected.StatusCode);
        var registered = await candidate.Register(); Assert.Equal("candidate", registered.GetProperty("user").GetProperty("role").GetString());
        Assert.Empty((await candidate.Get("/api/rooms")).EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await candidate.Send(HttpMethod.Get, "/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await candidate.Send(HttpMethod.Get, "/api/tasks")).StatusCode);
        await candidate.Send(HttpMethod.Post, "/api/auth/logout");
        Assert.Equal(HttpStatusCode.Unauthorized, (await candidate.Send(HttpMethod.Get, "/api/auth/me")).StatusCode);
        await candidate.Post("/api/auth/login", new { email = "candidate@example.test", password = "TestPassword123!" });
        Assert.Equal("candidate", (await candidate.Get("/api/auth/me")).GetProperty("user").GetProperty("role").GetString());
    }

    [Fact]
    public async Task Invitation_Is_OneUse_And_Does_Not_Leak_Hidden_Tests()
    {
        await using var factory = new ApiFactory(); await factory.Initialize();
        var setup = await Prepare(factory);
        using var candidate = factory.Session(); await candidate.Register();
        await candidate.Post("/api/invitations/redeem", new { token = setup.Token, name = "Кандидат" });
        var room = await candidate.Get($"/api/rooms/{setup.RoomId}");
        Assert.Single(room.GetProperty("tasks")[0].GetProperty("tests").EnumerateArray());
        Assert.DoesNotContain("PRIVATE_INPUT", room.GetRawText());
        Assert.DoesNotContain("PRIVATE_EXPECTED", room.GetRawText());
        Assert.Equal(HttpStatusCode.Conflict, (await candidate.Send(HttpMethod.Post, "/api/invitations/redeem", new { token = setup.Token, name = "Повтор" })).StatusCode);
        using var stranger = factory.Session(); await stranger.Register("other@example.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.Send(HttpMethod.Get, $"/api/rooms/{setup.RoomId}")).StatusCode);
    }

    [Fact]
    public async Task SignalR_Broadcasts_Saved_Code_Rejects_Stale_And_Second_Writer()
    {
        await using var factory = new ApiFactory(); await factory.Initialize(); var setup = await Prepare(factory);
        using var candidate = factory.Session(); await candidate.Register(); await candidate.Post("/api/invitations/redeem", new { token = setup.Token, name = "Кандидат" });
        using var interviewer = factory.Session(); await interviewer.Login("interviewer");
        await using var writer = Connect(factory, candidate); await writer.StartAsync(); await writer.InvokeAsync("JoinInterview", setup.RoomId);
        await using var viewer = Connect(factory, interviewer); await viewer.StartAsync(); await viewer.InvokeAsync("JoinInterview", setup.RoomId);
        var received = new TaskCompletionSource<DocumentDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        viewer.On<DocumentDto>("CodeUpdated", document => received.TrySetResult(document));
        var update = new DocumentUpdate(setup.TaskId, "Python", "print(7)", 0, Guid.NewGuid());
        var saved = await writer.InvokeAsync<DocumentDto>("UpdateCode", setup.RoomId, update);
        var broadcast = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, saved.Version); Assert.Equal("print(7)", broadcast.Code);
        Assert.Equal(saved, await writer.InvokeAsync<DocumentDto>("UpdateCode", setup.RoomId, update));
        await Assert.ThrowsAsync<HubException>(() => writer.InvokeAsync<DocumentDto>("UpdateCode", setup.RoomId, update with { MutationId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<HubException>(() => viewer.InvokeAsync<DocumentDto>("UpdateCode", setup.RoomId, update));
        await using var second = Connect(factory, candidate); await second.StartAsync();
        await Assert.ThrowsAsync<HubException>(() => second.InvokeAsync("JoinInterview", setup.RoomId));
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.CodeEvents.CountAsync());
        Assert.Equal("print(7)", (await db.Documents.SingleAsync()).Code);
    }

    [Fact]
    public async Task Hints_Evaluation_Submission_And_Reports_Are_Persisted()
    {
        await using var factory = new ApiFactory(); await factory.Initialize(); var setup = await Prepare(factory);
        using var candidate = factory.Session(); await candidate.Register(); await candidate.Post("/api/invitations/redeem", new { token = setup.Token, name = "Кандидат" });
        using var interviewer = factory.Session(); await interviewer.Login("interviewer");
        var room = await interviewer.Post($"/api/rooms/{setup.RoomId}/hints", new { taskId = setup.TaskId, text = "Разберите вход" });
        var hintId = room.GetProperty("hints")[0].GetProperty("id").GetGuid();
        await candidate.Post($"/api/rooms/{setup.RoomId}/hint-usage", new { hintId });
        await candidate.Post($"/api/rooms/{setup.RoomId}/hint-usage", new { hintId });
        await interviewer.Post($"/api/rooms/{setup.RoomId}/evaluations", new { rubricId = setup.RubricId, scores = new { algorithm = 4 }, comment = "Хорошо" });
        await interviewer.Post($"/api/rooms/{setup.RoomId}/evaluations", new { rubricId = setup.RubricId, scores = new { algorithm = 4 }, comment = "Уточнённая оценка" });
        Assert.Empty((await candidate.Get($"/api/rooms/{setup.RoomId}")).GetProperty("evaluations").EnumerateArray());
        await candidate.Post($"/api/rooms/{setup.RoomId}/submissions", new { taskId = setup.TaskId, documentVersion = 0 });
        await candidate.Post($"/api/rooms/{setup.RoomId}/submissions", new { taskId = setup.TaskId, documentVersion = 0 });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await candidate.Send(HttpMethod.Post, $"/api/rooms/{setup.RoomId}/runs", new { taskId = setup.TaskId, documentVersion = 0 })).StatusCode);
        using var lead = factory.Session(); await lead.Login("teamlead");
        var report = await lead.Get("/api/reports"); var row = report.GetProperty("candidates")[0];
        Assert.Equal(1, row.GetProperty("submitted").GetInt32()); Assert.Equal(1, row.GetProperty("hintsUsed").GetInt32()); Assert.Equal(4, row.GetProperty("averageScore").GetDouble());
    }

    [Fact]
    public async Task Admin_Creates_Staff_Setup_Link_And_Revokes_Disabled_Account()
    {
        await using var factory = new ApiFactory(); await factory.Initialize();
        using var admin = factory.Session(); await admin.Login("admin");
        var result = await admin.Post("/api/admin/users", new { name = "Новый интервьюер", email = "staff@example.test", role = "interviewer" });
        var token = result.GetProperty("setupToken").GetString();
        using var staff = factory.Session();
        await staff.Post("/api/auth/setup-password", new { token, password = "AnotherTestPassword!" });
        var id = result.GetProperty("user").GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.Conflict, (await staff.Send(HttpMethod.Post, "/api/auth/setup-password", new { token, password = "AnotherTestPassword!" })).StatusCode);
        var changed = await admin.Send(HttpMethod.Patch, $"/api/admin/users/{id}", new { active = false, runQuota = 0 }); Assert.True(changed.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await staff.Send(HttpMethod.Get, "/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Daily_Quota_Reserves_Only_Allowed_Number_Of_Runs()
    {
        await using var factory = new ApiFactory(runnerEnabled: true); await factory.Initialize(); var setup = await Prepare(factory);
        using var candidate = factory.Session(); var registered = await candidate.Register(); await candidate.Post("/api/invitations/redeem", new { token = setup.Token, name = "Кандидат" });
        using var admin = factory.Session(); await admin.Login("admin");
        var id = registered.GetProperty("user").GetProperty("id").GetString();
        Assert.True((await admin.Send(HttpMethod.Patch, $"/api/admin/users/{id}", new { runQuota = 1 })).IsSuccessStatusCode);
        await candidate.Post($"/api/rooms/{setup.RoomId}/runs", new { taskId = setup.TaskId, documentVersion = 0 });
        Assert.Equal(HttpStatusCode.TooManyRequests, (await candidate.Send(HttpMethod.Post, $"/api/rooms/{setup.RoomId}/runs", new { taskId = setup.TaskId, documentVersion = 0 })).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, (await db.DailyRunUsages.SingleAsync()).ReservedRuns); Assert.Equal(1, await db.Runs.CountAsync());
    }

    [Fact]
    public async Task Worker_Compares_Outputs_Without_Leaking_Hidden_Input_Or_Output()
    {
        await using var factory = new ApiFactory(runnerEnabled: true); await factory.Initialize(); var setup = await Prepare(factory);
        using var candidate = factory.Session(); await candidate.Register(); await candidate.Post("/api/invitations/redeem", new { token = setup.Token, name = "Кандидат" });
        var queued = await candidate.Post($"/api/rooms/{setup.RoomId}/runs", new { taskId = setup.TaskId, documentVersion = 0 });
        using var worker = ActivatorUtilities.CreateInstance<RunWorker>(factory.Services);
        await worker.StartAsync(CancellationToken.None);
        JsonElement result = queued;
        try
        {
            for (var i = 0; i < 50; i++)
            {
                await Task.Delay(100);
                result = await candidate.Get($"/api/rooms/{setup.RoomId}/runs/{queued.GetProperty("id").GetGuid()}");
                if (result.GetProperty("status").GetString() == "completed") break;
            }
            Assert.Equal("completed", result.GetProperty("status").GetString());
            Assert.All(result.GetProperty("tests").EnumerateArray(), item => Assert.True(item.GetProperty("passed").GetBoolean()));
            Assert.DoesNotContain("PRIVATE_", result.GetRawText());
            await candidate.Post($"/api/rooms/{setup.RoomId}/submissions", new { taskId = setup.TaskId, documentVersion = 0 });
            using var lead = factory.Session(); await lead.Login("teamlead");
            Assert.Equal(1, (await lead.Get("/api/reports")).GetProperty("tasks")[0].GetProperty("solved").GetInt32());
        }
        finally { await worker.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task Bank_Updates_Do_Not_Change_Room_Snapshot_And_Invalid_Scores_Are_Rejected()
    {
        await using var factory = new ApiFactory(); await factory.Initialize(); var setup = await Prepare(factory);
        using var interviewer = factory.Session(); await interviewer.Login("interviewer");
        var task = (await interviewer.Get("/api/tasks"))[0];
        var changed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(task.GetRawText())!;
        changed["description"] = JsonSerializer.SerializeToElement("Новое условие в банке");
        var updateResponse = await interviewer.Send(HttpMethod.Put, $"/api/tasks/{setup.TaskId}", changed);
        Assert.True(updateResponse.IsSuccessStatusCode, await updateResponse.Content.ReadAsStringAsync());
        Assert.Equal("Сложите числа", (await interviewer.Get($"/api/rooms/{setup.RoomId}")).GetProperty("tasks")[0].GetProperty("description").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await interviewer.Send(HttpMethod.Post, $"/api/rooms/{setup.RoomId}/evaluations", new { rubricId = setup.RubricId, scores = new { algorithm = 100 }, comment = "" })).StatusCode);
        using var session = factory.Session();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/hubs/interviews/negotiate?negotiateVersion=1");
        // Проверка Origin выполняется до выдачи connection token.
        using var raw = factory.CreateClient();
        request.Headers.Add("Origin", "https://untrusted.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await raw.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Active_TeamLeads_Are_Limited_To_Ten()
    {
        await using var factory = new ApiFactory(); await factory.Initialize();
        using var admin = factory.Session(); await admin.Login("admin");
        for (var i = 0; i < 9; i++) await admin.Post("/api/admin/users", new { name = "Тимлид " + i, email = $"lead-{i}@example.test", role = "teamlead" });
        var rejected = await admin.Send(HttpMethod.Post, "/api/admin/users", new { name = "Лишний", email = "extra@example.test", role = "teamlead" });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
    }
}
