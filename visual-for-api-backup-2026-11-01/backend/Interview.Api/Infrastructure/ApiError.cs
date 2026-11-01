using Microsoft.EntityFrameworkCore;
using System.Diagnostics.CodeAnalysis;

namespace Interview.Api.Infrastructure;

public sealed class ApiError(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
    public static void Require([DoesNotReturnIf(false)] bool condition, string message, int status = 400)
    {
        if (!condition) throw new ApiError(status, message);
    }
}

// Блокировка живёт до завершения транзакции; два запроса не погасят один токен.
public static class DatabaseLocks
{
    public static async Task Acquire(Data.AppDbContext db, string resource, CancellationToken cancellation = default)
    {
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({resource}, 0))", cancellation);
    }
}
