using Interview.Api.Contracts;
using Interview.Api.Hubs;
using Interview.Api.Infrastructure;
using Interview.Api.Models;
using Microsoft.AspNetCore.SignalR;

namespace Interview.Api.Services;

// Оба участника пишут в общий документ по очереди, передавая управление.
// Для нескольких реплик API нужен общий lease/store вместо этого реестра.
public sealed class RoomConnections(IHubContext<InterviewHub> hub)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, (string UserId, Guid RoomId, string Name, string Role)> members = [];
    private readonly Dictionary<Guid, string> writers = [];
    private readonly HashSet<string> revoked = [];
    private readonly Dictionary<string, Queue<DateTime>> messages = [];
    public static string Group(Guid id) => "interview:" + id;
    private EditorControl State(Guid roomId) => writers.TryGetValue(roomId, out var id) && members.TryGetValue(id, out var owner)
        ? new(id, owner.UserId, owner.Name) : new(null, null, null);

    public async Task<EditorControl> Join(string connectionId, AppUser actor, Guid roomId)
    {
        await gate.WaitAsync();
        try
        {
            ApiError.Require(!revoked.Contains(connectionId), "Сессия соединения отозвана.", 401);
            if (actor.Role == "candidate") ApiError.Require(!members.Any(x => x.Key != connectionId && x.Value.UserId == actor.Id && x.Value.RoomId == roomId), "Комната уже открыта в другой вкладке кандидата.", 409);
            if (members.TryGetValue(connectionId, out var old) && old.RoomId != roomId) await LeaveCore(connectionId);
            await hub.Groups.AddToGroupAsync(connectionId, Group(roomId));
            members[connectionId] = (actor.Id, roomId, actor.DisplayName, actor.Role);
            if (actor.Role == "candidate" && !writers.ContainsKey(roomId)) writers[roomId] = connectionId;
            var state = State(roomId);
            await hub.Clients.Group(Group(roomId)).SendAsync("EditorControlChanged", state);
            return state;
        }
        finally { gate.Release(); }
    }
    public async Task<EditorControl> TakeControl(string id, AppUser actor, Guid roomId)
    {
        await gate.WaitAsync();
        try
        {
            RequireMember(id, actor.Id, roomId);
            ApiError.Require(!writers.TryGetValue(roomId, out var writer) || writer == id || actor.Role == "interviewer", "Редактор занят. Попросите интервьюера передать управление.", 409);
            writers[roomId] = id;
            var state = State(roomId);
            await hub.Clients.Group(Group(roomId)).SendAsync("EditorControlChanged", state);
            return state;
        }
        finally { gate.Release(); }
    }
    public async Task ReleaseControl(string id, AppUser actor, Guid roomId)
    {
        await gate.WaitAsync();
        try
        {
            RequireMember(id, actor.Id, roomId);
            ApiError.Require(writers.GetValueOrDefault(roomId) == id || actor.Role == "interviewer", "Нет права передавать управление.", 403);
            writers.Remove(roomId);
            await hub.Clients.Group(Group(roomId)).SendAsync("EditorControlChanged", State(roomId));
        }
        finally { gate.Release(); }
    }
    private void RequireMember(string id, string userId, Guid roomId) => ApiError.Require(!revoked.Contains(id)
        && members.TryGetValue(id, out var member) && member.UserId == userId && member.RoomId == roomId, "Нет доступа к соединению комнаты.", 403);
    public async Task CheckMember(string id, string userId, Guid roomId, bool writer)
    {
        await gate.WaitAsync();
        try
        {
            RequireMember(id, userId, roomId);
            if (writer) ApiError.Require(writers.GetValueOrDefault(roomId) == id, "Управление редактором передано другому участнику.", 409);
            if (!messages.TryGetValue(id, out var queue)) messages[id] = queue = new();
            while (queue.TryPeek(out var timestamp) && timestamp < DateTime.UtcNow.AddSeconds(-10)) queue.Dequeue();
            ApiError.Require(queue.Count < 100, "Слишком частые сообщения редактора.", 429);
            queue.Enqueue(DateTime.UtcNow);
        }
        finally { gate.Release(); }
    }
    public Task CheckWriter(string id, string userId, Guid roomId) => CheckMember(id, userId, roomId, true);
    private async Task LeaveCore(string id)
    {
        if (members.Remove(id, out var member))
        {
            await hub.Groups.RemoveFromGroupAsync(id, Group(member.RoomId));
            if (writers.GetValueOrDefault(member.RoomId) == id) writers.Remove(member.RoomId);
            await hub.Clients.Group(Group(member.RoomId)).SendAsync("PeerLeft", new { ConnectionId = id });
            await hub.Clients.Group(Group(member.RoomId)).SendAsync("EditorControlChanged", State(member.RoomId));
        }
        messages.Remove(id);
    }
    public async Task Leave(string id)
    {
        await gate.WaitAsync();
        try { await LeaveCore(id); revoked.Remove(id); } finally { gate.Release(); }
    }
    public async Task Revoke(string userId)
    {
        await gate.WaitAsync();
        try
        {
            foreach (var id in members.Where(x => x.Value.UserId == userId).Select(x => x.Key).ToArray())
            {
                revoked.Add(id); await LeaveCore(id); await hub.Clients.Client(id).SendAsync("AccessRevoked");
            }
        }
        finally { gate.Release(); }
    }
    public Task Changed(Guid roomId) => hub.Clients.Group(Group(roomId)).SendAsync("RoomChanged");
}
