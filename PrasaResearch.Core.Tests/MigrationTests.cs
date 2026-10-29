using System.IO;
using PrasaResarch.Services;
using PrasaResarch.Data;
namespace PrasaResarch.Tests;
public class MigrationTests
{
    [Fact]
    public void ReadOnlyLegacyImportPreservesSettingsJobsAndOriginalDatabase()
    {
        var root = Path.Combine(Path.GetTempPath(), "prasa-migration-" + Guid.NewGuid());
        var original = Path.Combine(root, "original"); var modern = Path.Combine(root, "desktop"); Directory.CreateDirectory(original);
        try
        {
            File.WriteAllText(Path.Combine(original, "settings.json"), "{\"OcrMaxPages\":0}");
            var old = new JobPersistenceService(Path.Combine(original, "jobs.db")); old.Save(new() { CompanyNumber = "00001234" });
            LegacyMigration.ImportOnce(modern, original);
            Assert.Equal(File.ReadAllText(Path.Combine(original, "settings.json")), File.ReadAllText(Path.Combine(modern, "settings.json")));
            var current = new JobPersistenceService(Path.Combine(modern, "jobs.db")); Assert.Single(current.Load());
            current.Clear(); Assert.Single(old.Load());
            LegacyMigration.ImportOnce(modern, original); Assert.Empty(current.Load());
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) File.Delete(file); Directory.Delete(modern); Directory.Delete(original); Directory.Delete(root); }
    }
}
