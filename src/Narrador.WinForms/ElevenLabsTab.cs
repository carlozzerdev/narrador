using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Narrador.Application;
using Narrador.Domain;

namespace Narrador.WinForms;

public sealed partial class ElevenLabsTab : UserControl
{
    private const string ProviderId = "elevenlabs";
    private readonly LoadSrt _loadSrt;
    private readonly GenerateSynchronizedNarration _generate;
    private readonly ISettingsStore _settings;
    private readonly ISecretStore _secrets;
    private CancellationTokenSource? _operationCancellation;
    private bool _hasSrt;
    private int _cueCount;
    private string? _lastOutput;
    private bool _preferencesLoaded;
    private bool _secretsLoaded;
    private string? _outputDirectory;
    private IReadOnlyList<SubtitleCue> _cues = [];
    private bool _populatingGrid;

    public bool IsBusy { get; private set; }
    public event EventHandler? BusyChanged;

    public ElevenLabsTab(LoadSrt loadSrt, GenerateSynchronizedNarration generate, ISettingsStore settings, ISecretStore secrets)
    {
        _loadSrt = loadSrt;
        _generate = generate;
        _settings = settings;
        _secrets = secrets;
        InitializeComponent();
        _voiceId.Text = new AppSettings().VoiceId;
        _browseSrt.Click += async (_, _) => await LoadSrtAsync();
        _browseOutput.Click += (_, _) => SelectOutput();
        _generateButton.Click += async (_, _) => await GenerateAsync();
        _cancelButton.Click += (_, _) => CancelOperation();
        _showKey.CheckedChanged += (_, _) => _apiKey.UseSystemPasswordChar = !_showKey.Checked;
        _editor.CellValueChanged += (_, _) =>
        {
            if (!_populatingGrid && !Disposing && !IsDisposed && !_editor.Disposing)
                UpdateSummary();
        };
        _saveSettingsButton.Click += (_, _) =>
        {
            if (TrySavePreferences()) SetStatus("Configuración guardada.");
        };
        _playButton.Click += (_, _) => OpenResult(folder: false);
        _folderButton.Click += (_, _) => OpenResult(folder: true);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_preferencesLoaded || DesignMode) return;
        try
        {
            var settings = _settings.Load();
            _voiceId.Text = settings.VoiceId;
            _stability.Value = ClampSetting(settings.Stability, 0.5M);
            _similarity.Value = ClampSetting(settings.SimilarityBoost, 0.75M);
            _speed.Value = double.IsFinite(settings.Speed) ? (decimal)Math.Clamp(settings.Speed, 0.50, 2.00) : 1.00M;
            _outputDirectory = settings.OutputDirectory;
            _rememberKey.Checked = settings.RememberApiKey;
            _preferencesLoaded = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _preferencesLoaded = true;
            SetStatus("No se pudo leer la configuración. Se utilizan los valores iniciales.", error: true);
        }
        try
        {
            if (_rememberKey.Checked) _apiKey.Text = _secrets.ReadApiKey(ProviderId) ?? "";
            _secretsLoaded = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            SetStatus("No se pudo recuperar la clave guardada. Introduce de nuevo tu API key.", error: true);
        }
    }

    private async Task LoadSrtAsync()
    {
        using var dialog = new OpenFileDialog { Filter = "Subtítulos SRT (*.srt)|*.srt", Title = "Seleccionar narración", CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await LoadSrtFileAsync(dialog.FileName);
    }

    internal async Task LoadSrtFileAsync(string path)
    {
        BeginOperation();
        try
        {
            SetStatus("Leyendo y preparando los subtítulos…");
            var narration = await _loadSrt.ExecuteAsync(path, _operationCancellation!.Token);
            _hasSrt = true;
            _cues = narration.Cues;
            _cueCount = narration.Cues.Count;
            _srtPath.Text = path;
            PopulateSubtitles();
            _outputPath.Text = Path.Combine(
                Directory.Exists(_outputDirectory) ? _outputDirectory : Path.GetDirectoryName(path)!,
                Path.GetFileNameWithoutExtension(path) + ".wav");
            SetStatus("SRT cargado. Revisa el texto y pulsa Generar audio.");
        }
        catch (OperationCanceledException) { SetStatus("Carga cancelada."); }
        catch (Exception ex) { ShowOperationError(ex); }
        finally { EndOperation(); }
    }

    private bool SelectOutput()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "Audio WAV para montaje (*.wav)|*.wav|Audio MP3 (*.mp3)|*.mp3", DefaultExt = "wav", AddExtension = true,
            FilterIndex = Path.GetExtension(_outputPath.Text).Equals(".mp3", StringComparison.OrdinalIgnoreCase) ? 2 : 1,
            FileName = string.IsNullOrWhiteSpace(_outputPath.Text) ? "narracion" : Path.GetFileNameWithoutExtension(_outputPath.Text),
            InitialDirectory = Path.GetDirectoryName(_outputPath.Text),
            Title = "Guardar narración", OverwritePrompt = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        _outputPath.Text = dialog.FileName;
        return true;
    }

    private async Task GenerateAsync()
    {
        if (!_hasSrt || _cues.Count == 0) return;
        if (string.IsNullOrWhiteSpace(_apiKey.Text) || string.IsNullOrWhiteSpace(_voiceId.Text))
        {
            SetStatus("Introduce la API key y el ID de voz antes de generar.", error: true);
            return;
        }
        if (string.IsNullOrWhiteSpace(_outputPath.Text) && !SelectOutput()) return;
        var overwrite = File.Exists(_outputPath.Text);
        if (overwrite && MessageBox.Show(this, "El archivo de destino ya existe. ¿Quieres sustituirlo al finalizar?",
                "Sustituir audio", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        // Preferences are independent of a paid generation; failed persistence does not prevent synthesis.
        TrySavePreferences();
        BeginOperation();
        try
        {
            foreach (DataGridViewRow row in _editor.Rows) row.ErrorText = "";
            ApplyAdjustedTimings(_cues);
            var progress = new Progress<GenerationProgress>(value =>
            {
                _progress.Style = ProgressBarStyle.Continuous;
                _progress.Value = value.TotalChunks == 0 ? 0 : value.CompletedChunks * 100 / value.TotalChunks;
                SetStatus(value.Message);
            });
            _editor.EndEdit();
            var result = await _generate.ExecuteAsync(ProviderId, ReadEditedSubtitles(), _apiKey.Text, _voiceId.Text,
                _model.Text, new VoiceSettings((double)_stability.Value, (double)_similarity.Value, (double)_speed.Value),
                _outputPath.Text, overwrite, progress, _operationCancellation!.Token);
            _lastOutput = result.FilePath;
            if (result.AdjustedCues is not null) ApplyAdjustedTimings(result.AdjustedCues);
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Value = 100;
            var extension = result.AdjustedCues is { Count: > 0 } adjusted ? adjusted[^1].End - _cues[^1].End : TimeSpan.Zero;
            SetStatus($"Audio guardado · {result.Duration:hh\\:mm\\:ss\\.fff} · {result.Chunks} subtítulos · ampliación de tiempos: {extension.TotalSeconds:F3} s.");
        }
        catch (OperationCanceledException) when (_operationCancellation!.IsCancellationRequested)
        {
            SetStatus("Generación cancelada. Los fragmentos ya procesados por el servicio pueden consumir cuota.");
        }
        catch (OperationCanceledException) { SetStatus("ElevenLabs ha tardado demasiado en responder. Puedes volver a intentarlo.", error: true); }
        catch (Exception ex) { ShowOperationError(ex); }
        finally { EndOperation(); }
    }

    public void CancelOperation()
    {
        _operationCancellation?.Cancel();
        _cancelButton.Enabled = false;
        SetStatus("Cancelando la operación…");
    }

    private void BeginOperation()
    {
        _operationCancellation = new CancellationTokenSource();
        IsBusy = true;
        UpdateControls();
        _progress.Value = 0;
        _progress.Style = ProgressBarStyle.Marquee;
        BusyChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EndOperation()
    {
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        IsBusy = false;
        _progress.Style = ProgressBarStyle.Continuous;
        UpdateControls();
        BusyChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateControls()
    {
        if (Disposing || IsDisposed) return;
        _browseSrt.Enabled = !IsBusy;
        _browseOutput.Enabled = !IsBusy;
        _configuration.Enabled = !IsBusy;
        _editor.Enabled = !IsBusy && _hasSrt;
        _saveSettingsButton.Enabled = !IsBusy;
        _generateButton.Enabled = !IsBusy && _hasSrt && _cues.Count > 0;
        _cancelButton.Enabled = IsBusy;
        _playButton.Enabled = !IsBusy && File.Exists(_lastOutput);
        _folderButton.Enabled = !IsBusy && File.Exists(_lastOutput);
    }

    private void UpdateSummary()
    {
        if (Disposing || IsDisposed || _editor.Disposing) return;
        var characters = _editor.Rows.Cast<DataGridViewRow>().Sum(row => row.Cells["CueText"].Value?.ToString()?.Length ?? 0);
        _summary.Text = $"{_cueCount:N0} subtítulos · {characters:N0} caracteres · Los silencios disponibles permiten recuperar los tiempos del SRT.";
        UpdateControls();
    }

    private void PopulateSubtitles()
    {
        _populatingGrid = true;
        try
        {
            _editor.Rows.Clear();
            foreach (var cue in _cues)
                _editor.Rows.Add(cue.Number, cue.Start.ToString(@"hh\:mm\:ss\.fff"),
                    cue.End.ToString(@"hh\:mm\:ss\.fff"), $"{(cue.End - cue.Start).TotalSeconds:F3} s", cue.Text);
        }
        finally { _populatingGrid = false; }
        UpdateSummary();
    }

    internal IReadOnlyList<SubtitleCue> ReadEditedSubtitles()
    {
        var edited = new List<SubtitleCue>();
        for (var i = 0; i < _cues.Count; i++)
        {
            var text = _editor.Rows[i].Cells["CueText"].Value?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                throw new NarrationException($"El subtítulo {_cues[i].Number} no tiene texto. Corrígelo antes de generar.");
            var cue = _cues[i];
            edited.Add(new(cue.Number, cue.Start, cue.End, text));
        }
        return edited.AsReadOnly();
    }

    internal void ApplyAdjustedTimings(IReadOnlyList<SubtitleCue> timeline)
    {
        if (timeline.Count != _cues.Count) throw new ArgumentException("La línea de tiempo no coincide con los subtítulos.", nameof(timeline));
        _populatingGrid = true;
        try
        {
            for (var index = 0; index < timeline.Count; index++)
            {
                var cue = timeline[index];
                var original = _cues[index];
                var row = _editor.Rows[index];
                row.Cells["CueStart"].Value = cue.Start.ToString(@"hh\:mm\:ss\.fff");
                row.Cells["CueEnd"].Value = cue.End.ToString(@"hh\:mm\:ss\.fff");
                row.Cells["CueDuration"].Value = $"{(cue.End - cue.Start).TotalSeconds:F3} s";
                var changed = cue.Start != original.Start || cue.End != original.End;
                foreach (var column in new[] { "CueStart", "CueEnd", "CueDuration" })
                {
                    row.Cells[column].Style.BackColor = changed ? Color.FromArgb(255, 246, 209) : Color.Empty;
                    row.Cells[column].ToolTipText = $"SRT original: {original.Start:hh\\:mm\\:ss\\.fff} → {original.End:hh\\:mm\\:ss\\.fff}";
                }
            }
        }
        finally { _populatingGrid = false; }
        UpdateSummary();
    }

    private bool TrySavePreferences()
    {
        try
        {
            // Do not delete an unreadable stored secret merely because startup could not decrypt it.
            if (_secretsLoaded || _rememberKey.Checked || !string.IsNullOrEmpty(_apiKey.Text))
                _secrets.SaveApiKey(ProviderId, _rememberKey.Checked ? _apiKey.Text.Trim() : null);
            _settings.Save(new AppSettings(_voiceId.Text.Trim(), _model.Text,
                (double)_stability.Value, (double)_similarity.Value,
                string.IsNullOrWhiteSpace(_outputPath.Text) ? _outputDirectory : Path.GetDirectoryName(_outputPath.Text),
                _rememberKey.Checked, (double)_speed.Value));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            SetStatus("No se pudo guardar la configuración en este usuario de Windows.", error: true);
            return false;
        }
    }

    public void SaveOnClose()
    {
        if (_preferencesLoaded) TrySavePreferences();
    }

    private void OpenResult(bool folder)
    {
        if (!File.Exists(_lastOutput))
        {
            SetStatus("El archivo de audio ya no está disponible en su ubicación.", error: true);
            UpdateControls();
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(folder ? Path.GetDirectoryName(_lastOutput)! : _lastOutput!) { UseShellExecute = true });
        }
        catch (Exception) { SetStatus("Windows no pudo abrir el archivo o la carpeta. Comprueba la aplicación predeterminada de audio.", error: true); }
    }

    private void ShowOperationError(Exception ex)
    {
        SetStatus(ex switch
        {
            NarrationException => ex.Message,
            UnauthorizedAccessException => "No tienes permisos para leer o guardar en la ubicación seleccionada.",
            IOException => "No se pudo acceder al archivo. Comprueba la ruta, el espacio disponible y si está abierto en otra aplicación.",
            HttpRequestException => "No se pudo conectar con ElevenLabs. Comprueba la conexión a Internet.",
            _ => "No se pudo completar la operación. Comprueba el SRT y la configuración de audio de Windows."
        }, error: true);
    }

    private void SetStatus(string text, bool error = false)
    {
        _status.Text = text;
        _status.ForeColor = error ? Color.FromArgb(164, 40, 40) : Color.FromArgb(40, 65, 78);
    }

    private static decimal ClampSetting(double value, decimal fallback) =>
        double.IsFinite(value) ? (decimal)Math.Clamp(value, 0, 1) : fallback;
}
