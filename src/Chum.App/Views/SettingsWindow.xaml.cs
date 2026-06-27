using System.Windows;
using Chum.App.Services;
using Chum.Audio.Capture;
using Application = System.Windows.Application;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;

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

        PopulateDeviceCombo(LoopbackDeviceCombo, AudioDeviceEnumerator.GetRenderDevices(), s.LoopbackDeviceId);
        PopulateDeviceCombo(MicDeviceCombo, AudioDeviceEnumerator.GetCaptureDevices(), s.MicDeviceId);

        HoldToAskBox.Text = s.HoldToAskHotkey;
        ScreenCapBox.Text = s.ScreenCaptureHotkey;
        PrivacyPauseBox.Text = s.PrivacyPauseHotkey;
        OpacitySlider.Value = s.OverlayOpacity;
        RetentionSlider.Value = s.TranscriptRetentionMinutes;
        RetentionLabel.Text = $"{s.TranscriptRetentionMinutes} min";
        StartWithWindowsBox.IsChecked = s.StartWithWindows;
        StartCapturingBox.IsChecked = s.StartCapturingOnLaunch;
        AutoHideShareBox.IsChecked = s.AutoHideOnScreenShare;
        ExcludeFromCaptureBox.IsChecked = s.ExcludeFromScreenCapture;

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

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        string? oldLoopback = _settings.Current.LoopbackDeviceId;
        string? oldMic = _settings.Current.MicDeviceId;

        _settings.Update(s =>
        {
            if (ModelCombo.SelectedItem is ComboBoxItem modelItem)
                s.LlmModel = modelItem.Tag?.ToString() ?? s.LlmModel;

            if (WhisperModelCombo.SelectedItem is ComboBoxItem whisperItem)
                s.WhisperModel = whisperItem.Tag?.ToString() ?? s.WhisperModel;

            s.LoopbackDeviceId = (LoopbackDeviceCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            s.MicDeviceId = (MicDeviceCombo.SelectedItem as ComboBoxItem)?.Tag as string;

            s.HoldToAskHotkey = HoldToAskBox.Text.Trim();
            s.ScreenCaptureHotkey = ScreenCapBox.Text.Trim();
            s.PrivacyPauseHotkey = PrivacyPauseBox.Text.Trim();
            s.OverlayOpacity = OpacitySlider.Value;
            s.TranscriptRetentionMinutes = (int)RetentionSlider.Value;
            s.StartWithWindows = StartWithWindowsBox.IsChecked == true;
            s.StartCapturingOnLaunch = StartCapturingBox.IsChecked == true;
            s.AutoHideOnScreenShare = AutoHideShareBox.IsChecked == true;
            s.ExcludeFromScreenCapture = ExcludeFromCaptureBox.IsChecked == true;
        });

        ((App)Application.Current).ReapplyHotkeys();
        ((App)Application.Current).ApplyCaptureExclusionToOverlay();

        bool devicesChanged = _settings.Current.LoopbackDeviceId != oldLoopback
                           || _settings.Current.MicDeviceId != oldMic;
        if (devicesChanged)
            await ((App)Application.Current).ApplyAudioDevicesAsync();

        DialogResult = true;
        Close();
    }

    private static void PopulateDeviceCombo(ComboBox combo, IReadOnlyList<AudioDeviceInfo> devices, string? selectedId)
    {
        combo.Items.Clear();
        var defaultItem = new ComboBoxItem { Content = "Windows Default", Tag = null };
        combo.Items.Add(defaultItem);

        ComboBoxItem? toSelect = null;
        foreach (var dev in devices)
        {
            var item = new ComboBoxItem
            {
                Content = dev.IsDefault ? $"{dev.Name}  (default)" : dev.Name,
                Tag = dev.Id
            };
            combo.Items.Add(item);
            if (dev.Id == selectedId) toSelect = item;
        }

        combo.SelectedItem = toSelect ?? defaultItem;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void RetentionSlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (RetentionLabel is not null)
            RetentionLabel.Text = $"{(int)e.NewValue} min";
    }

    private void ShowError(string msg)
    {
        AnthropicKeyStatus.Text = $"✗ {msg}";
        AnthropicKeyStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
        AnthropicKeyStatus.Visibility = Visibility.Visible;
    }
}
