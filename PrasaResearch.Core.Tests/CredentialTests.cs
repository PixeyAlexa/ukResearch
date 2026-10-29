using System.IO;
using PrasaResarch.Services;
namespace PrasaResarch.Tests;
public class CredentialTests
{
    [Fact]
    public void NativeCredentialRoundtripAndUpdate()
    {
        const string fixture = "test-credential-not-a-real-api-key";
        if (OperatingSystem.IsWindows())
        {
            var folder = Path.Combine(Path.GetTempPath(), "prasa-credential-" + Guid.NewGuid());
            try
            {
                var store = new WindowsCredentialStore(folder); Assert.Equal("", store.Read());
                store.Write(fixture); Assert.Equal(fixture, store.Read());
                Assert.DoesNotContain(fixture, System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(folder, "credential.bin"))));
                store.Write("updated-fixture"); Assert.Equal("updated-fixture", store.Read());
            }
            finally { if (Directory.Exists(folder)) { foreach (var file in Directory.GetFiles(folder)) File.Delete(file); Directory.Delete(folder); } }
        }
        else if (OperatingSystem.IsMacOS())
        {
            var store = new MacKeychainCredentialStore("com.prasaresearch.test." + Guid.NewGuid().ToString("N"));
            try { Assert.Equal("", store.Read()); store.Write(fixture); Assert.Equal(fixture, store.Read()); store.Write("updated-fixture"); Assert.Equal("updated-fixture", store.Read()); }
            finally { store.DeleteTestCredential(); }
        }
        else Assert.Fail("This desktop release targets Windows and macOS.");
    }
}
