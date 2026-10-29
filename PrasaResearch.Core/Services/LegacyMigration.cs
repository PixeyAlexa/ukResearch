using Microsoft.Data.Sqlite;
namespace PrasaResarch.Services;
public static class LegacyMigration
{
    public static void ImportOnce(string destination, string source)
    {
        var identity = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(destination))));
        using var mutex = new Mutex(false, "PrasaResearchImport-" + identity);
        bool owned = false;
        try
        {
            try { owned = mutex.WaitOne(TimeSpan.FromSeconds(30)); } catch (AbandonedMutexException) { owned = true; }
            if (!owned) throw new IOException("Another PrasaResearch process is importing legacy data. Try launching again shortly.");
            ImportLocked(destination, source);
        }
        finally { if (owned) mutex.ReleaseMutex(); }
    }
    private static void ImportLocked(string destination, string source)
    {
        if (File.Exists(Path.Combine(destination, "legacy-import.complete"))) return;
        Directory.CreateDirectory(destination);
        foreach (var name in new[] { "settings.json", "credential.bin" })
            if (File.Exists(Path.Combine(source, name)) && !File.Exists(Path.Combine(destination, name))) File.Copy(Path.Combine(source, name), Path.Combine(destination, name));
        var original = Path.Combine(source, "jobs.db"); var migrated = Path.Combine(destination, "jobs.db");
        if (File.Exists(original) && !File.Exists(migrated))
        {
            using var input = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = original, Mode = SqliteOpenMode.ReadOnly }.ToString());
            var temporary = migrated + ".import-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var output = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Pooling = false }.ToString()))
                { input.Open(); output.Open(); input.BackupDatabase(output); }
                File.Move(temporary, migrated);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        File.WriteAllText(Path.Combine(destination, "legacy-import.complete"), $"{DateTimeOffset.UtcNow:O}\nRead-only import from {source}. Original files were not changed.\n");
    }
}
