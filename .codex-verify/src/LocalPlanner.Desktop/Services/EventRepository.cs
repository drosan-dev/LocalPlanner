using System;
using System.Collections.Generic;
using System.Globalization;
using LocalPlanner.Desktop.Models;
using Microsoft.Data.Sqlite;

namespace LocalPlanner.Desktop.Services;

public sealed class EventRepository
{
    private readonly string _connectionString;

    public EventRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public IReadOnlyList<CalendarEvent> GetActiveEvents()
    {
        var results = new List<CalendarEvent>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            @"SELECT id, title, description, starts_at_utc, ends_at_utc, timezone_id, is_all_day, rrule_text, created_at_utc, updated_at_utc, deleted_at_utc
            FROM events
            WHERE deleted_at_utc IS NULL
            ORDER BY starts_at_utc ASC;";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadEvent(reader));
        }

        return results;
    }

    public CalendarEvent Save(EventEditorState editorState)
    {
        var nowUtc = DateTime.UtcNow;
        var id = editorState.Id ?? Guid.NewGuid();
        var createdAt = editorState.Id.HasValue ? GetCreatedAt(id) ?? nowUtc : nowUtc;
        var timezone = ResolveTimezone(editorState.TimezoneId);
        var startsAtUtc = ConvertEditorTimeToUtc(editorState.StartLocal, timezone);
        var endsAtUtc = ConvertEditorTimeToUtc(editorState.EndLocal, timezone);

        var calendarEvent = new CalendarEvent
        {
            Id = id,
            Title = editorState.Title.Trim(),
            Description = NormalizeNullable(editorState.Description),
            StartsAtUtc = startsAtUtc,
            EndsAtUtc = endsAtUtc,
            TimezoneId = timezone.Id,
            IsAllDay = editorState.IsAllDay,
            RRuleText = NormalizeNullable(editorState.RRuleText),
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = nowUtc,
            DeletedAtUtc = null
        };

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            @"INSERT INTO events (id, title, description, starts_at_utc, ends_at_utc, timezone_id, is_all_day, rrule_text, created_at_utc, updated_at_utc, deleted_at_utc)
            VALUES ($id, $title, $description, $startsAtUtc, $endsAtUtc, $timezoneId, $isAllDay, $rruleText, $createdAtUtc, $updatedAtUtc, NULL)
            ON CONFLICT(id) DO UPDATE SET
                title = excluded.title,
                description = excluded.description,
                starts_at_utc = excluded.starts_at_utc,
                ends_at_utc = excluded.ends_at_utc,
                timezone_id = excluded.timezone_id,
                is_all_day = excluded.is_all_day,
                rrule_text = excluded.rrule_text,
                updated_at_utc = excluded.updated_at_utc,
                deleted_at_utc = NULL;";
        BindEvent(command, calendarEvent);
        command.ExecuteNonQuery();

        return calendarEvent;
    }

    public bool SoftDelete(Guid id)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            @"UPDATE events
            SET deleted_at_utc = $deletedAtUtc, updated_at_utc = $updatedAtUtc
            WHERE id = $id AND deleted_at_utc IS NULL;";
        var nowUtc = DateTime.UtcNow;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$deletedAtUtc", nowUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedAtUtc", nowUtc.ToString("O"));

        return command.ExecuteNonQuery() > 0;
    }

    private DateTime? GetCreatedAt(Guid id)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT created_at_utc FROM events WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id.ToString());

        var value = command.ExecuteScalar() as string;
        return value is null ? null : DateTime.Parse(value, null, DateTimeStyles.RoundtripKind);
    }

    private static void BindEvent(SqliteCommand command, CalendarEvent calendarEvent)
    {
        command.Parameters.AddWithValue("$id", calendarEvent.Id.ToString());
        command.Parameters.AddWithValue("$title", calendarEvent.Title);
        command.Parameters.AddWithValue("$description", (object?)calendarEvent.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("$startsAtUtc", calendarEvent.StartsAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$endsAtUtc", calendarEvent.EndsAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$timezoneId", calendarEvent.TimezoneId);
        command.Parameters.AddWithValue("$isAllDay", calendarEvent.IsAllDay ? 1 : 0);
        command.Parameters.AddWithValue("$rruleText", (object?)calendarEvent.RRuleText ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAtUtc", calendarEvent.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedAtUtc", calendarEvent.UpdatedAtUtc.ToString("O"));
    }

    private static CalendarEvent ReadEvent(SqliteDataReader reader)
    {
        return new CalendarEvent
        {
            Id = Guid.Parse(reader.GetString(0)),
            Title = reader.GetString(1),
            Description = reader.IsDBNull(2) ? null : reader.GetString(2),
            StartsAtUtc = DateTime.Parse(reader.GetString(3), null, DateTimeStyles.RoundtripKind),
            EndsAtUtc = DateTime.Parse(reader.GetString(4), null, DateTimeStyles.RoundtripKind),
            TimezoneId = reader.GetString(5),
            IsAllDay = reader.GetInt64(6) == 1,
            RRuleText = reader.IsDBNull(7) ? null : reader.GetString(7),
            CreatedAtUtc = DateTime.Parse(reader.GetString(8), null, DateTimeStyles.RoundtripKind),
            UpdatedAtUtc = DateTime.Parse(reader.GetString(9), null, DateTimeStyles.RoundtripKind),
            DeletedAtUtc = reader.IsDBNull(10)
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
        // Timeline drag operates on local device time, which comes through as Kind=Local.
        // Convert that path using the current machine zone to avoid WPF drop-time exceptions.
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
