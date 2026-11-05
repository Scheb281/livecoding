using Interview.Api.Data;
using Interview.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Interview.Api.Services;

public sealed class RoomExpiryWorker(IServiceScopeFactory scopes, ILogger<RoomExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var ids = await db.Rooms.Where(r => r.Status == "active" && r.SessionExpiresAt <= DateTime.UtcNow).Select(r => r.Id).ToListAsync(stoppingToken);
                foreach (var id in ids)
                {
                    await using var transaction = await db.Database.BeginTransactionAsync(stoppingToken);
                    await DatabaseLocks.Acquire(db, "room:" + id, stoppingToken);
                    var room = await db.Rooms.SingleAsync(r => r.Id == id, stoppingToken);
                    if (room.Status != "active" || !AccessService.Expired(room)) continue;
                    room.Status = "completed"; room.CompletedAt = room.SessionExpiresAt;
                    await scope.ServiceProvider.GetRequiredService<SessionRecorder>().Append(id, "SessionExpired", null, new { room.CompletedAt });
                    await transaction.CommitAsync(stoppingToken);
                    await scope.ServiceProvider.GetRequiredService<RoomConnections>().Changed(id);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "Не удалось завершить истёкшие комнаты"); }
        }
    }
}
