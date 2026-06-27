using System.Windows;
using Chum.App.Services;

namespace Chum.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settings;
    private readonly CredentialService _credentials;

    public SettingsWindow()
    {
        InitializeComponent();
        _settings = ((App)Application.Current).Settings;
        _credentials = ((App)Application.Current).Credentials;
        LoadCurrentSettings();
    }

    private void LoadCurrentSettings()
    {
        var s = _settings.Current;

        // Populate model combo
        foreach (System.Windows.Controls.ComboBoxItem item in ModelCombo.Items)
        {
            if (item.Tag?.ToString() == s.LlmModel)
                ModelCombo.SelectedItem = item;
        }

        foreach (System.Windows.Controls.ComboBoxItem item in WhisperModelCombo.Items)
        {
            if (item.Tag?.ToString() == s.WhisperModel)
                WhisperModelCombo.SelectedItem = item;
        }

        HoldToAskBox.Text = s.HoldToAskHotkey;
        ScreenCapBox.Text = s.ScreenCaptureHotkey;
        PrivacyPauseBox.Text = s.PrivacyPauseHotkey;
        OpacitySlider.Value = s.OverlayOpacity;
        StartWithWindowsBox.IsChecked = s.StartWithWindows;
        StartCapturingBox.IsChecked = s.StartCapturingOnLaunch;
        AutoHideShareBox.IsChecked = s.AutoHideOnScreenShare;

        // Show masked key indicator if key is stored
        if (_credentials.GetAnthropicKey() is not null)
        {
            AnthropicKeyStatus.Text = "✓ API key stored";
            AnthropicKeyStatus.Visibility = Visibility.Visible;
        }
    }

    private void SaveAnthropicKey_Click(object sender, RoutedEventArgs e)
    {
        var key = AnthropicKeyBox.Password.Trim();
        if (string.IsNullOrWhiteSpace(key)) { ShowError("Key cannot be empty."); return; }

        _credentials.SaveAnthropicKey(key);
        AnthropicKeyBox.Clear();
        AnthropicKeyStatus.Text = "✓ Key saved securely";
        AnthropicKeyStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
        AnthropicKeyStatus.Visibility = Visibility.Visible;
    }

    private async void TestAnthropicKey_Click(object sender, RoutedEventArgs e)
    {
        var key = _credentials.GetAnthropicKey();
        if (key is null) { ShowError("No key stored — save a key first."); return; }

        AnthropicKeyStatus.Text = "Testing...";
        AnthropicKeyStatus.Visibility = Visibility.Visible;

        try
        {
            var provider = new Chum.Llm.AnthropicLlmProvider(key);
            var sb = new System.Text.StringBuilder();
            await foreach (var tok in provider.StreamResponseAsync(
                new Chum.Llm.LlmRequest("You are a test assistant.", "Reply with only: OK", MaxTokens: 10)))
                sb.Append(tok);

            AnthropicKeyStatus.Text = $"✓ Connected — model replied: {sb}";
            AnthropicKeyStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
        }
        catch (Exception ex)
        {
            AnthropicKeyStatus.Text = $"✗ Failed: {ex.Message}";
            AnthropicKeyStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
        }
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings.Update(s =>
        {
            if (ModelCombo.SelectedItem is System.Windows.Controls.ComboBoxItem modelItem)
                s.LlmModel = modelItem.Tag?.ToString() ?? s.LlmModel;

            if (WhisperModelCombo.SelectedItem is System.Windows.Controls.ComboBoxItem whisperItem)
                s.WhisperModel = whisperItem.Tag?.ToString() ?? s.WhisperModel;

            s.HoldToAskHotkey = HoldToAskBox.Text.Trim();
            s.ScreenCaptureHotkey = ScreenCapBox.Text.Trim();
            s.PrivacyPauseHotkey = PrivacyPauseBox.Text.Trim();
            s.OverlayOpacity = OpacitySlider.Value;
            s.StartWithWindows = StartWithWindowsBox.IsChecked == true;
            s.StartCapturingOnLaunch = StartCapturingBox.IsChecked == true;
            s.AutoHideOnScreenShare = AutoHideShareBox.IsChecked == true;
        });

        // Re-register hotkeys immediately
        ((App)Application.Current).ReapplyHotkeys();

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowError(string msg)
    {
        AnthropicKeyStatus.Text = $"✗ {msg}";
        AnthropicKeyStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
        AnthropicKeyStatus.Visibility = Visibility.Visible;
    }
}
