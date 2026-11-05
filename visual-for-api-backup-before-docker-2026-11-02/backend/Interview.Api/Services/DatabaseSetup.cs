using Interview.Api.Controllers;
using Interview.Api.Data;
using Interview.Api.Infrastructure;
using Interview.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Interview.Api.Services;

public static class DatabaseSetup
{
    public static readonly string[] Roles = ["candidate", "interviewer", "teamlead", "admin"];
    public static async Task SeedRoles(IServiceProvider services)
    {
        var manager = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Roles)
            if (!await manager.RoleExistsAsync(role)) AuthController.IdentitySucceeded(await manager.CreateAsync(new IdentityRole(role)));
    }

    // Первый администратор создаётся только локальной командой оператора, не публичным API.
    public static async Task BootstrapAdmin(IServiceProvider services, IConfiguration config)
    {
        var db = services.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await DatabaseLocks.Acquire(db, "staff-accounts");
        ApiError.Require(!await db.Users.AnyAsync(x => x.Role == "admin"), "Администратор уже существует. Начальное создание повторять нельзя.", 409);
        var email = config["Bootstrap:Email"];
        var password = config["Bootstrap:Password"];
        ApiError.Require(!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(password), "Задайте Bootstrap:Email и Bootstrap:Password через user-secrets или переменные среды.");
        var manager = services.GetRequiredService<UserManager<AppUser>>();
        var account = new AppUser { UserName = email, Email = email, DisplayName = config["Bootstrap:Name"] ?? "Администратор", Role = "admin", RunQuota = 100 };
        AuthController.IdentitySucceeded(await manager.CreateAsync(account, password!));
        AuthController.IdentitySucceeded(await manager.AddToRoleAsync(account, "admin"));
        db.Audit.Add(new AuditEvent { Actor = "Система", Action = "admin.bootstrap", EntityId = account.Id });
        await db.SaveChangesAsync(); await transaction.CommitAsync();
    }
}
