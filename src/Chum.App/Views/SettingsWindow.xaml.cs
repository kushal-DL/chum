using System.IO;
using System.Text.Json;
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
    private TemplateService? _templateService;

    public SettingsWindow()
    {
        InitializeComponent();
        _settings = ((App)Application.Current).Settings;
        _credentials = ((App)Application.Current).Credentials;
        _templateService = ((App)Application.Current).Orchestrator?.GetTemplateService();
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
        LocalOnlyModeBox.IsChecked = s.LocalOnlyMode;
        OllamaModelBox.Text = s.OllamaModel;
        OllamaUrlBox.Text = s.OllamaBaseUrl;
        StartWithWindowsBox.IsChecked = s.StartWithWindows;
        StartCapturingBox.IsChecked = s.StartCapturingOnLaunch;
        AutoHideShareBox.IsChecked = s.AutoHideOnScreenShare;
        ExcludeFromCaptureBox.IsChecked = s.ExcludeFromScreenCapture;
        ConfirmScreenCaptureBox.IsChecked = s.ConfirmScreenCapture;
        AutoStartCaptureBox.IsChecked = s.AutoStartCapture;
        UseTeamsCaptionsBox.IsChecked = s.UseTeamsCaptions;
        SpendThresholdBox.Text = s.SpendThresholdDollars.ToString("G");
        DisclosureReminderBox.IsChecked = s.ShowDisclosureReminder;
        CrashReportingBox.IsChecked = s.EnableCrashReporting;

        // Populate template combo
        if (_templateService is not null)
        {
            ActiveTemplateCombo.Items.Clear();
            foreach (var t in _templateService.All)
                ActiveTemplateCombo.Items.Add(t.Name);
            var activeIdx = _templateService.All
                .Select((t, i) => (t, i))
                .FirstOrDefault(x => x.t.Name.Equals(s.ActiveTemplateName, StringComparison.OrdinalIgnoreCase)).i;
            ActiveTemplateCombo.SelectedIndex = activeIdx;
        }
        ActiveTemplateCombo.SelectionChanged += (_, _) => LoadTemplateIntoEditor();

        CloudSttFallbackBox.IsChecked = s.CloudSttFallback;
        CloudSttModelBox.Text = s.CloudSttModel;

        // Show masked key indicator if key is stored
        if (_credentials.GetAnthropicKey() is not null)
        {
            AnthropicKeyStatus.Text = "✓ API key stored";
            AnthropicKeyStatus.Visibility = Visibility.Visible;
        }

        if (_credentials.GetOpenAiKey() is not null)
        {
            OpenAiKeyStatus.Text = "✓ API key stored";
            OpenAiKeyStatus.Visibility = Visibility.Visible;
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

    private void SaveOpenAiKey_Click(object sender, RoutedEventArgs e)
    {
        var key = OpenAiKeyBox.Password.Trim();
        if (string.IsNullOrWhiteSpace(key)) { ShowError("Key cannot be empty."); return; }

        _credentials.SaveOpenAiKey(key);
        OpenAiKeyBox.Clear();
        OpenAiKeyStatus.Text = "✓ Key saved securely";
        OpenAiKeyStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
        OpenAiKeyStatus.Visibility = Visibility.Visible;
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
            s.LocalOnlyMode = LocalOnlyModeBox.IsChecked == true;
            s.OllamaModel = OllamaModelBox.Text.Trim();
            s.OllamaBaseUrl = OllamaUrlBox.Text.Trim();
            s.StartWithWindows = StartWithWindowsBox.IsChecked == true;
            s.StartCapturingOnLaunch = StartCapturingBox.IsChecked == true;
            s.AutoHideOnScreenShare = AutoHideShareBox.IsChecked == true;
            s.ExcludeFromScreenCapture = ExcludeFromCaptureBox.IsChecked == true;
            s.ConfirmScreenCapture = ConfirmScreenCaptureBox.IsChecked == true;
            s.AutoStartCapture = AutoStartCaptureBox.IsChecked == true;
            s.UseTeamsCaptions = UseTeamsCaptionsBox.IsChecked == true;
            if (decimal.TryParse(SpendThresholdBox.Text, out var thresh) && thresh >= 0)
                s.SpendThresholdDollars = thresh;
            s.ShowDisclosureReminder = DisclosureReminderBox.IsChecked == true;
            s.EnableCrashReporting = CrashReportingBox.IsChecked == true;
            if (ActiveTemplateCombo.SelectedItem is string tName)
                s.ActiveTemplateName = tName;
            s.CloudSttFallback = CloudSttFallbackBox.IsChecked == true;
            s.CloudSttModel = CloudSttModelBox.Text.Trim().Length > 0
                ? CloudSttModelBox.Text.Trim() : "whisper-1";
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

    private void ExportSettings_Click(object sender, RoutedEventArgs e)
    {
        var chumDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Chum");
        var settingsPath  = Path.Combine(chumDir, "settings.json");
        var templatesPath = Path.Combine(chumDir, "templates.json");

        var settingsJson  = File.Exists(settingsPath)  ? JsonDocument.Parse(File.ReadAllText(settingsPath)).RootElement  : (JsonElement?)null;
        var templatesJson = File.Exists(templatesPath) ? JsonDocument.Parse(File.ReadAllText(templatesPath)).RootElement : (JsonElement?)null;

        var backup = new
        {
            chumBackupVersion = 1,
            exportedAt = DateTime.UtcNow.ToString("o"),
            settings   = settingsJson,
            templates  = templatesJson
        };

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title      = "Export Chum Settings",
            Filter     = "Chum backup (*.json)|*.json|All files (*.*)|*.*",
            FileName   = $"chum_backup_{DateTime.Now:yyyyMMdd}.json",
            DefaultExt = ".json"
        };
        if (dlg.ShowDialog() != true) return;

        File.WriteAllText(dlg.FileName,
            JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void ImportSettings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Import Chum Settings",
            Filter = "Chum backup (*.json)|*.json|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(dlg.FileName));
            var root = doc.RootElement;

            var chumDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Chum");
            Directory.CreateDirectory(chumDir);

            if (root.TryGetProperty("settings", out var settingsEl) && settingsEl.ValueKind == JsonValueKind.Object)
                File.WriteAllText(Path.Combine(chumDir, "settings.json"),
                    JsonSerializer.Serialize(settingsEl, new JsonSerializerOptions { WriteIndented = true }));

            if (root.TryGetProperty("templates", out var templatesEl) && templatesEl.ValueKind == JsonValueKind.Array)
                File.WriteAllText(Path.Combine(chumDir, "templates.json"),
                    JsonSerializer.Serialize(templatesEl, new JsonSerializerOptions { WriteIndented = true }));

            // Reload settings into memory and refresh UI
            _settings.Load();
            LoadCurrentSettings();

            System.Windows.MessageBox.Show("Settings imported. Some changes take effect on next launch.",
                "Chum — Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Could not read backup file:\n{ex.Message}",
                "Chum — Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var win = new AboutWindow { Owner = this };
        win.ShowDialog();
    }

    private void LoadTemplateIntoEditor()
    {
        if (_templateService is null) return;
        var name = ActiveTemplateCombo.SelectedItem as string;
        var t = _templateService.GetByName(name);
        TemplateNameBox.Text = t?.Name ?? string.Empty;
        TemplateSuffixBox.Text = t?.SystemPromptSuffix ?? string.Empty;
    }

    private void AddTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (_templateService is null) return;
        var name = TemplateNameBox.Text.Trim();
        var suffix = TemplateSuffixBox.Text;
        if (string.IsNullOrWhiteSpace(name)) return;

        var userTemplates = _templateService.All
            .Where(t => Chum.Llm.PromptTemplate.BuiltIns.All(b => !b.Name.Equals(t.Name, StringComparison.OrdinalIgnoreCase)))
            .Where(t => !t.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        userTemplates.Add(new Chum.Llm.PromptTemplate(name, suffix));
        _templateService.Save(userTemplates);

        // Refresh combo
        ActiveTemplateCombo.SelectionChanged -= (_, _) => LoadTemplateIntoEditor();
        ActiveTemplateCombo.Items.Clear();
        foreach (var t in _templateService.All)
            ActiveTemplateCombo.Items.Add(t.Name);
        ActiveTemplateCombo.SelectedItem = name;
        ActiveTemplateCombo.SelectionChanged += (_, _) => LoadTemplateIntoEditor();
    }

    private void DeleteTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (_templateService is null) return;
        var name = ActiveTemplateCombo.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(name)) return;
        if (Chum.Llm.PromptTemplate.BuiltIns.Any(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            System.Windows.MessageBox.Show($"'{name}' is a built-in template and cannot be deleted.", "Chum", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var userTemplates = _templateService.All
            .Where(t => !Chum.Llm.PromptTemplate.BuiltIns.Any(b => b.Name.Equals(t.Name, StringComparison.OrdinalIgnoreCase)))
            .Where(t => !t.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        _templateService.Save(userTemplates);

        ActiveTemplateCombo.SelectionChanged -= (_, _) => LoadTemplateIntoEditor();
        ActiveTemplateCombo.Items.Clear();
        foreach (var t in _templateService.All)
            ActiveTemplateCombo.Items.Add(t.Name);
        ActiveTemplateCombo.SelectedIndex = 0;
        ActiveTemplateCombo.SelectionChanged += (_, _) => LoadTemplateIntoEditor();
        LoadTemplateIntoEditor();
    }
}
