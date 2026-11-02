using System.Text.Json;
using Npgsql;

// Настройки приходят через stdin: пароль не попадает в аргументы процесса.
try
{
    var input = await Console.In.ReadToEndAsync();
    var settings = JsonSerializer.Deserialize<ConnectionSettings>(input)
        ?? throw new InvalidOperationException();

    var builder = new NpgsqlConnectionStringBuilder
    {
        Host = settings.Host,
        Port = settings.Port,
        Database = settings.Database,
        Username = settings.Username,
        Password = settings.Password,
        SslMode = SslMode.VerifyFull,
        RootCertificate = settings.Certificate,
        Timeout = 15,
        CommandTimeout = 15,
        Pooling = false
    };

    await using var connection = new NpgsqlConnection(builder.ConnectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("SELECT VERSION();", connection);
    // VERSION() возвращает текст, поэтому GetInt32 из исходного примера не подходит.
    var version = (string?)await command.ExecuteScalarAsync();
    if (args.Contains("--inspect-schema"))
    {
        // Только чтение: проверяем, что миграции и создание ролей завершились.
        await using var tableCommand = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = current_schema() AND table_type = 'BASE TABLE';", connection);
        var tableCount = (long)(await tableCommand.ExecuteScalarAsync())!;
        await using var migrationCommand = new NpgsqlCommand(
            "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\";", connection);
        var migrations = new List<string>();
        await using (var reader = await migrationCommand.ExecuteReaderAsync())
            while (await reader.ReadAsync()) migrations.Add(reader.GetString(0));
        await using var roleCommand = new NpgsqlCommand("SELECT count(*) FROM \"AspNetRoles\";", connection);
        var roleCount = (long)(await roleCommand.ExecuteScalarAsync())!;
        await using var adminCommand = new NpgsqlCommand(
            "SELECT count(*) FROM \"AspNetUsers\" WHERE \"Role\" = 'admin';", connection);
        var adminCount = (long)(await adminCommand.ExecuteScalarAsync())!;
        Console.WriteLine(JsonSerializer.Serialize(new { success = true, version, tableCount, migrations, roleCount, adminCount }));
        return 0;
    }
    Console.WriteLine(JsonSerializer.Serialize(new { success = true, version }));
    return 0;
}
catch (PostgresException error)
{
    // Не печатаем строку подключения, пароль и полный текст серверной ошибки.
    Console.WriteLine(JsonSerializer.Serialize(new { success = false, sqlState = error.SqlState }));
    return 1;
}
catch (Exception error)
{
    Console.WriteLine(JsonSerializer.Serialize(new { success = false, errorType = error.GetType().Name }));
    return 1;
}

internal record ConnectionSettings(string Host, int Port, string Database,
    string Username, string Password, string Certificate);
