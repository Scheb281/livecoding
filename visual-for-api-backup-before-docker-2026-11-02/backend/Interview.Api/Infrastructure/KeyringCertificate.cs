using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Interview.Api.Infrastructure;

public static class KeyringCertificate
{
    // Локальный сертификат шифрует key ring. Он не является HTTPS-сертификатом сайта.
    public static void Create(IConfiguration configuration)
    {
        var path = configuration["Security:KeyEncryptionCertificatePath"];
        var password = configuration["Security:KeyEncryptionCertificatePassword"];
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(password) || password.Length < 16)
            throw new InvalidOperationException("Задайте путь сертификата вне репозитория и пароль не короче 16 символов через секреты.");
        if (File.Exists(path)) throw new InvalidOperationException("Сертификат уже существует. Не перезаписывайте ключи существующей базы.");
        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest("CN=interview-keyring", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyEncipherment, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(5));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
