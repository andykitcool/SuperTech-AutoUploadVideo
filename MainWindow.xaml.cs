using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shell;
using Microsoft.Win32;
using Supertech.AutoUploadVideo.Models;
using Supertech.AutoUploadVideo.Services;
using Supertech.AutoUploadVideo.Storage;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfClipboard = System.Windows.Clipboard;
using WpfMessageBox = System.Windows.MessageBox;
using WinForms = System.Windows.Forms;

namespace Supertech.AutoUploadVideo;

public partial class MainWindow : Window
{
    private readonly AppSettingsService _settingsService = new();
    private readonly ApiClient _apiClient = new();
    private readonly FileNameRuleParser _parser = new();
    private readonly VideoMetadataReader _metadataReader = new();
    private readonly FolderMonitorService _monitor = new();
    private readonly CloudUploaderFactory _uploaderFactory = new();
    private readonly UploadQueueRepository _queue;
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly Dictionary<long, ICloudUploader> _activeUploaders = new();
    private readonly Dictionary<long, CancellationTokenSource> _uploadCancellations = new();
    private readonly Dictionary<int, StringBuilder> _activityLogs = new();
    private System.Drawing.Icon? _lastStatusIcon;
    private string _lastStatusIconColor = "";
    private AppSettings _settings;
    private List<ProgramDto> _programs = new();

    public ObservableCollection<UploadTaskItem> Tasks { get; } = new();
    public ObservableCollection<UploadedVideoDto> UploadedVideos { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        _settings = _settingsService.Load();
        _queue = new UploadQueueRepository(_settingsService.DatabasePath);
        _monitor.FileReady += OnFileReadyAsync;
        _notifyIcon = new WinForms.NotifyIcon
        {
            Visible = true,
            Text = "Supertech Auto Upload Video",
            Icon = StatusIconFactory.Create(System.Drawing.Color.Gray),
            ContextMenuStrip = BuildTrayMenu(),
        };
        _notifyIcon.DoubleClick += (_, _) => ShowFromTaskbar();
        LoadSettingsIntoUi();
        ReloadTasksFromQueue();
        SetStatus("停止", WpfBrushes.Gray, TaskbarItemProgressState.None, 0);
    }

