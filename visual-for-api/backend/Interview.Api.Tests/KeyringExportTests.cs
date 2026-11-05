using System.Security.Cryptography.X509Certificates;
using Interview.Api.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Interview.Api.Tests;

public class KeyringExportTests
{
    [Fact]
    public void Export_Preserves_Keys_And_Data_Under_A_Portable_Certificate()
    {
        var root = Path.Combine(Path.GetTempPath(), "interview-key-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var certificatePath = Path.Combine(root, "keyring.pfx");
            var passwordFile = Path.Combine(root, "password");
            const string password = "Synthetic-certificate-test-123456!";
            File.WriteAllText(passwordFile, password);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:KeyEncryptionCertificatePath"] = certificatePath,
                ["Security:KeyEncryptionCertificatePassword"] = password,
                ["Export:CertificatePath"] = certificatePath,
                ["Export:CertificatePasswordFile"] = passwordFile,
                ["Export:Directory"] = Path.Combine(root, "exported")
            }).Build();
            KeyringCertificate.Create(configuration);
            var services = new ServiceCollection();
            var protection = services.AddDataProtection().SetApplicationName("visual-interview")
                .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(root, "source")));
            if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
            using var provider = services.BuildServiceProvider();
            var cipher = new FieldCipher(provider.GetRequiredService<IDataProtectionProvider>());
            var original = cipher.Encrypt("Синтетический исходник с Unicode");
            var originalIds = provider.GetRequiredService<IKeyManager>().GetAllKeys().Select(key => key.KeyId).Order().ToArray();
            KeyringExport.Run(provider, configuration);

            using var certificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath, password);
            var importedServices = new ServiceCollection();
            importedServices.AddDataProtection().SetApplicationName("visual-interview")
                .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(root, "exported")))
                .ProtectKeysWithCertificate(certificate).DisableAutomaticKeyGeneration();
            using var imported = importedServices.BuildServiceProvider();
            Assert.Equal(originalIds, imported.GetRequiredService<IKeyManager>().GetAllKeys().Select(key => key.KeyId).Order().ToArray());
            Assert.Equal("Синтетический исходник с Unicode", new FieldCipher(imported.GetRequiredService<IDataProtectionProvider>()).Decrypt(original));
            foreach (var file in Directory.GetFiles(Path.Combine(root, "exported"), "key-*.xml"))
            {
                var xml = File.ReadAllText(file);
                Assert.Contains("encryptedSecret", xml);
                Assert.DoesNotContain("DpapiXmlDecryptor", xml);
                Assert.DoesNotContain("masterKey", xml);
            }
            Assert.Throws<InvalidOperationException>(() => KeyringExport.Run(provider, configuration));
        }
        finally { Directory.Delete(root, true); }
    }
}
