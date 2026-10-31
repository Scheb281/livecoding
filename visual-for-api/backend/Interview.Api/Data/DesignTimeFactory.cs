using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

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
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
    }
}
