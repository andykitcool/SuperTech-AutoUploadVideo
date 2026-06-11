using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Supertech.AutoUploadVideo.Services;

public sealed record VideoMetadataResult(DateTime? RecordedAt, string? Error);

public sealed class VideoMetadataReader
{
    private static readonly string[] RecordedAtTagNames =
    {
        "creation_time",
        "com.apple.quicktime.creationdate",
        "date",
        "datetime",
        "encoded_date",
        "recorded_at",
    };

    private static readonly string[] DateTimeFormats =
    {
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH-mm-ss",
        "yyyy-MM-dd_HH-mm-ss",
        "yyyy:MM:dd HH:mm:ss",
        "yyyy-M-d H:m:s",
    };

    private static string ResolveFfprobePath()
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var localPath = Path.Combine(appDir, "tools", "ffprobe.exe");
        if (File.Exists(localPath)) return localPath;

        localPath = Path.Combine(appDir, "ffprobe.exe");
        if (File.Exists(localPath)) return localPath;

        return "ffprobe";
    }

    public async Task<VideoMetadataResult> ReadRecordedAtAsync(string filePath, CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

        Process? process = null;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ResolveFfprobePath(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-v");
            startInfo.ArgumentList.Add("quiet");
            startInfo.ArgumentList.Add("-print_format");
            startInfo.ArgumentList.Add("json");
            startInfo.ArgumentList.Add("-show_format");
            startInfo.ArgumentList.Add("-show_streams");
            startInfo.ArgumentList.Add(filePath);

            process = Process.Start(startInfo);
            if (process is null)
            {
                return new VideoMetadataResult(null, "无法启动 FFprobe");
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(timeoutCts.Token);
            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                return new VideoMetadataResult(null, string.IsNullOrWhiteSpace(error) ? "FFprobe 读取失败" : error.Trim());
            }

            var recordedAt = TryReadRecordedAtFromJson(output);
            return recordedAt.HasValue
                ? new VideoMetadataResult(recordedAt, null)
                : new VideoMetadataResult(null, "未找到视频内嵌录制时间");
        }
        catch (Win32Exception)
        {
            return new VideoMetadataResult(null, "未找到 FFprobe，请确认 ffprobe.exe 已加入 PATH 或放在程序目录");
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (process is { HasExited: false }) process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Ignore cleanup failures after timeout.
            }

            return new VideoMetadataResult(null, "FFprobe 读取超时");
        }
        catch (Exception ex)
        {
            return new VideoMetadataResult(null, $"FFprobe 读取异常：{ex.Message}");
        }
    }

    private static DateTime? TryReadRecordedAtFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("format", out var format)
            && TryReadRecordedAtFromTags(format, out var formatRecordedAt))
        {
            return formatRecordedAt;
        }

        if (root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
        {
            foreach (var stream in streams.EnumerateArray())
            {
                if (TryReadRecordedAtFromTags(stream, out var streamRecordedAt))
                {
                    return streamRecordedAt;
                }
            }
        }

        return null;
    }

    private static bool TryReadRecordedAtFromTags(JsonElement container, out DateTime recordedAt)
    {
        recordedAt = default;
        if (!container.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var tagName in RecordedAtTagNames)
        {
            foreach (var tag in tags.EnumerateObject())
            {
                if (!string.Equals(tag.Name, tagName, StringComparison.OrdinalIgnoreCase)) continue;
                if (tag.Value.ValueKind != JsonValueKind.String) continue;
                if (TryParseMetadataDateTime(tag.Value.GetString(), out recordedAt))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryParseMetadataDateTime(string? value, out DateTime recordedAt)
    {
        recordedAt = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var text = value.Trim();
        if (text.StartsWith("UTC ", StringComparison.OrdinalIgnoreCase))
        {
            text = text[4..].Trim();
            if (DateTime.TryParseExact(text, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out recordedAt))
            {
                recordedAt = recordedAt.ToLocalTime();
                return true;
            }
        }

        if (HasTimeZoneHint(text)
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var offset))
        {
            recordedAt = offset.LocalDateTime;
            return true;
        }

        if (DateTime.TryParseExact(text, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out recordedAt))
        {
            recordedAt = DateTime.SpecifyKind(recordedAt, DateTimeKind.Local);
            return true;
        }

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out recordedAt))
        {
            recordedAt = DateTime.SpecifyKind(recordedAt, DateTimeKind.Local);
            return true;
        }

        return false;
    }

    private static bool HasTimeZoneHint(string text)
    {
        if (text.EndsWith("Z", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Contains("GMT", StringComparison.OrdinalIgnoreCase)) return true;
        var timeSeparator = Math.Max(text.LastIndexOf('T'), text.LastIndexOf(' '));
        if (timeSeparator < 0 || text.Length - timeSeparator < 5) return false;

        var compactSuffix = text[^5..];
        if ((compactSuffix[0] == '+' || compactSuffix[0] == '-')
            && char.IsDigit(compactSuffix[1])
            && char.IsDigit(compactSuffix[2])
            && char.IsDigit(compactSuffix[3])
            && char.IsDigit(compactSuffix[4]))
        {
            return true;
        }

        if (text.Length - timeSeparator < 6) return false;
        var colonSuffix = text[^6..];
        return (colonSuffix[0] == '+' || colonSuffix[0] == '-')
               && char.IsDigit(colonSuffix[1])
               && char.IsDigit(colonSuffix[2])
               && colonSuffix[3] == ':'
               && char.IsDigit(colonSuffix[4])
               && char.IsDigit(colonSuffix[5]);
    }
}
