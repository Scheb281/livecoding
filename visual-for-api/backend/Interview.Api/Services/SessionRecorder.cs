using Interview.Api.Data;
using Interview.Api.Infrastructure;
using Interview.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Interview.Api.Services;

public sealed class SessionRecorder(AppDbContext db)
{
    // Записывает событие в той же транзакции, что и изменение данных.
    public async Task<int> Append(Guid roomId, string type, AppUser? actor, object payload, Guid? taskId = null, string visibility = "participants")
    {
        var ownTransaction = db.Database.CurrentTransaction == null ? await db.Database.BeginTransactionAsync() : null;
        try
        {
            await DatabaseLocks.Acquire(db, "room:" + roomId);
            var room = await db.Rooms.SingleAsync(x => x.Id == roomId);
            var sequence = await db.Rooms.Where(x => x.Id == roomId).Select(x => x.NextEventSequence).SingleAsync();
            room.NextEventSequence = checked(sequence + 1);
            db.SessionEvents.Add(new SessionEvent
            {
                RoomId = roomId,
                Sequence = room.NextEventSequence,
                Type = type,
                ActorId = actor?.Id,
                Author = actor?.DisplayName ?? "Система",
                TaskId = taskId,
                Visibility = visibility,
                Payload = DtoService.Serialize(payload)
            });
            await db.SaveChangesAsync();
            if (ownTransaction != null) await ownTransaction.CommitAsync();
            return room.NextEventSequence;
        }
        finally { if (ownTransaction != null) await ownTransaction.DisposeAsync(); }
    }
}
