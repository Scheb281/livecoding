using Interview.Api.Hubs;
using Interview.Api.Infrastructure;
using Interview.Api.Models;
using Microsoft.AspNetCore.SignalR;

namespace Interview.Api.Services;

// Реестр рассчитан на ОДИН экземпляр API. Для нескольких реплик нужен общий lease/store.
public sealed class RoomConnections(IHubContext<InterviewHub> hub)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, (string UserId, Guid RoomId)> members = [];
    private readonly Dictionary<Guid, string> writers = [];
    private readonly HashSet<string> revoked = [];
    private readonly Dictionary<string, Queue<DateTime>> messages = [];
    public static string Group(Guid id) => "interview:" + id;

    public async Task Join(string connectionId, AppUser actor, Guid roomId)
    {
        await gate.WaitAsync();
        try
        {
            ApiError.Require(!revoked.Contains(connectionId), "Сессия соединения отозвана.", 401);
            if (actor.Role == "candidate")
                ApiError.Require(!writers.TryGetValue(roomId, out var writer) || writer == connectionId, "Редактор уже открыт в другой вкладке кандидата. Закройте её и повторите вход.", 409);
            if (members.TryGetValue(connectionId, out var previous) && previous.RoomId != roomId)
                await LeaveCore(connectionId);
            await hub.Groups.AddToGroupAsync(connectionId, Group(roomId));
            members[connectionId] = (actor.Id, roomId);
            if (actor.Role == "candidate") writers[roomId] = connectionId;
        }
        finally { gate.Release(); }
    }

    public async Task CheckWriter(string connectionId, string userId, Guid roomId)
    {
        await gate.WaitAsync();
        try
        {
            ApiError.Require(!revoked.Contains(connectionId) && members.TryGetValue(connectionId, out var member) && member.UserId == userId
                && member.RoomId == roomId && writers.GetValueOrDefault(roomId) == connectionId, "Нет права изменять код этой комнаты.", 403);
            if (!messages.TryGetValue(connectionId, out var queue)) messages[connectionId] = queue = new();
            while (queue.TryPeek(out var timestamp) && timestamp < DateTime.UtcNow.AddSeconds(-10)) queue.Dequeue();
            ApiError.Require(queue.Count < 100, "Слишком частые обновления кода.", 429);
            queue.Enqueue(DateTime.UtcNow);
        }
        finally { gate.Release(); }
    }

    private async Task LeaveCore(string connectionId)
    {
        if (members.Remove(connectionId, out var member))
        {
            await hub.Groups.RemoveFromGroupAsync(connectionId, Group(member.RoomId));
            if (writers.GetValueOrDefault(member.RoomId) == connectionId) writers.Remove(member.RoomId);
        }
        messages.Remove(connectionId);
    }

    public async Task Leave(string connectionId)
    {
        await gate.WaitAsync();
        try { await LeaveCore(connectionId); revoked.Remove(connectionId); }
        finally { gate.Release(); }
    }

    public async Task Revoke(string userId)
    {
        await gate.WaitAsync();
        try
        {
            foreach (var id in members.Where(x => x.Value.UserId == userId).Select(x => x.Key).ToArray())
            {
                revoked.Add(id);
                await LeaveCore(id);
                await hub.Clients.Client(id).SendAsync("AccessRevoked");
            }
        }
        finally { gate.Release(); }
    }

    public Task Changed(Guid roomId) => hub.Clients.Group(Group(roomId)).SendAsync("RoomChanged");
}
