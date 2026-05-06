using System;
using System.Collections.Generic;
using System.Globalization;
using LocalPlanner.Desktop.Models;
using Microsoft.Data.Sqlite;

namespace LocalPlanner.Desktop.Services;

public sealed class TaskRepository
{
    private readonly string _connectionString;

    public TaskRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public IReadOnlyList<TaskItem> GetActiveItems()
    {
        var results = new List<TaskItem>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            @"SELECT id, title, notes, project_id, due_at_utc, due_ends_at_utc, timezone_id, is_completed, only_this_day, completed_at_utc, created_at_utc, updated_at_utc, archived_at_utc
            FROM task_items
            WHERE archived_at_utc IS NULL
            ORDER BY is_completed ASC, COALESCE(due_at_utc, updated_at_utc) ASC, updated_at_utc DESC;";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadTaskItem(reader));
        }

        return results;
    }

    public TaskItem? GetById(Guid id)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            @"SELECT id, title, notes, project_id, due_at_utc, due_ends_at_utc, timezone_id, is_completed, only_this_day, completed_at_utc, created_at_utc, updated_at_utc, archived_at_utc
            FROM task_items
            WHERE id = $id
            LIMIT 1;";
        command.Parameters.AddWithValue("$id", id.ToString());

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadTaskItem(reader) : null;
    }

    public TaskItem Save(TaskItemEditorState editorState)
    {
        var title = editorState.Title.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException("Введите название задачи.");
        }

        var timezone = ResolveTimezone(editorState.TimezoneId);
        DateTime? dueAtUtc = editorState.DueLocal is null
            ? null
            : ConvertEditorTimeToUtc(editorState.DueLocal.Value, timezone);
        DateTime? dueEndsAtUtc = editorState.DueEndLocal is null
            ? null
            : ConvertEditorTimeToUtc(editorState.DueEndLocal.Value, timezone);

        if (dueAtUtc is null && dueEndsAtUtc is not null)
        {
            throw new InvalidOperationException("Укажите начало срока задачи.");
        }

        if (dueAtUtc is not null && dueEndsAtUtc is not null && dueEndsAtUtc <= dueAtUtc)
        {
            throw new InvalidOperationException("Окончание задачи должно быть позже начала.");
        }

        var nowUtc = DateTime.UtcNow;
        var id = editorState.Id ?? Guid.NewGuid();
        var existing = editorState.Id.HasValue ? GetById(id) : null;
        var item = new TaskItem
        {
            Id = id,
            Title = title,
            Notes = NormalizeNullable(editorState.Notes),
            ProjectId = editorState.ProjectId,
            DueAtUtc = dueAtUtc,
            DueEndsAtUtc = dueEndsAtUtc,
            TimezoneId = timezone.Id,
            IsCompleted = existing?.IsCompleted ?? false,
            IsOnlyThisDay = editorState.IsOnlyThisDay,
            CompletedAtUtc = existing?.CompletedAtUtc,
            CreatedAtUtc = existing?.CreatedAtUtc ?? nowUtc,
            UpdatedAtUtc = nowUtc,
            ArchivedAtUtc = null
        };

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            @"INSERT INTO task_items (id, title, notes, project_id, due_at_utc, due_ends_at_utc, timezone_id, is_completed, only_this_day, completed_at_utc, created_at_utc, updated_at_utc, archived_at_utc)
            VALUES ($id, $title, $notes, $projectId, $dueAtUtc, $dueEndsAtUtc, $timezoneId, $isCompleted, $onlyThisDay, $completedAtUtc, $createdAtUtc, $updatedAtUtc, NULL)
            ON CONFLICT(id) DO UPDATE SET
                title = excluded.title,
                notes = excluded.notes,
                project_id = excluded.project_id,
                due_at_utc = excluded.due_at_utc,
                due_ends_at_utc = excluded.due_ends_at_utc,
                timezone_id = excluded.timezone_id,
                is_completed = excluded.is_completed,
                only_this_day = excluded.only_this_day,
                completed_at_utc = excluded.completed_at_utc,
                updated_at_utc = excluded.updated_at_utc,
                archived_at_utc = NULL;";
        BindItem(command, item);
        command.ExecuteNonQuery();

        return item;
    }

    public bool SetCompleted(Guid id, bool isCompleted)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            @"UPDATE task_items
            SET is_completed = $isCompleted,
                completed_at_utc = $completedAtUtc,
                updated_at_utc = $updatedAtUtc
            WHERE id = $id AND archived_at_utc IS NULL;";
        var nowUtc = DateTime.UtcNow;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$isCompleted", isCompleted ? 1 : 0);
        command.Parameters.AddWithValue("$completedAtUtc", isCompleted ? nowUtc.ToString("O") : DBNull.Value);
        command.Parameters.AddWithValue("$updatedAtUtc", nowUtc.ToString("O"));
        return command.ExecuteNonQuery() > 0;
    }

    public bool Archive(Guid id)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            @"UPDATE task_items
            SET archived_at_utc = $archivedAtUtc, updated_at_utc = $updatedAtUtc
            WHERE id = $id AND archived_at_utc IS NULL;";
        var nowUtc = DateTime.UtcNow;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$archivedAtUtc", nowUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedAtUtc", nowUtc.ToString("O"));
        return command.ExecuteNonQuery() > 0;
    }

    private static void BindItem(SqliteCommand command, TaskItem item)
    {
        command.Parameters.AddWithValue("$id", item.Id.ToString());
        command.Parameters.AddWithValue("$title", item.Title);
        command.Parameters.AddWithValue("$notes", (object?)item.Notes ?? DBNull.Value);
        command.Parameters.AddWithValue("$projectId", item.ProjectId is null ? DBNull.Value : item.ProjectId.Value.ToString());
        command.Parameters.AddWithValue("$dueAtUtc", item.DueAtUtc is null ? DBNull.Value : item.DueAtUtc.Value.ToString("O"));
        command.Parameters.AddWithValue("$dueEndsAtUtc", item.DueEndsAtUtc is null ? DBNull.Value : item.DueEndsAtUtc.Value.ToString("O"));
        command.Parameters.AddWithValue("$timezoneId", item.TimezoneId);
        command.Parameters.AddWithValue("$isCompleted", item.IsCompleted ? 1 : 0);
        command.Parameters.AddWithValue("$onlyThisDay", item.IsOnlyThisDay ? 1 : 0);
        command.Parameters.AddWithValue("$completedAtUtc", item.CompletedAtUtc is null ? DBNull.Value : item.CompletedAtUtc.Value.ToString("O"));
        command.Parameters.AddWithValue("$createdAtUtc", item.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedAtUtc", item.UpdatedAtUtc.ToString("O"));
    }

    private static TaskItem ReadTaskItem(SqliteDataReader reader)
    {
        return new TaskItem
        {
            Id = Guid.Parse(reader.GetString(0)),
            Title = reader.GetString(1),
            Notes = reader.IsDBNull(2) ? null : reader.GetString(2),
            ProjectId = reader.IsDBNull(3) ? null : Guid.Parse(reader.GetString(3)),
            DueAtUtc = reader.IsDBNull(4) ? null : DateTime.Parse(reader.GetString(4), null, DateTimeStyles.RoundtripKind),
            DueEndsAtUtc = reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5), null, DateTimeStyles.RoundtripKind),
            TimezoneId = reader.IsDBNull(6) ? TimeZoneInfo.Local.Id : reader.GetString(6),
            IsCompleted = !reader.IsDBNull(7) && reader.GetInt64(7) == 1,
            IsOnlyThisDay = !reader.IsDBNull(8) && reader.GetInt64(8) == 1,
            CompletedAtUtc = reader.IsDBNull(9) ? null : DateTime.Parse(reader.GetString(9), null, DateTimeStyles.RoundtripKind),
            CreatedAtUtc = DateTime.Parse(reader.GetString(10), null, DateTimeStyles.RoundtripKind),
            UpdatedAtUtc = DateTime.Parse(reader.GetString(11), null, DateTimeStyles.RoundtripKind),
            ArchivedAtUtc = reader.IsDBNull(12)
                ? null
                : DateTime.Parse(reader.GetString(12), null, DateTimeStyles.RoundtripKind)
        };
    }

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static DateTime ConvertEditorTimeToUtc(DateTime value, TimeZoneInfo timezone)
    {
        var normalized = value.Kind == DateTimeKind.Unspecified
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

        return TimeZoneInfo.ConvertTimeToUtc(normalized, timezone);
    }

    private static TimeZoneInfo ResolveTimezone(string timezoneId)
    {
        if (string.IsNullOrWhiteSpace(timezoneId))
        {
            return TimeZoneInfo.Local;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Local;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }
}
