using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Supertech.AutoUploadVideo.Models;

namespace Supertech.AutoUploadVideo.Services;

public sealed class FileNameRuleParser
{
    public ParsedVideoInfo Parse(string pattern, string fileName, DateTime? activityDate = null)
    {
        var name = NormalizeFileNameForParsing(fileName);
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return new ParsedVideoInfo { Error = "文件名规则不能为空" };
        }

        var regexPattern = BuildRegexPattern(pattern);

        var match = Regex.Match(name, $"^{regexPattern}$");
        if (!match.Success)
        {
            return new ParsedVideoInfo { Error = $"文件名不符合规则：{pattern}" };
        }

        var parsed = new ParsedVideoInfo();
        if (match.Groups["number"].Success && int.TryParse(match.Groups["number"].Value, out var number))
        {
            parsed.ProgramNumber = number;
        }
        if (match.Groups["name"].Success)
        {
            parsed.ProgramName = match.Groups["name"].Value.Trim();
        }

        var datePart = activityDate?.Date ?? DateTime.Today;
        if (match.Groups["date"].Success)
        {
            var dateText = match.Groups["date"].Value.Replace("_", "-").Replace("/", "-");
            if (DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                datePart = parsedDate.Date;
            }
        }
        if (match.Groups["time"].Success)
        {
            var timeText = match.Groups["time"].Value;
            parsed.RecordedAtText = timeText;
            if (TryParseRecordedAt(timeText, datePart, out var recordedAt))
            {
                parsed.RecordedAt = recordedAt;
            }
            else
            {
                parsed.Error = $"录制时间格式无效：{timeText}";
            }
        }

        return parsed;
    }

    private static string BuildRegexPattern(string pattern)
    {
        var tokenPatterns = new Dictionary<string, string>
        {
            ["{节目号}"] = "(?<number>\\d+)",
            ["{节目名}"] = "(?<name>.+?)",
            ["{录制时间}"] = "(?<time>(?:\\d{4}[-_/]\\d{1,2}[-_/]\\d{1,2}[-_T ]\\d{1,2}[:-]\\d{2}[:-]\\d{2})|(?:\\d{1,2}[:-]\\d{2}[:-]\\d{2}))",
            ["{日期}"] = "(?<date>\\d{4}[-_/]\\d{1,2}[-_/]\\d{1,2})",
        };

        var builder = new StringBuilder();
        var index = 0;
        while (index < pattern.Length)
        {
            var start = pattern.IndexOf('{', index);
            if (start < 0)
            {
                builder.Append(Regex.Escape(pattern[index..]));
                break;
            }

            if (start > index)
            {
                builder.Append(Regex.Escape(pattern[index..start]));
            }

            var end = pattern.IndexOf('}', start);
            if (end < 0)
            {
                builder.Append(Regex.Escape(pattern[start..]));
                break;
            }

            var token = pattern[start..(end + 1)];
            builder.Append(tokenPatterns.TryGetValue(token, out var tokenPattern)
                ? tokenPattern
                : Regex.Escape(token));
            index = end + 1;
        }

        return builder.ToString();
    }

    private static bool TryParseRecordedAt(string text, DateTime datePart, out DateTime recordedAt)
    {
        recordedAt = default;
        var normalized = text.Trim().Replace("/", "-").Replace("_", "-");

        var fullFormats = new[]
        {
            "yyyy-M-d-H-m-s",
            "yyyy-MM-dd-HH-mm-ss",
            "yyyy-M-d-HH-mm-ss",
            "yyyy-MM-dd-H-m-s",
            "yyyy-M-d H:m:s",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-M-dTH:m:s",
            "yyyy-MM-ddTHH:mm:ss",
        };

        if (DateTime.TryParseExact(normalized, fullFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out recordedAt))
        {
            return true;
        }

        var timeText = normalized.Replace("-", ":");
        if (TimeSpan.TryParse(timeText, CultureInfo.InvariantCulture, out var time))
        {
            recordedAt = datePart.Add(time);
            return true;
        }

        return false;
    }

    private static string NormalizeFileNameForParsing(string fileName)
    {
        var name = fileName.Trim();
        var slashIndex = Math.Max(name.LastIndexOf('\\'), name.LastIndexOf('/'));
        if (slashIndex >= 0)
        {
            name = name[(slashIndex + 1)..];
        }

        if (name.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            return name[..^4];
        }

        return name;
    }

    public ProgramDto? MatchProgram(ParsedVideoInfo info, IEnumerable<ProgramDto> programs)
    {
        var list = programs.ToList();
        if (info.ProgramNumber.HasValue)
        {
            var byNumber = list.FirstOrDefault(p => p.SequenceNumber == info.ProgramNumber.Value);
            if (byNumber != null) return byNumber;
        }
        if (!string.IsNullOrWhiteSpace(info.ProgramName))
        {
            return list.FirstOrDefault(p => string.Equals(p.Name.Trim(), info.ProgramName.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        return null;
    }
}
