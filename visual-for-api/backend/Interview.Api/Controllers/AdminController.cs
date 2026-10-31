using Interview.Api.Contracts;
using Interview.Api.Data;
using Interview.Api.Infrastructure;
using Interview.Api.Models;
using Interview.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Interview.Api.Controllers;

[ApiController, Route("api/admin"), Authorize]
public class AdminController(AppDbContext db, AccessService access, DtoService dto, UserManager<AppUser> users, RoomConnections connections, IConfiguration config) : ControllerBase
{
    private async Task<AppUser> Admin()
    {
        var actor = await access.Actor(User); AccessService.Role(actor, "admin"); return actor;
    }

    [HttpGet("users")]
    public async Task<object> Users()
    {
        await Admin();
        var result = new List<UserDto>();
        foreach (var account in await db.Users.OrderBy(x => x.CreatedAt).ToListAsync()) result.Add(await dto.User(account));
        return result;
    }

    [HttpPost("users")]
    public async Task<object> Create(CreateUserRequest request)
    {
        var actor = await Admin();
        ApiError.Require(new[] { "interviewer", "teamlead", "admin" }.Contains(request.Role), "Неверная роль сотрудника.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        await DatabaseLocks.Acquire(db, "staff-accounts");
        if (request.Role == "teamlead") ApiError.Require(await db.Users.CountAsync(x => x.Role == "teamlead" && x.IsActive) < config.GetValue("Security:MaxTeamLeads", 10), "Достигнут лимит активных тимлидов.", 409);
        var account = new AppUser { UserName = request.Email.Trim(), Email = request.Email.Trim(), DisplayName = request.Name.Trim(), Role = request.Role, RunQuota = 30 };
        AuthController.IdentitySucceeded(await users.CreateAsync(account));
        AuthController.IdentitySucceeded(await users.AddToRoleAsync(account, request.Role));
        var token = AuthController.NewToken();
        var setup = new PasswordSetup { UserId = account.Id, TokenHash = AuthController.HashToken(token), ExpiresAt = DateTime.UtcNow.AddDays(1) };
        db.PasswordSetups.Add(setup); access.Audit(actor, "staff.create", account.Id);
        await db.SaveChangesAsync(); await transaction.CommitAsync();
        return new { User = await dto.User(account), SetupToken = token, setup.ExpiresAt };
    }

    [HttpPatch("users/{id}")]
    public async Task<object> Update(string id, UpdateUserRequest request)
    {
        var actor = await Admin();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await DatabaseLocks.Acquire(db, "staff-accounts");
        var account = await users.FindByIdAsync(id) ?? throw new ApiError(404, "Пользователь не найден.");
        if (request.Active is bool active)
        {
            ApiError.Require(active || actor.Id != account.Id, "Нельзя отключить собственную учётную запись.", 409);
            if (active && !account.IsActive && account.Role == "teamlead")
                ApiError.Require(await db.Users.CountAsync(x => x.Role == "teamlead" && x.IsActive) < config.GetValue("Security:MaxTeamLeads", 10), "Достигнут лимит тимлидов.", 409);
            if (!active && account.Role == "admin")
                ApiError.Require(await db.Users.CountAsync(x => x.Role == "admin" && x.IsActive) > 1, "Нельзя отключить последнего администратора.", 409);
            account.IsActive = active;
            AuthController.IdentitySucceeded(await users.UpdateSecurityStampAsync(account));
        }
        if (request.RunQuota is int quota) account.RunQuota = quota;
        AuthController.IdentitySucceeded(await users.UpdateAsync(account));
        access.Audit(actor, "staff.update", id); await db.SaveChangesAsync(); await transaction.CommitAsync();
        if (request.Active == false) await connections.Revoke(id);
        return await dto.User(account);
    }

    [HttpGet("audit")]
    public async Task<object> Audit()
    {
        await Admin();
        return await db.Audit.OrderByDescending(x => x.CreatedAt).Take(100).Select(x => new { x.Id, x.CreatedAt, x.Actor, x.Action, x.EntityId }).ToListAsync();
    }

    [HttpGet("rooms/{id:guid}/code-history")]
    public async Task<object> History(Guid id, [FromQuery] int afterVersion = 0, [FromQuery] Guid? taskId = null)
    {
        var actor = await Admin();
        ApiError.Require(taskId.HasValue, "Укажите taskId.");
        access.Audit(actor, "code-history.read", id); await db.SaveChangesAsync();
        return await db.CodeEvents.Where(x => x.RoomId == id && x.TaskId == taskId && x.Version > afterVersion).OrderBy(x => x.Version).Take(200)
            .Select(x => new { x.Id, x.TaskId, x.Version, x.Language, x.Code, x.CreatedAt }).ToListAsync();
    }
}
