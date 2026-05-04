using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Supertech.AutoUploadVideo.Models;

public sealed class ActivityDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("event_date")] public string? EventDate { get; set; }
    [JsonPropertyName("venue")] public string? Venue { get; set; }
    public override string ToString() => EventDate is { Length: > 0 } ? $"{Name} ({EventDate})" : Name;
}

public sealed class ProgramDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("activity_id")] public int ActivityId { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("sequence_number")] public int SequenceNumber { get; set; }
    [JsonPropertyName("video_status")] public string VideoStatus { get; set; } = "";
    public override string ToString() => $"{SequenceNumber:D3} - {Name}";
}

public sealed class UploadedVideoDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("program_id")] public int ProgramId { get; set; }
    [JsonPropertyName("program_name")] public string ProgramName { get; set; } = "";
    [JsonPropertyName("program_sequence_number")] public int ProgramSequenceNumber { get; set; }
    [JsonPropertyName("filename")] public string Filename { get; set; } = "";
    [JsonPropertyName("file_size")] public long? FileSize { get; set; }
    [JsonPropertyName("recorded_at")] public DateTime? RecordedAt { get; set; }
    [JsonPropertyName("storage_url")] public string? StorageUrl { get; set; }
    [JsonPropertyName("storage_provider")] public string StorageProvider { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("created_at")] public DateTime CreatedAt { get; set; }
}

public sealed class DesktopUploadInitResponse
{
    [JsonPropertyName("upload_id")] public string UploadId { get; set; } = "";
    [JsonPropertyName("storage_key")] public string StorageKey { get; set; } = "";
    [JsonPropertyName("provider")] public string Provider { get; set; } = "";
    [JsonPropertyName("program_id")] public int ProgramId { get; set; }
    [JsonPropertyName("program_name")] public string ProgramName { get; set; } = "";
    [JsonPropertyName("program_sequence_number")] public int ProgramSequenceNumber { get; set; }
    [JsonPropertyName("upload_url")] public string? UploadUrl { get; set; }
    [JsonPropertyName("upload_token")] public string? UploadToken { get; set; }
    [JsonPropertyName("supported")] public bool Supported { get; set; } = true;
    [JsonPropertyName("unsupported_reason")] public string? UnsupportedReason { get; set; }
    [JsonPropertyName("resume_config")] public Dictionary<string, object?> ResumeConfig { get; set; } = new();
}

public sealed class AppSettings
{
    public string ServerUrl { get; set; } = "http://localhost:8000/api";
    public string? AccessToken { get; set; }
    public string WatchFolder { get; set; } = "";
    public int? ActivityId { get; set; }
    public string FileNamePattern { get; set; } = "{节目号}-{节目名}-{录制时间}";
    public bool AutoStartMonitor { get; set; }
}

public sealed class ParsedVideoInfo
{
    public int? ProgramNumber { get; set; }
    public string? ProgramName { get; set; }
    public DateTime? RecordedAt { get; set; }
    public string? RecordedAtText { get; set; }
    public string? Error { get; set; }
}

public enum UploadTaskStatus
{
    Pending,
    WaitingForFile,
    Ready,
    Uploading,
    Paused,
    Success,
    Failed,
    Cancelled,
    NeedsReview
}

public sealed class UploadTaskItem : INotifyPropertyChanged
{
    private UploadTaskStatus _status;
    private double _progress;
    private string? _message;
    private long _uploadedBytes;
    private double _speedBytesPerSecond;

    public long Id { get; set; }
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public long FileSize { get; set; }
    public int ActivityId { get; set; }
    public int? ProgramId { get; set; }
    public int? ProgramNumber { get; set; }
    public string? ProgramName { get; set; }
    public DateTime? RecordedAt { get; set; }
    public string? Provider { get; set; }
    public string? UploadId { get; set; }
    public string? StorageKey { get; set; }
    public string? ResumeRecordPath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public UploadTaskStatus Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public double Progress
    {
        get => _progress;
        set => SetField(ref _progress, value);
    }

    public long UploadedBytes
    {
        get => _uploadedBytes;
        set => SetField(ref _uploadedBytes, value);
    }

    public double SpeedBytesPerSecond
    {
        get => _speedBytesPerSecond;
        set => SetField(ref _speedBytesPerSecond, value);
    }

    public string? Message
    {
        get => _message;
        set => SetField(ref _message, value);
    }

    public string DisplayProgram => ProgramNumber.HasValue ? $"{ProgramNumber.Value:D3} - {ProgramName}" : ProgramName ?? "待匹配";
    public string DisplayProgress => $"{Progress:0}%";
    public string DisplaySize => FileSize <= 0 ? "-" : $"{FileSize / 1024d / 1024d:0.0} MB";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        if (propertyName is nameof(Progress)) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayProgress)));
    }
}
