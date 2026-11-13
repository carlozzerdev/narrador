using System.Diagnostics;
using Narrador.Application;
using Narrador.Domain;

namespace Narrador.WinForms;

public sealed class AudioGenerationPanel : UserControl
{
    private readonly SubtitleEditor _subtitles;
    private readonly GenerateSynchronizedNarration _generate;
    private readonly Func<IServiceConfiguration?> _getService;
    private readonly TextBox _outputPath;
    private readonly Button _browse;
    private readonly Button _generateButton;
    private readonly Button _cancel;
    private readonly Button _play;
    private readonly Button _folder;
    private readonly ProgressBar _progress;
    private readonly Label _status;
    private CancellationTokenSource? _operationCancellation;
    private string? _sourcePath;
    private string? _lastOutput;
    private bool _videoBusy;

    public bool IsBusy => _operationCancellation is not null;
    public string? LastOutputPath => _lastOutput;
    public event EventHandler? BusyChanged;
    public event EventHandler? AudioGenerated;

    public AudioGenerationPanel(SubtitleEditor subtitles, GenerateSynchronizedNarration generate, Func<IServiceConfiguration?> getService)
    {
        _subtitles = subtitles;
        _generate = generate;
        _getService = getService;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 14));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        var output = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        output.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        output.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
        output.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        output.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var label = new Label { Text = "Archivo de audio · WAV recomendado · MP3 disponible", AutoSize = true, Anchor = AnchorStyles.Left };
        output.Controls.Add(label, 0, 0);
        output.SetColumnSpan(label, 2);
        _outputPath = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Name = "OutputPath", AccessibleName = "Archivo de salida" };
        _browse = new Button { Dock = DockStyle.Fill, Text = "Guardar como…" };
        output.Controls.Add(_outputPath, 0, 1);
        output.Controls.Add(_browse, 1, 1);
        root.Controls.Add(output, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        _generateButton = ActionButton("Generar audio", 150);
        _generateButton.BackColor = Color.FromArgb(0, 108, 129);
        _generateButton.ForeColor = Color.White;
        _generateButton.FlatStyle = FlatStyle.Flat;
        _generateButton.FlatAppearance.BorderSize = 0;
        _cancel = ActionButton("Cancelar", 95);
        _play = ActionButton("Reproducir audio", 145);
        _folder = ActionButton("Abrir carpeta", 125);
        actions.Controls.AddRange([_generateButton, _cancel, _play, _folder]);
        root.Controls.Add(actions, 0, 1);
        _progress = new ProgressBar { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100, Margin = new Padding(3, 1, 3, 1) };
        root.Controls.Add(_progress, 0, 2);
        _status = new Label { Text = "Carga y revisa el SRT en el editor compartido.", Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, AccessibleName = "Estado de generación" };
        root.Controls.Add(_status, 0, 3);
        Controls.Add(root);
        _browse.Click += (_, _) => SelectOutput();
        _generateButton.Click += async (_, _) => await GenerateAsync();
        _cancel.Click += (_, _) => CancelOperation();
        _play.Click += (_, _) => OpenResult(folder: false);
        _folder.Click += (_, _) => OpenResult(folder: true);
        _subtitles.Changed += (_, _) =>
        {
            if (!IsBusy && _subtitles.SourcePath is { } path && path != _sourcePath)
            {
                _sourcePath = path;
                var directory = _getService()?.OutputDirectory;
                _outputPath.Text = Path.Combine(Directory.Exists(directory) ? directory : Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + ".wav");
                RememberOutputDirectory();
            }
            UpdateControls();
        };
        _subtitles.BusyChanged += (_, _) => UpdateControls();
        UpdateControls();
    }

    private static Button ActionButton(string text, int width) => new() { Text = text, Width = width, Height = 35, Margin = new Padding(0, 3, 8, 0) };

    private bool SelectOutput()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "Audio WAV para montaje (*.wav)|*.wav|Audio MP3 (*.mp3)|*.mp3", DefaultExt = "wav", AddExtension = true,
            FilterIndex = Path.GetExtension(_outputPath.Text).Equals(".mp3", StringComparison.OrdinalIgnoreCase) ? 2 : 1,
            FileName = string.IsNullOrWhiteSpace(_outputPath.Text) ? "narracion" : Path.GetFileNameWithoutExtension(_outputPath.Text),
            InitialDirectory = Path.GetDirectoryName(_outputPath.Text), Title = "Guardar narración", OverwritePrompt = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        _outputPath.Text = dialog.FileName;
        RememberOutputDirectory();
        return true;
    }

    private void RememberOutputDirectory()
    {
        if (_getService() is { } service && !string.IsNullOrWhiteSpace(_outputPath.Text))
            service.OutputDirectory = Path.GetDirectoryName(_outputPath.Text);
    }

    public void ServiceChanged() => UpdateControls();

    public void SetVideoBusy(bool busy)
    {
        _videoBusy = busy;
        UpdateControls();
    }

    internal async Task GenerateAsync()
    {
        if (IsBusy || _videoBusy || !_subtitles.HasSubtitles || _subtitles.IsBusy) return;
        IReadOnlyList<SubtitleCue> cues;
        SpeechRequest configuration;
        var service = _getService();
        if (service is null) { SetStatus("Selecciona un servicio de voz.", true); return; }
        try { cues = _subtitles.ReadEditedSubtitles(); configuration = service.ReadConfiguration(); }
        catch (NarrationException ex) { SetStatus(ex.Message, true); return; }
        if (string.IsNullOrWhiteSpace(_outputPath.Text) && !SelectOutput()) return;
        var overwrite = File.Exists(_outputPath.Text);
        if (overwrite && MessageBox.Show(this, "El archivo de destino ya existe. ¿Quieres sustituirlo al finalizar?",
                "Sustituir audio", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        RememberOutputDirectory();
        service.TrySavePreferences();
        _operationCancellation = new CancellationTokenSource();
        var operation = _operationCancellation;
        _subtitles.SetGenerationBusy(true);
        UpdateControls();
        _progress.Value = 0;
        _progress.Style = ProgressBarStyle.Marquee;
        BusyChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            var progress = new Progress<GenerationProgress>(value =>
            {
                if (_operationCancellation != operation || IsDisposed || Disposing) return;
                _progress.Style = ProgressBarStyle.Continuous;
                _progress.Value = value.TotalChunks == 0 ? 0 : value.CompletedChunks * 100 / value.TotalChunks;
                SetStatus(value.Message);
            });
            var result = await _generate.ExecuteAsync(service.ProviderId, cues, configuration.ApiKey, configuration.VoiceId,
                configuration.ModelId, configuration.Settings, _outputPath.Text, overwrite, progress, operation.Token);
            _lastOutput = result.FilePath;
            AudioGenerated?.Invoke(this, EventArgs.Empty);
            if (result.AdjustedCues is not null) _subtitles.ApplyAdjustedTimings(result.AdjustedCues);
            _progress.Value = 100;
            var extension = result.AdjustedCues is { Count: > 0 } adjusted ? adjusted[^1].End - cues[^1].End : TimeSpan.Zero;
            SetStatus($"Audio guardado · {result.Duration:hh\\:mm\\:ss\\.fff} · {result.Chunks} subtítulos · ampliación de tiempos: {extension.TotalSeconds:F3} s.");
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            SetStatus("Generación cancelada. Los fragmentos ya procesados por el servicio pueden consumir cuota.");
        }
        catch (OperationCanceledException) { SetStatus("El servicio ha tardado demasiado en responder. Puedes volver a intentarlo.", true); }
        catch (Exception ex)
        {
            SetStatus(ex switch
            {
                NarrationException => ex.Message,
                UnauthorizedAccessException => "No tienes permisos para leer o guardar en la ubicación seleccionada.",
                IOException => "No se pudo acceder al archivo. Comprueba la ruta y el espacio disponible.",
                HttpRequestException => "No se pudo conectar con el servicio de voz. Comprueba la conexión a Internet.",
                _ => "No se pudo completar la operación. Comprueba el SRT y la configuración de audio de Windows."
            }, true);
        }
        finally
        {
            operation.Dispose();
            _operationCancellation = null;
            _subtitles.SetGenerationBusy(false);
            _progress.Style = ProgressBarStyle.Continuous;
            UpdateControls();
            BusyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void CancelOperation()
    {
        _operationCancellation?.Cancel();
        _cancel.Enabled = false;
        SetStatus("Cancelando la operación…");
    }

    private void UpdateControls()
    {
        if (Disposing || IsDisposed) return;
        _browse.Enabled = !IsBusy && !_videoBusy;
        _generateButton.Enabled = !IsBusy && !_videoBusy && !_subtitles.IsBusy && _subtitles.HasSubtitles && _getService() is not null;
        _cancel.Enabled = IsBusy;
        _play.Enabled = !IsBusy && File.Exists(_lastOutput);
        _folder.Enabled = !IsBusy && File.Exists(_lastOutput);
    }

    private void OpenResult(bool folder)
    {
        if (!File.Exists(_lastOutput)) { SetStatus("El archivo de audio ya no está disponible.", true); UpdateControls(); return; }
        try { Process.Start(new ProcessStartInfo(folder ? Path.GetDirectoryName(_lastOutput)! : _lastOutput!) { UseShellExecute = true }); }
        catch (Exception) { SetStatus("Windows no pudo abrir el archivo o la carpeta.", true); }
    }

    private void SetStatus(string text, bool error = false)
    {
        _status.Text = text;
        _status.ForeColor = error ? Color.FromArgb(164, 40, 40) : Color.FromArgb(40, 65, 78);
    }
}
