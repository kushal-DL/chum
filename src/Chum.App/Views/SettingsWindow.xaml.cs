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
    private readonly ConfigFileService _config;
    private readonly DocumentContextService _docContext;
    private TemplateService? _templateService;

    public SettingsWindow()
    {
        InitializeComponent();
        _settings = ((App)Application.Current).Settings;
        _config = ((App)Application.Current).Config;
        _docContext = ((App)Application.Current).DocContext;
        _templateService = ((App)Application.Current).Orchestrator?.GetTemplateService();
        LoadCurrentSettings();
    }

    private void LoadCurrentSettings()
    {
        var s = _settings.Current;

        // Select provider
        foreach (System.Windows.Controls.ComboBoxItem item in ProviderCombo.Items)
        {
            if (item.Tag as string == s.LlmProvider)
                ProviderCombo.SelectedItem = item;
        }
        // Trigger visibility update
        ProviderCombo_SelectionChanged(ProviderCombo, null!);

        // Show key status
        var storedKey = s.LlmProvider == "Anthropic" ? _config.AnthropicApiKey : _config.OpenAiApiKey;
        if (storedKey is not null)
            ShowApiKeyStatus("✓ API key stored", true);

        // API base URL
        ApiBaseUrlBox.Text = s.LlmApiBaseUrl;

        // Ollama inline URL
        OllamaInlineUrlBox.Text = s.OllamaBaseUrl;

        // Model text box
        ModelCombo.Text = s.LlmModel;

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
        SpendThresholdBox.Text = s.SpendThresholdDollars.ToString("G");
        DisclosureReminderBox.IsChecked = s.ShowDisclosureReminder;

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

        UseOnnxWhisperBox.IsChecked = s.UseOnnxWhisper;
        UseOnnxWhisperBox.Checked   += (_, _) => { GpuHintText.Visibility = System.Windows.Visibility.Visible;   CpuHintText.Visibility = System.Windows.Visibility.Collapsed; CpuModelPanel.Visibility = System.Windows.Visibility.Collapsed; };
        UseOnnxWhisperBox.Unchecked += (_, _) => { GpuHintText.Visibility = System.Windows.Visibility.Collapsed; CpuHintText.Visibility = System.Windows.Visibility.Visible;   CpuModelPanel.Visibility = System.Windows.Visibility.Visible; };
        if (!s.UseOnnxWhisper) { GpuHintText.Visibility = System.Windows.Visibility.Collapsed; CpuHintText.Visibility = System.Windows.Visibility.Visible; CpuModelPanel.Visibility = System.Windows.Visibility.Visible; }

        RefreshDocumentList();
    }

    private void SaveApiKey_Click(object sender, RoutedEventArgs e)
    {
        var key = ApiKeyBox.Password.Trim();
        if (string.IsNullOrWhiteSpace(key)) { ShowApiKeyStatus("Key cannot be empty.", false); return; }

        var tag = (ProviderCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string;
        if (tag == "Anthropic")
            _config.AnthropicApiKey = key;
        else
            _config.OpenAiApiKey = key;

        ApiKeyBox.Clear();
        ShowApiKeyStatus("✓ Key saved to config.json", true);
    }

    private async void TestApiKey_Click(object sender, RoutedEventArgs e)
    {
        var tag = (ProviderCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string;
        var key = tag == "Anthropic" ? _config.AnthropicApiKey : _config.OpenAiApiKey;
        if (key is null) { ShowApiKeyStatus("No key stored — save a key first.", false); return; }

        ShowApiKeyStatus("Testing...", true);
        try
        {
            if (tag == "Anthropic")
            {
                var provider = new Chum.Llm.AnthropicLlmProvider(key);
                var sb = new System.Text.StringBuilder();
                await foreach (var tok in provider.StreamResponseAsync(
                    new Chum.Llm.LlmRequest("You are a test assistant.", "Reply with only: OK", MaxTokens: 10)))
                    sb.Append(tok);
                ShowApiKeyStatus($"✓ Connected — replied: {sb}", true);
            }
            else
            {
                var baseUrl = ApiBaseUrlBox.Text.Trim();
                var model = ModelCombo.Text.Trim();
                if (string.IsNullOrEmpty(model)) model = "gpt-4o-mini";
                var provider = new Chum.Llm.OpenAiLlmProvider(key, model, string.IsNullOrEmpty(baseUrl) ? null : baseUrl);
                var sb = new System.Text.StringBuilder();
                await foreach (var tok in provider.StreamResponseAsync(
                    new Chum.Llm.LlmRequest("You are a test assistant.", "Reply with only: OK", MaxTokens: 10)))
                    sb.Append(tok);
                ShowApiKeyStatus($"✓ Connected — replied: {sb}", true);
            }
        }
        catch (Exception ex)
        {
            ShowApiKeyStatus($"✗ Failed: {ex.Message}", false);
        }
    }

    private void ShowApiKeyStatus(string msg, bool success)
    {
        ApiKeyStatus.Text = msg;
        ApiKeyStatus.Foreground = success
            ? System.Windows.Media.Brushes.LightGreen
            : System.Windows.Media.Brushes.OrangeRed;
        ApiKeyStatus.Visibility = System.Windows.Visibility.Visible;
    }

    private void ProviderCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var tag = (ProviderCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string;
        if (ApiUrlPanel is null) return; // called before InitializeComponent

        ApiUrlPanel.Visibility = tag is "OpenAI" or "NVIDIA" or "Custom"
            ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        ApiKeyPanel.Visibility = tag is "Ollama"
            ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        OllamaInlinePanel.Visibility = tag is "Ollama"
            ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        // Pre-fill NVIDIA URL
        if (tag == "NVIDIA" && string.IsNullOrWhiteSpace(ApiBaseUrlBox.Text))
            ApiBaseUrlBox.Text = "https://integrate.api.nvidia.com/v1";

        if (ApiKeyLabel is null) return;
        ApiKeyLabel.Text = tag == "Anthropic" ? "Anthropic API Key" : "API Key";
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        // Save API key if typed
        if (!string.IsNullOrWhiteSpace(ApiKeyBox.Password))
        {
            var tag = (ProviderCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string;
            if (tag == "Anthropic")
                _config.AnthropicApiKey = ApiKeyBox.Password.Trim();
            else
                _config.OpenAiApiKey = ApiKeyBox.Password.Trim();
        }

        string? oldLoopback = _settings.Current.LoopbackDeviceId;
        string? oldMic = _settings.Current.MicDeviceId;

        _settings.Update(s =>
        {
            s.LlmProvider = (ProviderCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string ?? "Anthropic";
            s.LlmApiBaseUrl = ApiBaseUrlBox.Text.Trim();
            s.LlmModel = ModelCombo.Text.Trim().Length > 0 ? ModelCombo.Text.Trim() : s.LlmModel;
            // Update Ollama URL from inline field too
            if (s.LlmProvider == "Ollama")
                s.OllamaBaseUrl = OllamaInlineUrlBox.Text.Trim();

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
            if (decimal.TryParse(SpendThresholdBox.Text, out var thresh) && thresh >= 0)
                s.SpendThresholdDollars = thresh;
            s.ShowDisclosureReminder = DisclosureReminderBox.IsChecked == true;
            if (ActiveTemplateCombo.SelectedItem is string tName)
                s.ActiveTemplateName = tName;
            s.CloudSttFallback = CloudSttFallbackBox.IsChecked == true;
            s.CloudSttModel = CloudSttModelBox.Text.Trim().Length > 0
                ? CloudSttModelBox.Text.Trim() : "whisper-1";
            s.UseOnnxWhisper = UseOnnxWhisperBox.IsChecked == true;
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
        System.Windows.MessageBox.Show(msg, "Chum Settings", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
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

    private void RefreshDocumentList()
    {
        DocumentList.Items.Clear();
        foreach (var doc in _docContext.Documents)
            DocumentList.Items.Add(doc.Name);
    }

    private void AddDocument_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Add Reference Document",
            Filter = "Supported files (*.pdf;*.txt;*.md)|*.pdf;*.txt;*.md|All files (*.*)|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;
        foreach (var path in dlg.FileNames)
        {
            var err = _docContext.AddDocument(path);
            if (err is not null)
                System.Windows.MessageBox.Show(err, "Chum — Add Document", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
        RefreshDocumentList();
    }

    private void RemoveDocument_Click(object sender, RoutedEventArgs e)
    {
        int idx = DocumentList.SelectedIndex;
        if (idx < 0) return;
        _docContext.RemoveAt(idx);
        RefreshDocumentList();
    }
}
