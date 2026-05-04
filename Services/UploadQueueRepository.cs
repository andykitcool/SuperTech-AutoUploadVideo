using Microsoft.Data.Sqlite;
using Supertech.AutoUploadVideo.Models;

namespace Supertech.AutoUploadVideo.Services;

public sealed class UploadQueueRepository
{
    private readonly string _connectionString;

    public UploadQueueRepository(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        Initialize();
    }

    private void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS upload_tasks (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                file_path TEXT NOT NULL UNIQUE,
                file_name TEXT NOT NULL,
                file_size INTEGER NOT NULL,
                activity_id INTEGER NOT NULL,
                program_id INTEGER NULL,
                program_number INTEGER NULL,
                program_name TEXT NULL,
                recorded_at TEXT NULL,
                provider TEXT NULL,
                upload_id TEXT NULL,
                storage_key TEXT NULL,
                resume_record_path TEXT NULL,
                status TEXT NOT NULL,
                progress REAL NOT NULL DEFAULT 0,
                uploaded_bytes INTEGER NOT NULL DEFAULT 0,
                message TEXT NULL,
                created_at TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public List<UploadTaskItem> LoadTasks(int? activityId = null)
    {
        var result = new List<UploadTaskItem>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = activityId.HasValue
            ? "SELECT * FROM upload_tasks WHERE activity_id=$activity_id ORDER BY id DESC LIMIT 500"
            : "SELECT * FROM upload_tasks ORDER BY id DESC LIMIT 500";
        if (activityId.HasValue)
        {
            command.Parameters.AddWithValue("$activity_id", activityId.Value);
        }
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(ReadTask(reader));
        }
        return result;
    }

    public UploadTaskItem? GetByPath(string filePath)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM upload_tasks WHERE file_path=$file_path LIMIT 1";
        command.Parameters.AddWithValue("$file_path", filePath);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadTask(reader) : null;
    }

    public bool Exists(string filePath)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM upload_tasks WHERE file_path=$file_path";
        command.Parameters.AddWithValue("$file_path", filePath);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    public long AddTask(UploadTaskItem task)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO upload_tasks
            (file_path, file_name, file_size, activity_id, program_id, program_number, program_name, recorded_at, status, progress, uploaded_bytes, message, created_at)
            VALUES ($file_path, $file_name, $file_size, $activity_id, $program_id, $program_number, $program_name, $recorded_at, $status, $progress, $uploaded_bytes, $message, $created_at);
            SELECT id FROM upload_tasks WHERE file_path=$file_path;
            """;
        AddParameters(command, task);
        return Convert.ToInt64(command.ExecuteScalar());
    }

    public void UpdateTask(UploadTaskItem task)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE upload_tasks SET
                file_size=$file_size,
                activity_id=$activity_id,
                program_id=$program_id,
                program_number=$program_number,
                program_name=$program_name,
                recorded_at=$recorded_at,
                provider=$provider,
                upload_id=$upload_id,
                storage_key=$storage_key,
                resume_record_path=$resume_record_path,
                status=$status,
                progress=$progress,
                uploaded_bytes=$uploaded_bytes,
                message=$message
            WHERE id=$id
            """;
        AddParameters(command, task);
        command.Parameters.AddWithValue("$id", task.Id);
        command.ExecuteNonQuery();
    }

    private static void AddParameters(SqliteCommand command, UploadTaskItem task)
    {
        command.Parameters.AddWithValue("$file_path", task.FilePath);
        command.Parameters.AddWithValue("$file_name", task.FileName);
        command.Parameters.AddWithValue("$file_size", task.FileSize);
        command.Parameters.AddWithValue("$activity_id", task.ActivityId);
        command.Parameters.AddWithValue("$program_id", (object?)task.ProgramId ?? DBNull.Value);
        command.Parameters.AddWithValue("$program_number", (object?)task.ProgramNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$program_name", (object?)task.ProgramName ?? DBNull.Value);
        command.Parameters.AddWithValue("$recorded_at", task.RecordedAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$provider", (object?)task.Provider ?? DBNull.Value);
        command.Parameters.AddWithValue("$upload_id", (object?)task.UploadId ?? DBNull.Value);
        command.Parameters.AddWithValue("$storage_key", (object?)task.StorageKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$resume_record_path", (object?)task.ResumeRecordPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", task.Status.ToString());
        command.Parameters.AddWithValue("$progress", task.Progress);
        command.Parameters.AddWithValue("$uploaded_bytes", task.UploadedBytes);
        command.Parameters.AddWithValue("$message", (object?)task.Message ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at", task.CreatedAt.ToString("O"));
    }

    private static UploadTaskItem ReadTask(SqliteDataReader reader)
    {
        return new UploadTaskItem
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            FilePath = reader.GetString(reader.GetOrdinal("file_path")),
            FileName = reader.GetString(reader.GetOrdinal("file_name")),
            FileSize = reader.GetInt64(reader.GetOrdinal("file_size")),
            ActivityId = reader.GetInt32(reader.GetOrdinal("activity_id")),
            ProgramId = GetNullableInt(reader, "program_id"),
            ProgramNumber = GetNullableInt(reader, "program_number"),
            ProgramName = GetNullableString(reader, "program_name"),
            RecordedAt = GetNullableDateTime(reader, "recorded_at"),
            Provider = GetNullableString(reader, "provider"),
            UploadId = GetNullableString(reader, "upload_id"),
            StorageKey = GetNullableString(reader, "storage_key"),
            ResumeRecordPath = GetNullableString(reader, "resume_record_path"),
            Status = Enum.TryParse<UploadTaskStatus>(reader.GetString(reader.GetOrdinal("status")), out var status) ? status : UploadTaskStatus.Pending,
            Progress = reader.GetDouble(reader.GetOrdinal("progress")),
            UploadedBytes = reader.GetInt64(reader.GetOrdinal("uploaded_bytes")),
            Message = GetNullableString(reader, "message"),
            CreatedAt = GetNullableDateTime(reader, "created_at") ?? DateTime.Now,
        };
    }

    private static int? GetNullableInt(SqliteDataReader reader, string name)
    {
        var i = reader.GetOrdinal(name);
        return reader.IsDBNull(i) ? null : reader.GetInt32(i);
    }

    private static string? GetNullableString(SqliteDataReader reader, string name)
    {
        var i = reader.GetOrdinal(name);
        return reader.IsDBNull(i) ? null : reader.GetString(i);
    }

    private static DateTime? GetNullableDateTime(SqliteDataReader reader, string name)
    {
        var text = GetNullableString(reader, name);
        return DateTime.TryParse(text, out var dt) ? dt : null;
    }
}
