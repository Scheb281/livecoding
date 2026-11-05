using Interview.Api.Models;
using Microsoft.AspNetCore.Identity;

namespace Interview.Api.Infrastructure;

// Приглашённому гостю email не нужен; обычные учётные записи проверяются Identity.
public sealed class GuestUserValidator : UserValidator<AppUser>
{
    public override async Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user)
    {
        // Такой логин генерирует сервер в AuthController.Redeem, а не форма регистрации.
        var isGuest = user.Role == "candidate"
            && user.Email is null
            && user.PasswordHash is null
            && user.UserName is string name
            && name.StartsWith("guest-", StringComparison.Ordinal)
            && Guid.TryParseExact(name[6..], "N", out _);

        if (!isGuest) return await base.ValidateAsync(manager, user);

        // У гостя имя уже ограничено форматом guest-GUID, но уникальность всё равно проверяем.
        var existing = await manager.FindByNameAsync(user.UserName!);
        return existing != null && existing.Id != user.Id
            ? IdentityResult.Failed(Describer.DuplicateUserName(user.UserName!))
            : IdentityResult.Success;
    }
}
