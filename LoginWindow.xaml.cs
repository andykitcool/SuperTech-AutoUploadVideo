using System.Windows;
using System.Windows.Input;
using Supertech.AutoUploadVideo.Services;

namespace Supertech.AutoUploadVideo;

public partial class LoginWindow : Window
{
    private readonly AppSettingsService _settingsService = new();
    private readonly ApiClient _apiClient = new();

    public LoginWindow()
    {
        InitializeComponent();
        var settings = _settingsService.Load();
        ServerUrlBox.Text = settings.ServerUrl;
        UsernameBox.Focus();
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        await LoginAsync();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        ServerSettingsPanel.Visibility = ServerSettingsPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private async Task LoginAsync()
    {
        var serverUrl = ServerUrlBox.Text.Trim();
        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(serverUrl) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            MessageText.Text = "请填写服务器地址、用户名和密码。";
            return;
        }

        LoginButton.IsEnabled = false;
        MessageText.Text = "正在登录...";
        try
        {
            _apiClient.Configure(serverUrl, null);
            var token = await _apiClient.LoginAsync(username, password, CancellationToken.None);
            var settings = _settingsService.Load();
            settings.ServerUrl = serverUrl.TrimEnd('/');
            settings.AccessToken = token;
            _settingsService.Save(settings);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageText.Text = $"登录失败：{ex.Message}";
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void PasswordBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await LoginAsync();
        }
    }
}
