using System.Globalization;
using System.Windows;
using Supertech.AutoUploadVideo.Models;

namespace Supertech.AutoUploadVideo;

public partial class ManualTaskEditWindow : Window
{
    public int? ProgramNumber { get; private set; }
    public string? ProgramName { get; private set; }
    public DateTime? RecordedAt { get; private set; }

    public ManualTaskEditWindow(UploadTaskItem task)
    {
        InitializeComponent();
        FileNameText.Text = task.FileName;
        ProgramNumberBox.Text = task.ProgramNumber?.ToString("D3") ?? "";
        ProgramNameBox.Text = task.ProgramName ?? "";
        RecordedAtBox.Text = task.RecordedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var numberText = ProgramNumberBox.Text.Trim();
        var name = ProgramNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(numberText) && string.IsNullOrWhiteSpace(name))
        {
            System.Windows.MessageBox.Show("请至少填写节目号或节目名。", "无法保存", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!string.IsNullOrWhiteSpace(numberText))
        {
            if (!int.TryParse(numberText, out var number) || number <= 0)
            {
                System.Windows.MessageBox.Show("节目号必须是大于 0 的数字。", "无法保存", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ProgramNumber = number;
        }

        ProgramName = string.IsNullOrWhiteSpace(name) ? null : name;

        var recordedAtText = RecordedAtBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(recordedAtText))
        {
            var normalized = recordedAtText.Replace("/", "-").Replace("_", "-");
            var formats = new[]
            {
                "yyyy-M-d-H-m-s",
                "yyyy-MM-dd-HH-mm-ss",
                "yyyy-M-d H:m:s",
                "yyyy-MM-dd HH:mm:ss",
            };

            if (!DateTime.TryParseExact(normalized, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var recordedAt)
                && !DateTime.TryParse(recordedAtText, out recordedAt))
            {
                System.Windows.MessageBox.Show("录制时间格式不正确。", "无法保存", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            RecordedAt = recordedAt;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
