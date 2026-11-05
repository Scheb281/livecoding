using Interview.Api.Data;
using Interview.Api.Services;
using Interview.Api.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Interview.Api.Controllers;

[ApiController, Route("api/reports"), Authorize]
public class ReportsController(AppDbContext db, AccessService access) : ControllerBase
{
    [HttpGet]
    public async Task<object> Get()
    {
        var actor = await access.Actor(User); AccessService.Role(actor, "teamlead");
        var rooms = await db.Rooms.ToListAsync();
        var assignments = await db.RoomTasks.ToListAsync();
        var evaluations = await db.Evaluations.Include(x => x.Scores).ToListAsync();
        var submissions = await db.Submissions.ToListAsync();
        var hints = await db.Hints.ToListAsync();
        var usages = await db.HintUsages.ToListAsync();
        var tasks = await db.Tasks.ToListAsync();
        double? Average(Guid roomId)
        {
            var scores = evaluations.Where(x => x.RoomId == roomId).SelectMany(x => x.Scores).Select(x => x.Score).ToArray();
            return scores.Length == 0 ? null : scores.Average();
        }
        decimal? Final(Guid roomId)
        {
            var room = rooms.Single(r => r.Id == roomId);
            var evaluation = evaluations.FirstOrDefault(e => e.RoomId == roomId);
            return evaluation == null ? null : ScoringService.Calculate(DtoService.Deserialize<RubricDto>(room.RubricSnapshot), evaluation.Scores.ToDictionary(s => s.CriterionKey, s => s.Score));
        }
        return new
        {
            Candidates = rooms.Where(x => x.CandidateId != null).Select(room => new
            {
                room.Id,
                Name = room.CandidateName,
                Interview = room.Title,
                Submitted = submissions.Count(x => x.RoomId == room.Id),
                HintsUsed = usages.Count(x => hints.Any(hint => hint.Id == x.HintId && hint.RoomId == room.Id)),
                AverageScore = Average(room.Id),
                FinalScore = Final(room.Id)
            }),
            Tasks = tasks.Select(task =>
            {
                var assignedRooms = rooms.Where(x => x.CandidateId != null && assignments.Any(a => a.RoomId == x.Id && a.TaskId == task.Id)).ToArray();
                var averages = assignedRooms.Select(x => Average(x.Id)).Where(x => x.HasValue).Select(x => x!.Value).ToArray();
                return new
                {
                    task.Id,
                    task.Title,
                    Attempts = assignedRooms.Select(x => x.CandidateId).Distinct().Count(),
                    Solved = submissions.Where(x => x.TaskId == task.Id && x.VerifiedRunId != null).Select(x => x.CandidateId).Distinct().Count(),
                    AverageScore = averages.Length == 0 ? (double?)null : averages.Average()
                };
            })
        };
    }
}
