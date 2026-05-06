using System;
using System.Collections.Generic;
using System.Globalization;
using LocalPlanner.Desktop.Models;
using Microsoft.Data.Sqlite;

namespace LocalPlanner.Desktop.Services;

public sealed class PlanningRepository
{
    private readonly string _connectionString;

    public PlanningRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public IReadOnlyList<PlanningItem> GetActiveItems()
    {
        var results = new List<PlanningItem>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            @"SELECT id, title, notes, project_id, planned_starts_at_utc, planned_ends_at_utc, timezone_id, is_all_day, created_at_utc, updated_at_utc, archived_at_utc
            FROM planning_items
            WHERE archived_at_utc IS NULL
            ORDER BY COALESCE(planned_starts_at_utc, updated_at_utc) ASC, updated_at_utc DESC;";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadPlanningItem(reader));
        }

        return results;
    }

    public PlanningItem? GetById(Guid id)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            @"SELECT id, title, notes, project_id, planned_starts_at_utc, planned_ends_at_utc, timezone_id, is_all_day, created_at_utc, updated_at_utc, archived_at_utc
            FROM planning_items
            WHERE id = $id
            LIMIT 1;";
        command.Parameters.AddWithValue("$id", id.ToString());

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadPlanningItem(reader) : null;
    }

    public PlanningItem Save(PlanningItemEditorState editorState)
    {
        var title = editorState.Title.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException("Введите название плана.");
        }

        var timezone = ResolveTimezone(editorState.TimezoneId);
        DateTime? plannedStartsAtUtc = editorState.PlannedStartLocal is null
            ? null
            : ConvertEditorTimeToUtc(editorState.PlannedStartLocal.Value, timezone);
        DateTime? plannedEndsAtUtc = editorState.PlannedEndLocal is null
            ? null
            : ConvertEditorTimeToUtc(editorState.PlannedEndLocal.Value, timezone);

        if (plannedStartsAtUtc is not null && plannedEndsAtUtc is null)
        {
            throw new InvalidOperationException("Укажите окончание плана.");
        }

        if (plannedStartsAtUtc is null && plannedEndsAtUtc is not null)
        {
            throw new InvalidOperationException("Укажите начало плана.");
        }

        if (plannedStartsAtUtc is not null && plannedEndsAtUtc <= plannedStartsAtUtc)
        {
            throw new InvalidOperationException("Окончание плана должно быть позже начала.");
        }

        var nowUtc = DateTime.UtcNow;
        var id = editorState.Id ?? Guid.NewGuid();
        var createdAt = editorState.Id.HasValue ? GetCreatedAt(id) ?? nowUtc : nowUtc;
        var item = new PlanningItem
        {
            Id = id,
            Title = title,
            Notes = NormalizeNullable(editorState.Notes),
            ProjectId = editorState.ProjectId,
            PlannedStartsAtUtc = plannedStartsAtUtc,
            PlannedEndsAtUtc = plannedEndsAtUtc,
            TimezoneId = timezone.Id,
            IsAllDay = editorState.IsAllDay,
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = nowUtc,
            ArchivedAtUtc = null
        };

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            @"INSERT INTO planning_items (id, title, notes, project_id, planned_starts_at_utc, planned_ends_at_utc, timezone_id, is_all_day, created_at_utc, updated_at_utc, archived_at_utc)
            VALUES ($id, $title, $notes, $projectId, $plannedStartsAtUtc, $plannedEndsAtUtc, $timezoneId, $isAllDay, $createdAtUtc, $updatedAtUtc, NULL)
            ON CONFLICT(id) DO UPDATE SET
                title = excluded.title,
                notes = excluded.notes,
                project_id = excluded.project_id,
                planned_starts_at_utc = excluded.planned_starts_at_utc,
                planned_ends_at_utc = excluded.planned_ends_at_utc,
                timezone_id = excluded.timezone_id,
                is_all_day = excluded.is_all_day,
                updated_at_utc = excluded.updated_at_utc,
                archived_at_utc = NULL;";
        BindItem(command, item);
        command.ExecuteNonQuery();

        return item;
    }

    public bool Archive(Guid id)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            @"UPDATE planning_items
            SET archived_at_utc = $archivedAtUtc, updated_at_utc = $updatedAtUtc
            WHERE id = $id AND archived_at_utc IS NULL;";
        var nowUtc = DateTime.UtcNow;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$archivedAtUtc", nowUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedAtUtc", nowUtc.ToString("O"));
        return command.ExecuteNonQuery() > 0;
    }

    private DateTime? GetCreatedAt(Guid id)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT created_at_utc FROM planning_items WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id.ToString());

        var value = command.ExecuteScalar() as string;
        return value is null ? null : DateTime.Parse(value, null, DateTimeStyles.RoundtripKind);
    }

    private static void BindItem(SqliteCommand command, PlanningItem item)
    {
        command.Parameters.AddWithValue("$id", item.Id.ToString());
        command.Parameters.AddWithValue("$title", item.Title);
        command.Parameters.AddWithValue("$notes", (object?)item.Notes ?? DBNull.Value);
        command.Parameters.AddWithValue("$projectId", item.ProjectId is null ? DBNull.Value : item.ProjectId.Value.ToString());
        command.Parameters.AddWithValue("$plannedStartsAtUtc", item.PlannedStartsAtUtc is null ? DBNull.Value : item.PlannedStartsAtUtc.Value.ToString("O"));
        command.Parameters.AddWithValue("$plannedEndsAtUtc", item.PlannedEndsAtUtc is null ? DBNull.Value : item.PlannedEndsAtUtc.Value.ToString("O"));
        command.Parameters.AddWithValue("$timezoneId", item.TimezoneId);
        command.Parameters.AddWithValue("$isAllDay", item.IsAllDay ? 1 : 0);
        command.Parameters.AddWithValue("$createdAtUtc", item.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedAtUtc", item.UpdatedAtUtc.ToString("O"));
    }

    private static PlanningItem ReadPlanningItem(SqliteDataReader reader)
    {
        return new PlanningItem
        {
            Id = Guid.Parse(reader.GetString(0)),
            Title = reader.GetString(1),
            Notes = reader.IsDBNull(2) ? null : reader.GetString(2),
            ProjectId = reader.IsDBNull(3) ? null : Guid.Parse(reader.GetString(3)),
            PlannedStartsAtUtc = reader.IsDBNull(4) ? null : DateTime.Parse(reader.GetString(4), null, DateTimeStyles.RoundtripKind),
            PlannedEndsAtUtc = reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5), null, DateTimeStyles.RoundtripKind),
            TimezoneId = reader.IsDBNull(6) ? TimeZoneInfo.Local.Id : reader.GetString(6),
            IsAllDay = !reader.IsDBNull(7) && reader.GetInt64(7) == 1,
            CreatedAtUtc = DateTime.Parse(reader.GetString(8), null, DateTimeStyles.RoundtripKind),
            UpdatedAtUtc = DateTime.Parse(reader.GetString(9), null, DateTimeStyles.RoundtripKind),
            ArchivedAtUtc = reader.IsDBNull(10)
                ? null
                : DateTime.Parse(reader.GetString(10), null, DateTimeStyles.RoundtripKind)
        };
    }

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static DateTime ConvertEditorTimeToUtc(DateTime value, TimeZoneInfo timezone)
    {
        if (value.Kind == DateTimeKind.Local)
        {
            return value.ToUniversalTime();
        }

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
