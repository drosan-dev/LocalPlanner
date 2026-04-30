using Microsoft.Data.Sqlite;

namespace LocalPlanner.Desktop.Services;

public sealed class DatabaseInitializer
{
    private readonly string _connectionString;

    public DatabaseInitializer(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath
        }.ToString();
    }

    public string ConnectionString => _connectionString;

    public void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var pragmaCommand = connection.CreateCommand();
        pragmaCommand.CommandText = "PRAGMA foreign_keys = ON;";
        pragmaCommand.ExecuteNonQuery();

        using var command = connection.CreateCommand();
        command.CommandText =
            @"CREATE TABLE IF NOT EXISTS events (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                description TEXT NULL,
                starts_at_utc TEXT NOT NULL,
                ends_at_utc TEXT NOT NULL,
                timezone_id TEXT NOT NULL,
                is_all_day INTEGER NOT NULL DEFAULT 0,
                rrule_text TEXT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                deleted_at_utc TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS operations (
                op_id TEXT PRIMARY KEY,
                origin_device_id TEXT NOT NULL,
                origin_seq INTEGER NOT NULL,
                entity_type TEXT NOT NULL,
                entity_id TEXT NOT NULL,
                op_type TEXT NOT NULL,
                payload_json TEXT NULL,
                event_updated_at_utc TEXT NOT NULL,
                recorded_at_utc TEXT NOT NULL,
                UNIQUE(origin_device_id, origin_seq)
            );";

        command.ExecuteNonQuery();
    }
}
