using System.Security.Claims;
using Interview.Api.Data;
using Interview.Api.Infrastructure;
using Interview.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Interview.Api.Services;

public sealed class AccessService(AppDbContext db)
{
    // Читаем роль и активность из БД: браузер не может назначить себе права.
    public async Task<AppUser> Actor(ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var actor = id == null ? null : await db.Users.SingleOrDefaultAsync(x => x.Id == id);
        if (actor is not { IsActive: true }) throw new ApiError(401, "Необходимо войти в приложение.");
        return actor;
    }

    public static void Role(AppUser actor, params string[] allowed) =>
        ApiError.Require(allowed.Contains(actor.Role), "Нет доступа для этой роли.", 403);

    public async Task<Room> Room(AppUser actor, Guid roomId)
    {
        var room = await db.Rooms.SingleOrDefaultAsync(x => x.Id == roomId) ?? throw new ApiError(404, "Комната не найдена.");
        ApiError.Require((actor.Role == "candidate" && room.CandidateId == actor.Id)
            || (actor.Role == "interviewer" && room.InterviewerId == actor.Id), "Нет доступа к этому интервью.", 403);
        return room;
    }

    public static bool Expired(Room room) => room.SessionExpiresAt.HasValue && room.SessionExpiresAt <= DateTime.UtcNow;
    public static void Active(Room room) => ApiError.Require(room.Status == "active" && !Expired(room), "Интервью завершено или время сессии истекло.", 409);
    public static void Invitable(Room room) => ApiError.Require(room.Status is "waiting" or "active" && !Expired(room), "Комната не открыта или время сессии истекло.", 409);

    public async Task<Room> RecordingRoom(AppUser actor, Guid id) => actor.Role is "admin" or "teamlead"
        ? await db.Rooms.SingleOrDefaultAsync(x => x.Id == id) ?? throw new ApiError(404, "Комната не найдена.") : await Room(actor, id);

    public async Task<CodeDocument> Document(Guid roomId, Guid taskId) =>
        await db.Documents.SingleOrDefaultAsync(x => x.RoomId == roomId && x.TaskId == taskId)
        ?? throw new ApiError(404, "Задача не назначена этой комнате.");

    public void Audit(AppUser? actor, string action, object? entityId = null) => db.Audit.Add(new AuditEvent
    {
        ActorId = actor?.Id,
        Actor = actor?.DisplayName ?? "Система",
        Action = action,
        EntityId = entityId?.ToString()
    });
}
