using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.AspNetCore.DataProtection;
using Interview.Api.Infrastructure;
using Microsoft.AspNetCore.Identity;

namespace Interview.Api.Data;

// Генерация миграции и SQL не требует работающей базы и реального пароля.
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", true).AddUserSecrets<AppDbContext>(true).AddEnvironmentVariables().Build();
        var connection = configuration.GetConnectionString("Database");
        if (string.IsNullOrWhiteSpace(connection)) connection = "Host=localhost;Database=interview;Username=postgres";
        var services = new ServiceCollection().Configure<IdentityOptions>(options => options.Stores.SchemaVersion = IdentitySchemaVersions.Version2).BuildServiceProvider();
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).UseApplicationServiceProvider(services).Options, new FieldCipher(new EphemeralDataProtectionProvider()));
    }
}
