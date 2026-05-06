using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LocalPlanner.Desktop.Models;
using Microsoft.Data.Sqlite;

namespace LocalPlanner.Desktop.Services;

public sealed class ProjectRepository
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.Ordinal)
    {
        "Active",
        "Paused",
        "Completed"
    };

    private readonly string _connectionString;

    public ProjectRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public IReadOnlyList<Project> GetProjects()
    {
        var results = new List<Project>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            @"SELECT id, title, color, status, description, created_at_utc, updated_at_utc
            FROM projects
            ORDER BY updated_at_utc DESC, title COLLATE NOCASE ASC;";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadProject(reader));
        }

        return results;
    }

    public IReadOnlyList<Tag> GetTags()
    {
        var results = new List<Tag>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            @"SELECT id, name, created_at_utc, updated_at_utc
            FROM tags
            ORDER BY name COLLATE NOCASE ASC;";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadTag(reader));
        }

        return results;
    }

    public IReadOnlyList<Tag> GetProjectTags(Guid projectId)
    {
        var results = new List<Tag>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            @"SELECT tags.id, tags.name, tags.created_at_utc, tags.updated_at_utc
            FROM tags
            INNER JOIN project_tags ON project_tags.tag_id = tags.id
            WHERE project_tags.project_id = $projectId
            ORDER BY tags.name COLLATE NOCASE ASC;";
        command.Parameters.AddWithValue("$projectId", projectId.ToString());

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadTag(reader));
        }

        return results;
    }

    public Project Save(ProjectEditorState editorState)
    {
        var title = editorState.Title.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException("Введите название проекта.");
        }

        var nowUtc = DateTime.UtcNow;
        var id = editorState.Id ?? Guid.NewGuid();
        var createdAt = editorState.Id.HasValue ? GetProjectCreatedAt(id) ?? nowUtc : nowUtc;
        var project = new Project
        {
            Id = id,
            Title = title,
            Color = NormalizeColor(editorState.Color),
            Status = NormalizeStatus(editorState.Status),
            Description = NormalizeNullable(editorState.Description),
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = nowUtc
        };

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                @"INSERT INTO projects (id, title, color, status, description, created_at_utc, updated_at_utc)
                VALUES ($id, $title, $color, $status, $description, $createdAtUtc, $updatedAtUtc)
                ON CONFLICT(id) DO UPDATE SET
                    title = excluded.title,
                    color = excluded.color,
                    status = excluded.status,
                    description = excluded.description,
                    updated_at_utc = excluded.updated_at_utc;";
            BindProject(command, project);
            command.ExecuteNonQuery();
        }

        ReplaceProjectTags(connection, transaction, project.Id, ParseTagNames(editorState.TagsText), nowUtc);
        transaction.Commit();

        return project;
    }

    private DateTime? GetProjectCreatedAt(Guid id)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT created_at_utc FROM projects WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id.ToString());

        var value = command.ExecuteScalar() as string;
        return value is null ? null : DateTime.Parse(value, null, DateTimeStyles.RoundtripKind);
    }

    private static void ReplaceProjectTags(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid projectId,
        IReadOnlyList<string> tagNames,
        DateTime nowUtc)
    {
        using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM project_tags WHERE project_id = $projectId;";
            deleteCommand.Parameters.AddWithValue("$projectId", projectId.ToString());
            deleteCommand.ExecuteNonQuery();
        }

        foreach (var tagName in tagNames)
        {
            var tagId = UpsertTag(connection, transaction, tagName, nowUtc);
            using var linkCommand = connection.CreateCommand();
            linkCommand.Transaction = transaction;
            linkCommand.CommandText =
                @"INSERT OR IGNORE INTO project_tags (project_id, tag_id)
                VALUES ($projectId, $tagId);";
            linkCommand.Parameters.AddWithValue("$projectId", projectId.ToString());
            linkCommand.Parameters.AddWithValue("$tagId", tagId.ToString());
            linkCommand.ExecuteNonQuery();
        }
    }

    private static Guid UpsertTag(SqliteConnection connection, SqliteTransaction transaction, string tagName, DateTime nowUtc)
    {
        using (var selectCommand = connection.CreateCommand())
        {
            selectCommand.Transaction = transaction;
            selectCommand.CommandText = "SELECT id FROM tags WHERE name = $name COLLATE NOCASE LIMIT 1;";
            selectCommand.Parameters.AddWithValue("$name", tagName);

            if (selectCommand.ExecuteScalar() is string existingId)
            {
                return Guid.Parse(existingId);
            }
        }

        var tagId = Guid.NewGuid();
        using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText =
            @"INSERT INTO tags (id, name, created_at_utc, updated_at_utc)
            VALUES ($id, $name, $createdAtUtc, $updatedAtUtc);";
        insertCommand.Parameters.AddWithValue("$id", tagId.ToString());
        insertCommand.Parameters.AddWithValue("$name", tagName);
        insertCommand.Parameters.AddWithValue("$createdAtUtc", nowUtc.ToString("O"));
        insertCommand.Parameters.AddWithValue("$updatedAtUtc", nowUtc.ToString("O"));
        insertCommand.ExecuteNonQuery();

        return tagId;
    }

    private static void BindProject(SqliteCommand command, Project project)
    {
        command.Parameters.AddWithValue("$id", project.Id.ToString());
        command.Parameters.AddWithValue("$title", project.Title);
        command.Parameters.AddWithValue("$color", project.Color);
        command.Parameters.AddWithValue("$status", project.Status);
        command.Parameters.AddWithValue("$description", (object?)project.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAtUtc", project.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedAtUtc", project.UpdatedAtUtc.ToString("O"));
    }

    private static Project ReadProject(SqliteDataReader reader)
    {
        return new Project
        {
            Id = Guid.Parse(reader.GetString(0)),
            Title = reader.GetString(1),
            Color = reader.GetString(2),
            Status = reader.GetString(3),
            Description = reader.IsDBNull(4) ? null : reader.GetString(4),
            CreatedAtUtc = DateTime.Parse(reader.GetString(5), null, DateTimeStyles.RoundtripKind),
            UpdatedAtUtc = DateTime.Parse(reader.GetString(6), null, DateTimeStyles.RoundtripKind)
        };
    }

    private static Tag ReadTag(SqliteDataReader reader)
    {
        return new Tag
        {
            Id = Guid.Parse(reader.GetString(0)),
            Name = reader.GetString(1),
            CreatedAtUtc = DateTime.Parse(reader.GetString(2), null, DateTimeStyles.RoundtripKind),
            UpdatedAtUtc = DateTime.Parse(reader.GetString(3), null, DateTimeStyles.RoundtripKind)
        };
    }

    private static IReadOnlyList<string> ParseTagNames(string? tagsText)
    {
        if (string.IsNullOrWhiteSpace(tagsText))
        {
            return Array.Empty<string>();
        }

        return tagsText
            .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(tag => tag.Trim().TrimStart('#'))
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
    }

    private static string NormalizeColor(string? value)
    {
        var color = string.IsNullOrWhiteSpace(value) ? "#FF4F8DFD" : value.Trim();
        return color.StartsWith('#') && (color.Length == 7 || color.Length == 9)
            ? color
            : "#FF4F8DFD";
    }

    private static string NormalizeStatus(string? value)
    {
        return value is not null && AllowedStatuses.Contains(value)
            ? value
            : "Active";
    }

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
