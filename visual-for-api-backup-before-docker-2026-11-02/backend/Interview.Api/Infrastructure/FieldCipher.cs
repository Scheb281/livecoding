using Microsoft.AspNetCore.DataProtection;

namespace Interview.Api.Infrastructure;

// Data Protection обеспечивает шифрование и проверку целостности. Пароли сюда не попадают.
public sealed class FieldCipher(IDataProtectionProvider provider)
{
    private readonly IDataProtector protector = provider.CreateProtector("interview.database.fields.v1");
    private const string Prefix = "protected:v1:";
    public string Encrypt(string value) => Prefix + protector.Protect(value);

    // Старые строки читаются до выполнения явной команды --encrypt-existing-data.
    public string Decrypt(string value) => value.StartsWith(Prefix, StringComparison.Ordinal)
        ? protector.Unprotect(value[Prefix.Length..]) : value;
}
