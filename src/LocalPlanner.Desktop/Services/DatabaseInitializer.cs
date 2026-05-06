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
            );

            CREATE TABLE IF NOT EXISTS projects (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                color TEXT NOT NULL,
                status TEXT NOT NULL,
                description TEXT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS tags (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS project_tags (
                project_id TEXT NOT NULL,
                tag_id TEXT NOT NULL,
                PRIMARY KEY (project_id, tag_id),
                FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
                FOREIGN KEY (tag_id) REFERENCES tags(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS planning_items (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                notes TEXT NULL,
                project_id TEXT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                archived_at_utc TEXT NULL,
                FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS task_items (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                notes TEXT NULL,
                project_id TEXT NULL,
                due_at_utc TEXT NULL,
                due_ends_at_utc TEXT NULL,
                timezone_id TEXT NULL,
                is_completed INTEGER NOT NULL DEFAULT 0,
                only_this_day INTEGER NOT NULL DEFAULT 0,
                completed_at_utc TEXT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                archived_at_utc TEXT NULL,
                FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE SET NULL
            );";

        command.ExecuteNonQuery();

        EnsureColumn(connection, "planning_items", "planned_starts_at_utc", "TEXT NULL");
        EnsureColumn(connection, "planning_items", "planned_ends_at_utc", "TEXT NULL");
        EnsureColumn(connection, "planning_items", "timezone_id", "TEXT NULL");
        EnsureColumn(connection, "planning_items", "is_all_day", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "task_items", "due_ends_at_utc", "TEXT NULL");
        EnsureColumn(connection, "task_items", "only_this_day", "INTEGER NOT NULL DEFAULT 0");
    }

    private static void EnsureColumn(SqliteConnection connection, string tableName, string columnName, string columnDefinition)
    {
        using (var infoCommand = connection.CreateCommand())
        {
            infoCommand.CommandText = $"PRAGMA table_info({tableName});";
            using var reader = infoCommand.ExecuteReader();
            while (reader.Read())
            {
                if (reader.GetString(1) == columnName)
                {
                    return;
                }
            }
        }

        using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        alterCommand.ExecuteNonQuery();
    }
}
