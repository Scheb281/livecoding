using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Logging.Abstractions;

namespace Interview.Api.Infrastructure;

// Меняет защиту файлов ключей с Windows DPAPI на PFX; сами ключи и их ID сохраняет.
public static class KeyringExport
{
    private const string Purpose = "interview.keyring.export.check";
    private const string CheckText = "interview-keyring-portability-check";

    public static void Run(IServiceProvider services, IConfiguration configuration)
    {
        var directory = Path.GetFullPath(configuration["Export:Directory"] ?? throw new InvalidOperationException("Задайте Export:Directory."));
        var certificatePath = configuration["Export:CertificatePath"] ?? throw new InvalidOperationException("Задайте Export:CertificatePath.");
        var passwordFile = configuration["Export:CertificatePasswordFile"] ?? throw new InvalidOperationException("Задайте Export:CertificatePasswordFile.");
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
            throw new InvalidOperationException("Каталог экспорта должен быть пустым. Существующие ключи не перезаписываются.");

        using var certificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath,
            File.ReadAllText(passwordFile).TrimEnd('\r', '\n'), X509KeyStorageFlags.EphemeralKeySet);
        if (!certificate.HasPrivateKey) throw new InvalidOperationException("PFX должен содержать закрытый ключ.");
        var source = services.GetRequiredService<IDataProtectionProvider>();
        var check = source.CreateProtector(Purpose).Protect(CheckText);
        var keys = services.GetRequiredService<IKeyManager>().GetAllKeys();
        if (keys.Any(key => key.IsRevoked))
            throw new InvalidOperationException("В key ring есть отозванные ключи. Их перенос требует отдельного сохранения правил отзыва.");

        Directory.CreateDirectory(directory);
        var encryptor = new CertificateXmlEncryptor(certificate, NullLoggerFactory.Instance);
        XNamespace protectionNamespace = "http://schemas.asp.net/2015/03/dataProtection";
        foreach (var key in keys)
        {
            var descriptor = key.Descriptor.ExportToXml();
            // Внешний descriptor нужен XmlKeyManager для выбора десериализатора.
            // Шифруем его содержимое; идентификатор алгоритма остаётся снаружи.
            var encrypted = encryptor.Encrypt(descriptor.SerializedDescriptorElement);
            var descriptorElement = new XElement("descriptor",
                new XAttribute("deserializerType", descriptor.DeserializerType.AssemblyQualifiedName!),
                new XElement(protectionNamespace + "encryptedSecret",
                    new XAttribute("decryptorType", encrypted.DecryptorType.AssemblyQualifiedName!), encrypted.EncryptedElement));
            var keyElement = new XElement("key", new XAttribute("id", key.KeyId), new XAttribute("version", 1),
                new XElement("creationDate", key.CreationDate.ToString("O", CultureInfo.InvariantCulture)),
                new XElement("activationDate", key.ActivationDate.ToString("O", CultureInfo.InvariantCulture)),
                new XElement("expirationDate", key.ExpirationDate.ToString("O", CultureInfo.InvariantCulture)), descriptorElement);
            var path = Path.Combine(directory, $"key-{key.KeyId}.xml");
            keyElement.Save(path);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        // Проверка старого шифротекста новым хранилищем, без обращения к БД.
        var imported = DataProtectionProvider.Create(new DirectoryInfo(directory), options => options
            .SetApplicationName("visual-interview").UnprotectKeysWithAnyCertificate(certificate).DisableAutomaticKeyGeneration());
        if (imported.CreateProtector(Purpose).Unprotect(check) != CheckText)
            throw new InvalidOperationException("Проверка переноса ключей не прошла.");
        File.WriteAllText(Path.Combine(directory, "export-check.txt"), check);
        Console.WriteLine($"Экспортировано ключей: {keys.Count}. Они защищены PFX; проверка расшифрования пройдена.");
    }

    public static void Verify(IServiceProvider services, IConfiguration configuration)
    {
        var path = configuration["Export:CheckFile"] ?? "/imported-keys/export-check.txt";
        var value = services.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose)
            .Unprotect(File.ReadAllText(path).Trim());
        if (value != CheckText) throw new InvalidOperationException("Проверка импортированных ключей не прошла.");
        Console.WriteLine("Импортированные ключи расшифровывают исходные данные.");
    }
}
