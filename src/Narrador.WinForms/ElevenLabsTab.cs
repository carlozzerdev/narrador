using System.Security.Cryptography;
using System.Text.Json;
using Narrador.Application;
using Narrador.Domain;

namespace Narrador.WinForms;

public sealed partial class ElevenLabsTab : UserControl, IServiceConfiguration
{
    private readonly ISettingsStore _settings;
    private readonly ISecretStore _secrets;
    private bool _preferencesLoaded;
    private bool _secretsLoaded;
    private bool _selectingVoice;

    public string ProviderId => "elevenlabs";
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string? OutputDirectory { get; set; }

    public ElevenLabsTab(ISettingsStore settings, ISecretStore secrets)
    {
        _settings = settings;
        _secrets = secrets;
        InitializeComponent();
        PopulateVoices(SavedVoice.Defaults);
        _voiceId.Text = new AppSettings().VoiceId;
        _voices.SelectedIndexChanged += (_, _) =>
        {
            if (!_selectingVoice && _voices.SelectedItem is SavedVoice voice) _voiceId.Text = voice.VoiceId;
        };
        _voiceId.TextChanged += (_, _) => SelectMatchingVoice();
        SelectMatchingVoice();
        _showKey.CheckedChanged += (_, _) => _apiKey.UseSystemPasswordChar = !_showKey.Checked;
        _saveSettingsButton.Click += (_, _) =>
        {
            if (TrySavePreferences()) SetStatus("Configuración de ElevenLabs guardada.");
        };
        _loadSettingsButton.Click += (_, _) => LoadPreferences();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (!_preferencesLoaded && !DesignMode) LoadPreferences();
    }

    internal void LoadPreferences()
    {
        try
        {
            var settings = _settings.Load();
            PopulateVoices(settings.Voices);
            _voiceId.Text = settings.VoiceId;
            SelectMatchingVoice();
            _model.SelectedItem = settings.ModelId;
            if (_model.SelectedIndex < 0) _model.SelectedIndex = 0;
            _stability.Value = ClampSetting(settings.Stability, 0.5M);
            _similarity.Value = ClampSetting(settings.SimilarityBoost, 0.75M);
            _speed.Value = double.IsFinite(settings.Speed) ? (decimal)Math.Clamp(settings.Speed, 0.50, 2.00) : 1.00M;
            OutputDirectory = settings.OutputDirectory;
            _rememberKey.Checked = settings.RememberApiKey;
            _preferencesLoaded = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            SetStatus("No se pudo leer la configuración de ElevenLabs.", error: true);
            return;
        }
        try
        {
            var key = _rememberKey.Checked ? _secrets.ReadApiKey(ProviderId) : null;
            _apiKey.Text = key ?? "";
            _secretsLoaded = true;
            SetStatus("Configuración de ElevenLabs cargada.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            _secretsLoaded = false;
            SetStatus("No se pudo recuperar la clave guardada. Introduce de nuevo tu API key.", error: true);
        }
    }

    private void PopulateVoices(IReadOnlyList<SavedVoice>? voices)
    {
        _selectingVoice = true;
        try
        {
            _voices.Items.Clear();
            foreach (var voice in (voices ?? []).Concat(SavedVoice.Defaults)
                .Where(voice => voice is not null && !string.IsNullOrWhiteSpace(voice.Name) && !string.IsNullOrWhiteSpace(voice.VoiceId))
                .DistinctBy(voice => voice.Name, StringComparer.OrdinalIgnoreCase))
                _voices.Items.Add(voice);
        }
        finally { _selectingVoice = false; }
    }

    private void SelectMatchingVoice()
    {
        _selectingVoice = true;
        try
        {
            _voices.SelectedIndex = -1;
            for (var index = 0; index < _voices.Items.Count; index++)
                if (_voices.Items[index] is SavedVoice voice && voice.VoiceId == _voiceId.Text.Trim())
                {
                    _voices.SelectedIndex = index;
                    break;
                }
        }
        finally { _selectingVoice = false; }
    }

    public SpeechRequest ReadConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_apiKey.Text) || string.IsNullOrWhiteSpace(_voiceId.Text))
            throw new NarrationException("Introduce la API key y el ID de voz en la pestaña ElevenLabs.");
        return new SpeechRequest("", _apiKey.Text.Trim(), _voiceId.Text.Trim(), _model.Text,
            new VoiceSettings((double)_stability.Value, (double)_similarity.Value, (double)_speed.Value));
    }

    public bool TrySavePreferences()
    {
        try
        {
            // Preserve an unreadable stored key until the user explicitly replaces it.
            if (_secretsLoaded || !string.IsNullOrEmpty(_apiKey.Text))
                _secrets.SaveApiKey(ProviderId, _rememberKey.Checked ? _apiKey.Text.Trim() : null);
            _settings.Save(new AppSettings(_voiceId.Text.Trim(), _model.Text,
                (double)_stability.Value, (double)_similarity.Value, OutputDirectory,
                _rememberKey.Checked, (double)_speed.Value)
            {
                Voices = _voices.Items.Cast<SavedVoice>().ToArray()
            });
            _preferencesLoaded = true;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            SetStatus("No se pudo guardar la configuración de ElevenLabs.", error: true);
            return false;
        }
    }

    public void SaveOnClose()
    {
        if (_preferencesLoaded) TrySavePreferences();
    }

    private void SetStatus(string text, bool error = false)
    {
        _status.Text = text;
        _status.ForeColor = error ? Color.FromArgb(164, 40, 40) : Color.FromArgb(40, 65, 78);
    }

    private static decimal ClampSetting(double value, decimal fallback) =>
        double.IsFinite(value) ? (decimal)Math.Clamp(value, 0, 1) : fallback;
}
