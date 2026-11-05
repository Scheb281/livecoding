using Interview.Api.Contracts;
using Interview.Api.Data;
using Interview.Api.Infrastructure;
using Interview.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Interview.Api.Controllers;

[ApiController, Authorize, Route("api/rooms/{id:guid}")]
public class RecordingController(AppDbContext db, AccessService access) : ControllerBase
{
    [HttpGet("/api/recordings")]
    public async Task<object> List()
    {
        var actor = await access.Actor(User);
        var query = db.Rooms.AsQueryable();
        if (actor.Role is not ("admin" or "teamlead")) query = query.Where(r => r.InterviewerId == actor.Id || r.CandidateId == actor.Id);
        return await query.OrderByDescending(r => r.CreatedAt).Take(200).Select(r => new { r.Id, r.Title, r.CandidateName, r.CreatedAt, r.Status }).ToListAsync();
    }
    [HttpGet("events")]
    public async Task<object> Events(Guid id, [FromQuery] int afterSequence = 0, [FromQuery] int limit = 100)
    {
        var actor = await access.Actor(User); var room = await access.RecordingRoom(actor, id);
        limit = Math.Clamp(limit, 1, 200);
        var events = await db.SessionEvents.Where(e => e.RoomId == id && e.Sequence > afterSequence && (actor.Role != "candidate" || e.Visibility == "participants"))
            .OrderBy(e => e.Sequence).Take(limit).ToListAsync();
        return new
        {
            TotalSequence = room.NextEventSequence,
            NextAfter = events.LastOrDefault()?.Sequence ?? afterSequence,
            Items = events.Select(e => new { e.Id, e.Sequence, e.Type, e.Author, e.TaskId, e.CreatedAt, Payload = DtoService.Deserialize<System.Text.Json.JsonElement>(e.Payload) })
        };
    }
    [HttpGet("recording")]
    public async Task<object> State(Guid id, [FromQuery] int atSequence = 0)
    {
        var actor = await access.Actor(User); var room = await access.RecordingRoom(actor, id);
        ApiError.Require(atSequence >= 0 && atSequence <= room.NextEventSequence, "Позиция записи вне диапазона.");
        var assignments = await db.RoomTasks.Where(t => t.RoomId == id).OrderBy(t => t.Position).ToListAsync();
        var latest = await db.CodeEvents.Where(e => e.RoomId == id && e.SessionSequence != null && e.SessionSequence <= atSequence)
            .GroupBy(e => e.TaskId).Select(g => g.OrderByDescending(e => e.SessionSequence).First()).ToListAsync();
        return new
        {
            Sequence = atSequence,
            TotalSequence = room.NextEventSequence,
            Documents = assignments.Select(a =>
            {
                var task = DtoService.Deserialize<TaskDto>(a.TaskSnapshot);
                var code = latest.SingleOrDefault(e => e.TaskId == a.TaskId);
                return new { TaskId = a.TaskId, task.Title, Language = code?.Language ?? task.Language, Code = code?.Code ?? task.StarterCode, Version = code?.Version ?? 0 };
            })
        };
    }
}
