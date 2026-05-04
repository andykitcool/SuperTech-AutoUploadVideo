using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Supertech.AutoUploadVideo.Models;

namespace Supertech.AutoUploadVideo.Services;

public sealed class ApiClient
{
    private readonly HttpClient _http = new();
    private string _baseUrl = "http://localhost:8000/api";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public void Configure(string baseUrl, string? token)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _http.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(token)
            ? null
            : new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<string> LoginAsync(string username, string password, CancellationToken cancellationToken)
    {
        var response = await _http.PostAsJsonAsync($"{_baseUrl}/admin/login", new { username, password }, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return doc.RootElement.GetProperty("access_token").GetString() ?? "";
    }

    public async Task<List<ActivityDto>> GetActivitiesAsync(CancellationToken cancellationToken)
    {
        return await _http.GetFromJsonAsync<List<ActivityDto>>($"{_baseUrl}/admin/activities", JsonOptions, cancellationToken) ?? new();
    }

    public async Task<List<ProgramDto>> GetProgramsAsync(int activityId, CancellationToken cancellationToken)
    {
        return await _http.GetFromJsonAsync<List<ProgramDto>>($"{_baseUrl}/admin/activities/{activityId}/programs", JsonOptions, cancellationToken) ?? new();
    }

    public async Task<DesktopUploadInitResponse> InitDesktopUploadAsync(UploadTaskItem task, CancellationToken cancellationToken)
    {
        var payload = new
        {
            activity_id = task.ActivityId,
            program_id = task.ProgramId,
            program_number = task.ProgramNumber,
            program_name = task.ProgramName,
            filename = task.FileName,
            file_size = task.FileSize,
            recorded_at = task.RecordedAt,
            source = "supertech-AutoUploadVideo",
        };
        var response = await _http.PostAsJsonAsync($"{_baseUrl}/upload/desktop/init", payload, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DesktopUploadInitResponse>(JsonOptions, cancellationToken) ?? new DesktopUploadInitResponse();
    }

    public async Task<UploadedVideoDto> CompleteDesktopUploadAsync(UploadTaskItem task, string? etag, CancellationToken cancellationToken)
    {
        var payload = new
        {
            upload_id = task.UploadId,
            activity_id = task.ActivityId,
            program_id = task.ProgramId,
            program_number = task.ProgramNumber,
            program_name = task.ProgramName,
            storage_key = task.StorageKey,
            filename = task.FileName,
            file_size = task.FileSize,
            etag,
            recorded_at = task.RecordedAt,
            source = "supertech-AutoUploadVideo",
        };
        var response = await _http.PostAsJsonAsync($"{_baseUrl}/upload/desktop/complete", payload, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UploadedVideoDto>(JsonOptions, cancellationToken) ?? new UploadedVideoDto();
    }

    public async Task AbortDesktopUploadAsync(UploadTaskItem task, CancellationToken cancellationToken)
    {
        var payload = new { upload_id = task.UploadId, storage_key = task.StorageKey, reason = "client-abort" };
        var response = await _http.PostAsJsonAsync($"{_baseUrl}/upload/desktop/abort", payload, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<UploadedVideoDto>> GetUploadedVideosAsync(int activityId, CancellationToken cancellationToken)
    {
        return await _http.GetFromJsonAsync<List<UploadedVideoDto>>($"{_baseUrl}/upload/desktop/videos?activity_id={activityId}", JsonOptions, cancellationToken) ?? new();
    }

    public async Task DeleteUploadedVideoAsync(int videoId, CancellationToken cancellationToken)
    {
        var response = await _http.DeleteAsync($"{_baseUrl}/upload/desktop/videos/{videoId}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