    private WinForms.ContextMenuStrip BuildTrayMenu()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("打开主界面", null, (_, _) => Dispatcher.Invoke(ShowFromTaskbar));
        menu.Items.Add("启动/停止监听", null, (_, _) => Dispatcher.Invoke(ToggleMonitor));
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(() =>
        {
            _notifyIcon.Visible = false;
            System.Windows.Application.Current.Shutdown();
        }));
        return menu;
    }

    private void LoadSettingsIntoUi()
    {
        AuthStatusText.Text = string.IsNullOrWhiteSpace(_settings.AccessToken) ? "未登录" : "已登录";
        AuthStatusText.Foreground = string.IsNullOrWhiteSpace(_settings.AccessToken)
            ? WpfBrushes.Goldenrod
            : new SolidColorBrush(System.Windows.Media.Color.FromRgb(204, 251, 241));
        WatchFolderBox.Text = _settings.WatchFolder;
        PatternBox.Text = _settings.FileNamePattern;
        ParseFileNameBeforeUploadBox.IsChecked = _settings.ParseFileNameBeforeUpload;
        _apiClient.Configure(_settings.ServerUrl, _settings.AccessToken);
    }

    private void SaveSettingsFromUi()
    {
        _settings.WatchFolder = WatchFolderBox.Text.Trim();
        _settings.FileNamePattern = PatternBox.Text.Trim();
        _settings.ParseFileNameBeforeUpload = ParseFileNameBeforeUploadBox.IsChecked == true;
        _settings.ActivityId = (ActivityCombo.SelectedItem as ActivityDto)?.Id ?? _settings.ActivityId;
        _settingsService.Save(_settings);
        _apiClient.Configure(_settings.ServerUrl, _settings.AccessToken);
    }

    private void ReloadTasksFromQueue()
    {
        Tasks.Clear();
        foreach (var task in _queue.LoadTasks(_settings.ActivityId)) Tasks.Add(task);
    }

    private void RenderCurrentActivityLog()
    {
        LogBox.Clear();
        if (_settings.ActivityId.HasValue && _activityLogs.TryGetValue(_settings.ActivityId.Value, out var log))
        {
            LogBox.Text = log.ToString();
            LogBox.ScrollToEnd();
        }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_settings.AccessToken))
        {
            await LoadActivitiesAsync();
        }
    }

    private async void ReLogin_Click(object sender, RoutedEventArgs e)
    {
        var loginWindow = new LoginWindow { Owner = this };
        if (loginWindow.ShowDialog() != true) return;

        _settings = _settingsService.Load();
        LoadSettingsIntoUi();
        Log("重新登录成功");
        await LoadActivitiesAsync();
    }

    private async void RefreshActivities_Click(object sender, RoutedEventArgs e)
    {
        await LoadActivitiesAsync();
    }

    private async Task LoadActivitiesAsync()
    {
        SaveSettingsFromUi();
        var activities = await _apiClient.GetActivitiesAsync(CancellationToken.None);
        ActivityCombo.ItemsSource = activities;
        ActivityCombo.SelectedItem = activities.FirstOrDefault(a => a.Id == _settings.ActivityId) ?? activities.FirstOrDefault();
        Log($"活动列表已刷新：{activities.Count} 个");
    }

    private async void ActivityCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ActivityCombo.SelectedItem is not ActivityDto activity) return;
        _settings.ActivityId = activity.Id;
        SaveSettingsFromUi();
        // Prune logs for activities we are no longer viewing (keep only current)
        foreach (var key in _activityLogs.Keys.Where(k => k != activity.Id).ToList())
        {
            _activityLogs.Remove(key);
        }
        ReloadTasksFromQueue();
        RenderCurrentActivityLog();
        await LoadProgramsAsync(activity.Id);
        await RefreshUploadedVideosAsync();
    }

    private async Task LoadProgramsAsync(int activityId)
    {
        try
        {
            _programs = await _apiClient.GetProgramsAsync(activityId, CancellationToken.None);
            Log($"节目列表已刷新：{_programs.Count} 个");
        }
        catch (Exception ex)
        {
            Log($"加载节目失败：{ex.Message}");
        }
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog();
        dialog.Description = "选择 OBS 保存视频的目录";
        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            WatchFolderBox.Text = dialog.SelectedPath;
            SaveSettingsFromUi();
        }
    }

    private void SettingsInput_LostFocus(object sender, RoutedEventArgs e)
    {
        SaveSettingsFromUi();
    }

    private void SettingsCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        SaveSettingsFromUi();
    }

    private async void SaveConfiguration_Click(object sender, RoutedEventArgs e)
    {
        SaveSettingsFromUi();
        var modeText = _settings.ParseFileNameBeforeUpload ? "解析文件名" : "全自动";
        Log($"配置已保存：目录={_settings.WatchFolder}；模式={modeText}；规则={_settings.FileNamePattern}");
        await ReparseReviewTasksAsync(autoUploadReadyTasks: true);
    }

    private void TestRule_Click(object sender, RoutedEventArgs e)
    {
        SaveSettingsFromUi();
        var info = _parser.Parse(PatternBox.Text.Trim(), RuleTestBox.Text.Trim());
        RuleResultText.Text = info.Error ?? FormatParseResult(info);
    }

    private static string FormatParseResult(ParsedVideoInfo info)
    {
        var result = new StringBuilder("解析成功");
        if (info.ProgramNumber.HasValue)
        {
            result.AppendLine();
            result.Append("节目号：").Append(info.ProgramNumber.Value.ToString("D3"));
        }
        if (!string.IsNullOrWhiteSpace(info.ProgramName))
        {
            result.AppendLine();
            result.Append("节目名：").Append(info.ProgramName.Trim());
        }
        if (!string.IsNullOrWhiteSpace(info.RecordedAtText) || info.RecordedAt.HasValue)
        {
            result.AppendLine();
            result.Append("录制时间：").Append(info.RecordedAtText ?? info.RecordedAt?.ToString("HH:mm:ss"));
        }

        return result.ToString();
    }

    private async Task<TaskProgramInfo> CreateTaskInfoAsync(FileInfo file, int activityId, long? excludeTaskId = null)
    {
        if (_settings.ParseFileNameBeforeUpload)
        {
            var parsed = _parser.Parse(_settings.FileNamePattern, file.Name);
            var program = _parser.MatchProgram(parsed, _programs);
            var hasParsedProgram = parsed.ProgramNumber.HasValue || !string.IsNullOrWhiteSpace(parsed.ProgramName);
            if (parsed.Error is not null || !hasParsedProgram)
            {
                return new TaskProgramInfo(parsed, program, UploadTaskStatus.NeedsReview, parsed.Error ?? "未解析到节目号或节目名");
            }

            var message = program is null ? "已解析节目，上传时将在管理端自动创建或更新" : "已匹配节目，等待上传";
            return new TaskProgramInfo(parsed, program, UploadTaskStatus.Ready, message);
        }

        if (_programs.Count == 0)
        {
            await LoadProgramsAsync(activityId);
        }

        var programNumber = GetNextAutoProgramNumber(activityId, excludeTaskId);
        var programCode = programNumber.ToString("D3");
        var metadata = await _metadataReader.ReadRecordedAtAsync(file.FullName);
        var parsedInfo = new ParsedVideoInfo
        {
            ProgramNumber = programNumber,
            ProgramName = programCode,
            RecordedAt = metadata.RecordedAt,
            RecordedAtText = metadata.RecordedAt?.ToString("yyyy-MM-dd HH:mm:ss"),
        };
        var metadataMessage = metadata.RecordedAt.HasValue
            ? $"已读取录制时间 {metadata.RecordedAt:yyyy-MM-dd HH:mm:ss}"
            : metadata.Error ?? "未读取到录制时间";
        return new TaskProgramInfo(
            parsedInfo,
            null,
            UploadTaskStatus.Ready,
            $"全自动模式：已使用编号 {programCode}，{metadataMessage}");
    }

    private int GetNextAutoProgramNumber(int activityId, long? excludeTaskId)
    {
        var maxProgramNumber = _programs
            .Select(p => p.SequenceNumber)
            .DefaultIfEmpty(0)
            .Max();
        var maxTaskNumber = Tasks
            .Where(t => t.ActivityId == activityId && (!excludeTaskId.HasValue || t.Id != excludeTaskId.Value))
            .Select(t => t.ProgramNumber ?? 0)
            .DefaultIfEmpty(0)
            .Max();
        return Math.Max(maxProgramNumber, maxTaskNumber) + 1;
    }

    private sealed record TaskProgramInfo(
        ParsedVideoInfo Parsed,
        ProgramDto? Program,
        UploadTaskStatus Status,
        string Message);

    private async Task ReparseReviewTasksAsync(bool autoUploadReadyTasks)
    {
        if (_settings.ActivityId is null) return;
        if (_programs.Count == 0)
        {
            await LoadProgramsAsync(_settings.ActivityId.Value);
        }

        var readyTasks = new List<UploadTaskItem>();
        var reparsedCount = 0;
        foreach (var task in Tasks.Where(t =>
                     t.ActivityId == _settings.ActivityId.Value
                     && t.Status == UploadTaskStatus.NeedsReview
                     && File.Exists(t.FilePath)).ToList())
        {
            var file = new FileInfo(task.FilePath);
            var taskInfo = await CreateTaskInfoAsync(file, _settings.ActivityId.Value, excludeTaskId: task.Id);
            if (taskInfo.Status != UploadTaskStatus.Ready)
            {
                task.Message = taskInfo.Message;
                _queue.UpdateTask(task);
                continue;
            }

            task.FileName = file.Name;
            task.FileSize = file.Length;
            task.ProgramId = taskInfo.Program?.Id;
            task.ProgramNumber = taskInfo.Parsed.ProgramNumber;
            task.ProgramName = taskInfo.Parsed.ProgramName ?? taskInfo.Program?.Name;
            task.RecordedAt = taskInfo.Parsed.RecordedAt;
            task.Status = UploadTaskStatus.Ready;
            task.Message = _settings.ParseFileNameBeforeUpload
                ? (taskInfo.Program is null ? "已用新规则重新解析，上传时将在管理端自动创建或更新" : "已用新规则重新匹配节目")
                : taskInfo.Message;
            _queue.UpdateTask(task);
            readyTasks.Add(task);
            reparsedCount++;
        }

        if (reparsedCount > 0)
        {
            Log($"已用当前文件名规则重新解析 {reparsedCount} 个待处理任务");
        }

        if (autoUploadReadyTasks)
        {
            foreach (var task in readyTasks)
            {
                await UploadTaskAsync(task);
            }
        }
    }

    private void StartStop_Click(object sender, RoutedEventArgs e) => ToggleMonitor();

    private void ToggleMonitor()
    {
        if (_monitor.IsRunning)
        {
            _monitor.Stop();
            StartStopButton.Content = "启动监听";
            SetStatus("停止", WpfBrushes.Gray, TaskbarItemProgressState.None, 0);
            Log("监听已停止");
            return;
        }

        SaveSettingsFromUi();
        if (!Directory.Exists(_settings.WatchFolder))
        {
            WpfMessageBox.Show("请先选择有效的 OBS 视频目录。", "无法启动", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_settings.ActivityId is null)
        {
            WpfMessageBox.Show("请先登录并选择活动。", "无法启动", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _monitor.Start(_settings.WatchFolder);
        StartStopButton.Content = "停止监听";
        SetStatus("监听中", WpfBrushes.Green, TaskbarItemProgressState.Normal, 0);
        Log($"监听已启动：{_settings.WatchFolder}");
    }

    private async Task OnFileReadyAsync(string path)
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            SaveSettingsFromUi();
            if (_settings.ActivityId is null) return;

            var file = new FileInfo(path);
            var existingTask = _queue.GetByPath(file.FullName);
            if (existingTask is not null)
            {
                if (existingTask.ActivityId == _settings.ActivityId.Value && existingTask.Status == UploadTaskStatus.NeedsReview)
                {
                    var existingTaskInfo = await CreateTaskInfoAsync(file, _settings.ActivityId.Value, excludeTaskId: existingTask.Id);
                    if (existingTaskInfo.Status == UploadTaskStatus.Ready)
                    {
                        existingTask.ProgramId = existingTaskInfo.Program?.Id;
                        existingTask.ProgramNumber = existingTaskInfo.Parsed.ProgramNumber;
                        existingTask.ProgramName = existingTaskInfo.Parsed.ProgramName ?? existingTaskInfo.Program?.Name;
                        existingTask.RecordedAt = existingTaskInfo.Parsed.RecordedAt;
                        existingTask.Status = UploadTaskStatus.Ready;
                        existingTask.Message = _settings.ParseFileNameBeforeUpload
                            ? (existingTaskInfo.Program is null ? "已用当前规则重新解析，上传时将在管理端自动创建或更新" : "已用当前规则重新匹配节目")
                            : existingTaskInfo.Message;
                        _queue.UpdateTask(existingTask);
                        ReloadTasksFromQueue();
                        await UploadTaskAsync(existingTask);
                    }
                }
                return;
            }

            var taskInfo = await CreateTaskInfoAsync(file, _settings.ActivityId.Value);
            var task = new UploadTaskItem
            {
                FilePath = file.FullName,
                FileName = file.Name,
                FileSize = file.Length,
                ActivityId = _settings.ActivityId.Value,
                ProgramId = taskInfo.Program?.Id,
                ProgramNumber = taskInfo.Parsed.ProgramNumber,
                ProgramName = taskInfo.Parsed.ProgramName ?? taskInfo.Program?.Name,
                RecordedAt = taskInfo.Parsed.RecordedAt,
                Status = taskInfo.Status,
                Message = taskInfo.Message,
            };
            task.Id = _queue.AddTask(task);
            Tasks.Insert(0, task);
            _queue.UpdateTask(task);
            Log($"发现新视频：{task.FileName} -> {task.DisplayProgram}");
            if (task.Status == UploadTaskStatus.Ready)
            {
                await UploadTaskAsync(task);
            }
            else
            {
                SetStatus("待处理", WpfBrushes.Goldenrod, TaskbarItemProgressState.Paused, 0);
            }
        });
    }

    private async void UploadTask_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is UploadTaskItem task)
        {
            await UploadTaskAsync(task);
        }
    }

    private async void EditTaskManually_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not UploadTaskItem task) return;
        var window = new ManualTaskEditWindow(task) { Owner = this };
        if (window.ShowDialog() != true) return;

        var program = _parser.MatchProgram(new ParsedVideoInfo
        {
            ProgramNumber = window.ProgramNumber,
            ProgramName = window.ProgramName,
        }, _programs);

        task.ProgramId = program?.Id;
        task.ProgramNumber = window.ProgramNumber;
        task.ProgramName = window.ProgramName ?? program?.Name;
        task.RecordedAt = window.RecordedAt;
        task.Status = UploadTaskStatus.Ready;
        task.Message = program is null ? "已人工补填，上传时将在管理端自动创建或更新" : "已人工补填并匹配节目";
        _queue.UpdateTask(task);
        Log($"人工补填完成：{task.FileName} -> {task.DisplayProgram}");
        await UploadTaskAsync(task);
    }

    private async Task UploadTaskAsync(UploadTaskItem task)
    {
        if (task.ProgramId is null && task.ProgramNumber is null && string.IsNullOrWhiteSpace(task.ProgramName))
        {
            task.Status = UploadTaskStatus.NeedsReview;
            task.Message = "未解析到节目号或节目名，不能上传";
            _queue.UpdateTask(task);
            return;
        }
        if (!File.Exists(task.FilePath))
        {
            task.Status = UploadTaskStatus.Failed;
            task.Message = "本地文件不存在";
            _queue.UpdateTask(task);
            return;
        }

        var cts = new CancellationTokenSource();
        _uploadCancellations[task.Id] = cts;
        try
        {
            task.Status = UploadTaskStatus.Uploading;
            task.Message = "初始化直传凭证";
            _queue.UpdateTask(task);
            SetStatus("上传中", WpfBrushes.DodgerBlue, TaskbarItemProgressState.Normal, task.Progress / 100);

            var init = await _apiClient.InitDesktopUploadAsync(task, cts.Token);
            task.UploadId = init.UploadId;
            task.ProgramId = init.ProgramId > 0 ? init.ProgramId : task.ProgramId;
            task.ProgramNumber = init.ProgramSequenceNumber > 0 ? init.ProgramSequenceNumber : task.ProgramNumber;
            task.ProgramName = !string.IsNullOrWhiteSpace(init.ProgramName) ? init.ProgramName : task.ProgramName;
            task.Provider = init.Provider;
            task.StorageKey = init.StorageKey;
            task.ResumeRecordPath ??= Path.Combine(_settingsService.ResumeDirectory, $"{task.Id}-{init.UploadId}.progress");
            _queue.UpdateTask(task);

            var uploader = _uploaderFactory.Create(init);
            _activeUploaders[task.Id] = uploader;
            var progress = new Progress<UploadProgress>(p =>
            {
                task.UploadedBytes = p.UploadedBytes;
                task.Progress = p.Percent;
                task.SpeedBytesPerSecond = p.SpeedBytesPerSecond;
                task.Message = $"{p.UploadedBytes / 1024d / 1024d:0.0}/{p.TotalBytes / 1024d / 1024d:0.0} MB";
                _queue.UpdateTask(task);
                SetStatus("上传中", WpfBrushes.DodgerBlue, TaskbarItemProgressState.Normal, p.Percent / 100);
            });

            var etag = await uploader.StartAsync(task, init, progress, cts.Token);
            await _apiClient.CompleteDesktopUploadAsync(task, etag, cts.Token);
            task.Status = UploadTaskStatus.Success;
            task.Progress = 100;
            task.Message = "上传完成并已写入数据库";
            _queue.UpdateTask(task);
            Log($"上传完成：{task.FileName}");
            await RefreshUploadedVideosAsync();
            SetIdleStatus();
        }
        catch (OperationCanceledException)
        {
            task.Status = UploadTaskStatus.Cancelled;
            task.Message = "上传已取消";
            _queue.UpdateTask(task);
            SetStatus("已取消", WpfBrushes.Goldenrod, TaskbarItemProgressState.Paused, task.Progress / 100);
        }
        catch (Exception ex)
        {
            task.Status = UploadTaskStatus.Failed;
            task.Message = ex.Message;
            _queue.UpdateTask(task);
            SetStatus("错误", WpfBrushes.Red, TaskbarItemProgressState.Error, task.Progress / 100);
            Log($"上传失败：{task.FileName} - {ex.Message}");
        }
        finally
        {
            _activeUploaders.Remove(task.Id);
            _uploadCancellations.Remove(task.Id);
        }
    }

    private void PauseTask_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not UploadTaskItem task) return;
        if (_activeUploaders.TryGetValue(task.Id, out var uploader))
        {
            uploader.Pause();
            task.Status = UploadTaskStatus.Paused;
            task.Message = "上传已暂停，可点击上传继续";
            _queue.UpdateTask(task);
            SetStatus("暂停", WpfBrushes.Goldenrod, TaskbarItemProgressState.Paused, task.Progress / 100);
        }
    }

    private async void AbortTask_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not UploadTaskItem task) return;
        if (_activeUploaders.TryGetValue(task.Id, out var uploader)) uploader.Abort();
        if (_uploadCancellations.TryGetValue(task.Id, out var cts)) cts.Cancel();
        try { await _apiClient.AbortDesktopUploadAsync(task, CancellationToken.None); } catch { }
        task.Status = UploadTaskStatus.Cancelled;
        task.Message = "上传已人工中断";
        _queue.UpdateTask(task);
        SetStatus("已中断", WpfBrushes.Goldenrod, TaskbarItemProgressState.Paused, task.Progress / 100);
    }

    private async void RefreshVideos_Click(object sender, RoutedEventArgs e) => await RefreshUploadedVideosAsync();

    private async Task RefreshUploadedVideosAsync()
    {
        if (_settings.ActivityId is null) return;
        try
        {
            UploadedVideos.Clear();
            foreach (var item in await _apiClient.GetUploadedVideosAsync(_settings.ActivityId.Value, CancellationToken.None))
            {
                UploadedVideos.Add(item);
            }
        }
        catch (Exception ex)
        {
            Log($"刷新已上传视频失败：{ex.Message}");
        }
    }

    private void OpenVideo_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is UploadedVideoDto video && !string.IsNullOrWhiteSpace(video.StorageUrl))
        {
            Process.Start(new ProcessStartInfo(video.StorageUrl) { UseShellExecute = true });
        }
    }

    private void CopyVideoUrl_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is UploadedVideoDto video && !string.IsNullOrWhiteSpace(video.StorageUrl))
        {
            WpfClipboard.SetText(video.StorageUrl);
        }
    }

    private async void DeleteVideo_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not UploadedVideoDto video) return;
        if (WpfMessageBox.Show($"确定删除视频：{video.Filename}？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await _apiClient.DeleteUploadedVideoAsync(video.Id, CancellationToken.None);
        await RefreshUploadedVideosAsync();
    }

    private void SetIdleStatus()
    {
        if (_monitor.IsRunning) SetStatus("监听中", WpfBrushes.Green, TaskbarItemProgressState.Normal, 0);
        else SetStatus("停止", WpfBrushes.Gray, TaskbarItemProgressState.None, 0);
    }

    private void SetStatus(string text, System.Windows.Media.Brush brush, TaskbarItemProgressState taskbarState, double progress)
    {
        StatusText.Text = text;
        StatusDot.Fill = brush;
        TaskbarInfo.ProgressState = taskbarState;
        TaskbarInfo.ProgressValue = Math.Clamp(progress, 0, 1);
        var color = brush switch
        {
            SolidColorBrush b when b.Color == Colors.Red => System.Drawing.Color.Red,
            SolidColorBrush b when b.Color == Colors.Goldenrod => System.Drawing.Color.Goldenrod,
            SolidColorBrush b when b.Color == Colors.DodgerBlue => System.Drawing.Color.DodgerBlue,
            SolidColorBrush b when b.Color == Colors.Green => System.Drawing.Color.SeaGreen,
            _ => System.Drawing.Color.Gray,
        };
        try
        {
            var colorKey = color.Name;
            if (_lastStatusIconColor != colorKey)
            {
                var newIcon = StatusIconFactory.Create(color);
                _notifyIcon.Icon = newIcon;
                _lastStatusIcon?.Dispose();
                _lastStatusIcon = newIcon;
                _lastStatusIconColor = colorKey;
            }
        }
        catch (Exception ex)
        {
            App.WriteCrashLog("SetStatus-Icon", ex);
        }
        _notifyIcon.Text = $"Supertech Auto Upload Video - {text}";
    }

    private const int MaxLogLinesPerActivity = 500;

    private void Log(string text)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}";
        if (_settings.ActivityId.HasValue)
        {
            if (!_activityLogs.TryGetValue(_settings.ActivityId.Value, out var log))
            {
                log = new StringBuilder();
                _activityLogs[_settings.ActivityId.Value] = log;
            }
            log.Append(line);
            // Trim log if it exceeds max lines to prevent unbounded memory growth
            if (log.Length > MaxLogLinesPerActivity * 80)
            {
                var excess = log.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
                log.Clear();
                foreach (var l in excess.Skip(excess.Length - MaxLogLinesPerActivity))
                {
                    log.AppendLine(l);
                }
            }
        }
        LogBox.AppendText(line);
        LogBox.ScrollToEnd();
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            // Minimize to tray: hide from taskbar, keep tray icon visible
            ShowInTaskbar = false;
            Hide();
        }
    }

    private void ShowFromTaskbar()
    {
        Show();
        WindowState = WindowState.Normal;
        ShowInTaskbar = true;
        Activate();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _monitor.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _lastStatusIcon?.Dispose();
        _lastStatusIcon = null;
    }
}

internal static class StatusIconFactory
{
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static System.Drawing.Icon Create(System.Drawing.Color color)
    {
        using var bitmap = new System.Drawing.Bitmap(32, 32);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.Clear(System.Drawing.Color.Transparent);
        using var brush = new System.Drawing.SolidBrush(color);
        graphics.FillEllipse(brush, 4, 4, 24, 24);
        using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 3);
        graphics.DrawEllipse(pen, 4, 4, 24, 24);

        var hIcon = bitmap.GetHicon();
        try
        {
            // Clone() creates a deep copy that owns its own data,
            // so the icon survives after the bitmap is disposed.
            return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(hIcon).Clone();
        }
        finally
        {
            // Destroy the temporary HICON that FromHandle doesn't own.
            DestroyIcon(hIcon);
        }
    }
}
