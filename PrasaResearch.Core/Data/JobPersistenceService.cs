using Microsoft.Data.Sqlite;
using PrasaResarch.Models;
using System.Text.Json;

namespace PrasaResarch.Data;
public class JobPersistenceService
{
    private readonly string connectionString;
    public JobPersistenceService(Services.SettingsService settings) : this(Path.Combine(settings.Root, "jobs.db")) { }
    public JobPersistenceService(string path)
    {
        connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        using var db = Open(); using var command = db.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS results (number TEXT PRIMARY KEY, data TEXT NOT NULL); CREATE TABLE IF NOT EXISTS revenue_reviews (id INTEGER PRIMARY KEY AUTOINCREMENT,number TEXT NOT NULL,decision TEXT NOT NULL,previous_result TEXT NOT NULL);"; command.ExecuteNonQuery();
    }
    private SqliteConnection Open() { var db = new SqliteConnection(connectionString); db.Open(); return db; }
    public List<CompanyResult> Load()
    {
        using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT data FROM results ORDER BY number";
        using var reader = cmd.ExecuteReader(); var results = new List<CompanyResult>();
        while (reader.Read()) results.Add(JsonSerializer.Deserialize<CompanyResult>(reader.GetString(0))!); return results;
    }
    public void Save(CompanyResult result)
    {
        using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "INSERT INTO results(number,data) VALUES($number,$data) ON CONFLICT(number) DO UPDATE SET data=$data";
        cmd.Parameters.AddWithValue("$number", result.CompanyNumber); cmd.Parameters.AddWithValue("$data", JsonSerializer.Serialize(result)); cmd.ExecuteNonQuery();
    }
    public void Clear() { using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "DELETE FROM results"; cmd.ExecuteNonQuery(); }
    public void SaveManualDecision(CompanyResult previous, CompanyResult verified, ManualRevenueDecision decision)
    {
        using var db = Open(); using var transaction = db.BeginTransaction(); using var cmd = db.CreateCommand(); cmd.Transaction = transaction;
        cmd.CommandText = "INSERT INTO revenue_reviews(number,decision,previous_result) VALUES($number,$decision,$previous); INSERT INTO results(number,data) VALUES($number,$data) ON CONFLICT(number) DO UPDATE SET data=$data;";
        cmd.Parameters.AddWithValue("$number", previous.CompanyNumber); cmd.Parameters.AddWithValue("$decision", JsonSerializer.Serialize(decision));
        cmd.Parameters.AddWithValue("$previous", JsonSerializer.Serialize(previous)); cmd.Parameters.AddWithValue("$data", JsonSerializer.Serialize(verified));
        cmd.ExecuteNonQuery(); transaction.Commit();
    }
    public List<ManualRevenueDecision> Reviews(string number)
    {
        using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT decision FROM revenue_reviews WHERE number=$number ORDER BY id DESC"; cmd.Parameters.AddWithValue("$number", number);
        using var reader = cmd.ExecuteReader(); var results = new List<ManualRevenueDecision>(); while (reader.Read()) results.Add(JsonSerializer.Deserialize<ManualRevenueDecision>(reader.GetString(0))!); return results;
    }
}
