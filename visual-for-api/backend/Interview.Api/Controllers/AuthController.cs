using System.Security.Cryptography;
using System.Text;
using Interview.Api.Contracts;
using Interview.Api.Data;
using Interview.Api.Infrastructure;
using Interview.Api.Models;
using Interview.Api.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Interview.Api.Controllers;

[ApiController, Route("api"), Authorize]
public class AuthController(AppDbContext db, UserManager<AppUser> users, SignInManager<AppUser> signIn, AccessService access, DtoService dto) : ControllerBase
{
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public static void IdentitySucceeded(IdentityResult result)
    {
        if (!result.Succeeded) throw new ApiError(400, string.Join(" ", result.Errors.Select(x => x.Description)));
    }

    [HttpGet("auth/csrf"), AllowAnonymous]
    public object Csrf([FromServices] IAntiforgery antiforgery) => new { Token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken };

    [HttpGet("auth/me")]
    public async Task<object> Me() => new { User = await dto.User(await access.Actor(User)) };

    [HttpPost("auth/register/candidate"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<object> Register(RegisterRequest request)
    {
        ApiError.Require(!string.IsNullOrWhiteSpace(request.Name), "Укажите имя.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = new AppUser { UserName = request.Email.Trim(), Email = request.Email.Trim(), DisplayName = request.Name.Trim(), Role = "candidate", RunQuota = 10 };
        IdentitySucceeded(await users.CreateAsync(user, request.Password));
        IdentitySucceeded(await users.AddToRoleAsync(user, "candidate"));
        access.Audit(user, "candidate.register", user.Id);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        await signIn.SignInAsync(user, false);
        return new { User = await dto.User(user) };
    }

    [HttpPost("auth/login"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<object> Login(LoginRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is not { IsActive: true }) throw new ApiError(401, "Неверный email или пароль.");
        var result = await signIn.PasswordSignInAsync(user, request.Password, false, true);
        if (!result.Succeeded) throw new ApiError(401, "Неверный email или пароль, либо вход временно заблокирован.");
        access.Audit(user, "account.login", user.Id);
        await db.SaveChangesAsync();
        return new { User = await dto.User(user) };
    }

    [HttpPost("auth/logout")]
    public async Task<IActionResult> Logout([FromServices] RoomConnections connections)
    {
        var actor = await access.Actor(User);
        await connections.Revoke(actor.Id);
        await signIn.SignOutAsync();
        return NoContent();
    }

    [HttpPost("invitations/redeem"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<object> Redeem(RedeemRequest request)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var hash = HashToken(request.Token);
        await DatabaseLocks.Acquire(db, "invite:" + hash);
        var invitation = await db.Invitations.SingleOrDefaultAsync(x => x.TokenHash == hash);
        ApiError.Require(invitation != null && invitation.ConsumedAt == null && invitation.ExpiresAt > DateTime.UtcNow,
            "Приглашение недействительно, истекло или уже использовано.", 409);
        await DatabaseLocks.Acquire(db, "room:" + invitation!.RoomId);
        var room = await db.Rooms.SingleAsync(x => x.Id == invitation.RoomId);
        AccessService.Active(room);
        ApiError.Require(room.CandidateId == null, "К этой комнате уже подключён кандидат.", 409);
        AppUser? candidate = null;
        if (User.Identity?.IsAuthenticated == true)
        {
            candidate = await access.Actor(User);
            AccessService.Role(candidate, "candidate");
        }
        if (candidate == null)
        {
            candidate = new AppUser { UserName = "guest-" + Guid.NewGuid().ToString("N"), DisplayName = request.Name.Trim(), Role = "candidate", RunQuota = 10 };
            IdentitySucceeded(await users.CreateAsync(candidate));
            IdentitySucceeded(await users.AddToRoleAsync(candidate, "candidate"));
        }
        ApiError.Require(!string.IsNullOrWhiteSpace(request.Name), "Укажите имя кандидата.");
        room.CandidateId = candidate.Id;
        room.CandidateName = request.Name.Trim();
        invitation.ConsumedAt = DateTime.UtcNow;
        access.Audit(candidate, "invitation.redeem", room.Id);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        await signIn.SignInAsync(candidate, false);
        await HttpContext.RequestServices.GetRequiredService<RoomConnections>().Changed(room.Id);
        return new { User = await dto.User(candidate), RoomId = room.Id };
    }

    [HttpPost("auth/setup-password"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<object> SetupPassword(SetupRequest request)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var hash = HashToken(request.Token);
        await DatabaseLocks.Acquire(db, "setup:" + hash);
        var setup = await db.PasswordSetups.SingleOrDefaultAsync(x => x.TokenHash == hash);
        ApiError.Require(setup != null && setup.ConsumedAt == null && setup.ExpiresAt > DateTime.UtcNow, "Ссылка настройки пароля недействительна.", 409);
        var user = await users.FindByIdAsync(setup!.UserId) ?? throw new ApiError(404, "Пользователь не найден.");
        ApiError.Require(user.IsActive && !await users.HasPasswordAsync(user), "Для этой учётной записи уже установлен пароль.", 409);
        IdentitySucceeded(await users.AddPasswordAsync(user, request.Password));
        setup.ConsumedAt = DateTime.UtcNow;
        access.Audit(user, "account.password-setup", user.Id);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        await signIn.SignInAsync(user, false);
        return new { User = await dto.User(user) };
    }
}
