using Qiniu.Http;
using Qiniu.Storage;
using Supertech.AutoUploadVideo.Models;

namespace Supertech.AutoUploadVideo.Storage;

public sealed record UploadProgress(long UploadedBytes, long TotalBytes, double Percent, double SpeedBytesPerSecond);

public interface ICloudUploader
{
    Task<string?> StartAsync(UploadTaskItem task, DesktopUploadInitResponse init, IProgress<UploadProgress> progress, CancellationToken cancellationToken);
    void Pause();
    void Resume();
    void Abort();
}

public sealed class QiniuUploader : ICloudUploader
{
    private volatile bool _paused;
    private volatile bool _aborted;

    public Task<string?> StartAsync(UploadTaskItem task, DesktopUploadInitResponse init, IProgress<UploadProgress> progress, CancellationToken cancellationToken)
    {
        return Task.Run<string?>(() =>
        {
            if (string.IsNullOrWhiteSpace(init.UploadToken)) throw new InvalidOperationException("缺少 Qiniu 上传凭证");
            if (string.IsNullOrWhiteSpace(init.StorageKey)) throw new InvalidOperationException("缺少云存储 key");

            var startedAt = DateTime.UtcNow;
            var extra = new PutExtra
            {
                MimeType = "video/mp4",
                ResumeRecordFile = task.ResumeRecordPath,
                BlockUploadThreads = 4,
                MaxRetryTimes = 3,
                ProgressHandler = (uploaded, total) =>
                {
                    var seconds = Math.Max(1, (DateTime.UtcNow - startedAt).TotalSeconds);
                    progress.Report(new UploadProgress(uploaded, total, total <= 0 ? 0 : uploaded * 100d / total, uploaded / seconds));
                },
                UploadController = () =>
                {
                    if (cancellationToken.IsCancellationRequested || _aborted) return UploadControllerAction.Aborted;
                    return _paused ? UploadControllerAction.Suspended : UploadControllerAction.Activated;
                },
            };

            var uploader = new ResumableUploader(new Config());
            HttpResult result = uploader.UploadFile(task.FilePath, init.StorageKey, init.UploadToken, extra);
            if (result.Code is >= 200 and < 300)
            {
                progress.Report(new UploadProgress(task.FileSize, task.FileSize, 100, 0));
                return result.Text;
            }
            throw new InvalidOperationException($"Qiniu upload failed: {result.Code} {result.Text}");
        }, cancellationToken);
    }

    public void Pause() => _paused = true;
    public void Resume() => _paused = false;
    public void Abort() => _aborted = true;
}

public sealed class UnsupportedCloudUploader : ICloudUploader
{
    private readonly string _reason;

    public UnsupportedCloudUploader(string reason)
    {
        _reason = reason;
    }

    public Task<string?> StartAsync(UploadTaskItem task, DesktopUploadInitResponse init, IProgress<UploadProgress> progress, CancellationToken cancellationToken)
    {
        throw new NotSupportedException(_reason);
    }

    public void Pause() { }
    public void Resume() { }
    public void Abort() { }
}

public sealed class CloudUploaderFactory
{
    public ICloudUploader Create(DesktopUploadInitResponse init)
    {
        if (!init.Supported)
        {
            return new UnsupportedCloudUploader(init.UnsupportedReason ?? "当前云存储不支持客户端直传");
        }
        return init.Provider switch
        {
            "qiniu" => new QiniuUploader(),
            "aliyun" => new UnsupportedCloudUploader("Aliyun OSS 桌面端直传需要先在服务端配置 STS 临时凭证。"),
            "tencent" => new UnsupportedCloudUploader("Tencent COS 桌面端直传需要先在服务端配置 CAM 临时密钥。"),
            _ => new UnsupportedCloudUploader($"未知云存储厂商：{init.Provider}"),
        };
    }
}
