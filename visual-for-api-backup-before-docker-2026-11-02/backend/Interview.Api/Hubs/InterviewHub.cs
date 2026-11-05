using Interview.Api.Contracts;
using Interview.Api.Data;
using Interview.Api.Infrastructure;
using Interview.Api.Models;
using Interview.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Interview.Api.Hubs;

[Authorize]
public class InterviewHub(AppDbContext db, AccessService access, RoomConnections connections, SessionRecorder recorder) : Hub
{
    public async Task<EditorControl> JoinInterview(Guid roomId)
    {
        try
        {
            var actor = await access.Actor(Context.User!);
            await access.Room(actor, roomId);
            return await connections.Join(Context.ConnectionId, actor, roomId);
        }
        catch (ApiError error) { throw new HubException(error.Message); }
    }

    // Сохраняем подтверждённую версию в БД ДО рассылки. При конфликте запись отклоняется.
    public async Task<DocumentDto> UpdateCode(Guid roomId, DocumentUpdate update)
    {
        try
        {
            ApiError.Require(update != null && update.Code != null && update.Code.Length <= 20000 && DtoService.Languages.Contains(update.Language)
                && update.MutationId != Guid.Empty && update.BaseVersion >= 0, "Некорректное изменение кода.");
            var actor = await access.Actor(Context.User!); AccessService.Role(actor, "candidate", "interviewer");
            await connections.CheckWriter(Context.ConnectionId, actor.Id, roomId);
            await using var transaction = await db.Database.BeginTransactionAsync();
            await DatabaseLocks.Acquire(db, "room:" + roomId);
            var room = await access.Room(actor, roomId); AccessService.Active(room);
            var previous = await db.CodeEvents.SingleOrDefaultAsync(x => x.RoomId == roomId && x.MutationId == update!.MutationId);
            if (previous != null)
            {
                ApiError.Require(previous.AuthorId == actor.Id && previous.TaskId == update!.TaskId && previous.Code == update.Code && previous.Language == update.Language,
                    "Идентификатор изменения уже использован.", 409);
                return new(previous.TaskId, previous.Language, previous.Code, previous.Version);
            }
            var document = await access.Document(roomId, update!.TaskId);
            ApiError.Require(document.Version == update.BaseVersion && document.Version < int.MaxValue, "Версия кода изменилась. Сохраните локальный текст и откройте комнату заново.", 409);
            document.Version++; document.Language = update.Language; document.Code = update.Code; document.UpdatedAt = DateTime.UtcNow;
            var codeEvent = new CodeEvent
            {
                RoomId = roomId,
                TaskId = update.TaskId,
                AuthorId = actor.Id,
                MutationId = update.MutationId,
                Version = document.Version,
                Code = document.Code,
                Language = document.Language
            };
            db.CodeEvents.Add(codeEvent);
            codeEvent.SessionSequence = await recorder.Append(roomId, "CodeChanged", actor, new { update.TaskId, document.Version }, update.TaskId);
            await db.SaveChangesAsync(); await transaction.CommitAsync();
            var result = DtoService.Document(document);
            await Clients.OthersInGroup(RoomConnections.Group(roomId)).SendAsync("CodeUpdated", result);
            return result;
        }
        catch (ApiError error) { throw new HubException(error.Message); }
        catch (DbUpdateConcurrencyException) { throw new HubException("Версия кода изменилась. Не затирайте чужие изменения."); }
    }

    public async Task<EditorControl> TakeEditorControl(Guid roomId)
    {
        try
        {
            var actor = await access.Actor(Context.User!); AccessService.Active(await access.Room(actor, roomId));
            var control = await connections.TakeControl(Context.ConnectionId, actor, roomId);
            await recorder.Append(roomId, "EditorControlTaken", actor, new { actor.DisplayName });
            return control;
        }
        catch (ApiError error) { throw new HubException(error.Message); }
    }

    public async Task ReleaseEditorControl(Guid roomId)
    {
        try
        {
            var actor = await access.Actor(Context.User!); await access.Room(actor, roomId);
            await connections.ReleaseControl(Context.ConnectionId, actor, roomId);
            await recorder.Append(roomId, "EditorControlReleased", actor, new { actor.DisplayName });
        }
        catch (ApiError error) { throw new HubException(error.Message); }
    }

    public async Task UpdateCursor(Guid roomId, CursorUpdate update)
    {
        try
        {
            ApiError.Require(update.LineNumber is >= 1 and <= 100000 && update.Column is >= 1 and <= 20001, "Некорректная позиция курсора.");
            var actor = await access.Actor(Context.User!); AccessService.Active(await access.Room(actor, roomId));
            await connections.CheckMember(Context.ConnectionId, actor.Id, roomId, false);
            await access.Document(roomId, update.TaskId);
            await Clients.OthersInGroup(RoomConnections.Group(roomId)).SendAsync("CursorUpdated", new { Context.ConnectionId, actor.DisplayName, update.TaskId, update.LineNumber, update.Column });
        }
        catch (ApiError error) { throw new HubException(error.Message); }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await connections.Leave(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
