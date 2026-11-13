using System.Diagnostics;
using Narrador.Application;

namespace Narrador.WinForms;

public sealed class VideoCompositionPanel : UserControl
{
    private readonly ApplyNarrationToVideo _apply;
    private readonly TextBox _videoPath;
    private readonly TextBox _audioPath;
    private readonly TextBox _outputPath;
    private readonly Button _browseVideo;
    private readonly Button _browseOutput;
    private readonly Button _browseAudio;
    private readonly EmbeddedVideoPlayer _player;
    private readonly Button _applyButton;
    private readonly Button _cancel;
    private readonly Button _play;
    private readonly Button _folder;
    private readonly ProgressBar _progress;
    private readonly Label _status;
    private CancellationTokenSource? _operation;
    private bool _audioBusy;
    private string? _lastOutput;

    public bool IsBusy => _operation is not null;
    public event EventHandler? BusyChanged;

    public VideoCompositionPanel(ApplyNarrationToVideo apply)
    {
        _apply = apply;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(12, 0, 0, 0) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 44, 0, 204, 82, 42, 14, 42 })
            root.RowStyles.Add(height == 0 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, height));
        root.Controls.Add(new Label { Text = "Vídeo", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 22F, FontStyle.Bold), ForeColor = Color.FromArgb(24, 48, 72), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _player = new EmbeddedVideoPlayer { Dock = DockStyle.Fill, Name = "EmbeddedVideoPlayer" };
        root.Controls.Add(_player, 0, 1);
        var paths = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        paths.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var index = 0; index < 3; index++) paths.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / 3));
        paths.Controls.Add(PathRow("Vídeo original", "InputVideo", "Seleccionar vídeo…", out _videoPath, out _browseVideo), 0, 0);
        paths.Controls.Add(PathRow("Narración · automática o seleccionada", "GeneratedAudioForVideo", "Seleccionar audio…", out _audioPath, out _browseAudio), 0, 1);
        paths.Controls.Add(PathRow("Vídeo de salida", "VideoOutput", "Guardar vídeo como…", out _outputPath, out _browseOutput), 0, 2);
        root.Controls.Add(paths, 0, 2);
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        _applyButton = ActionButton("Aplicar narración al vídeo");
        _cancel = ActionButton("Cancelar montaje");
        _play = ActionButton("Reproducir vídeo");
        _folder = ActionButton("Abrir carpeta del vídeo");
        actions.Controls.Add(_applyButton, 0, 0);
        actions.Controls.Add(_cancel, 1, 0);
        actions.Controls.Add(_play, 0, 1);
        actions.Controls.Add(_folder, 1, 1);
        root.Controls.Add(actions, 0, 3);
        var hint = new Label { Text = "Sustituye el sonido original. Añade silencio o recorta la narración a la duración del vídeo.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        root.Controls.Add(hint, 0, 4);
        _progress = new ProgressBar { Dock = DockStyle.Fill, Margin = new Padding(3, 1, 3, 1) };
        root.Controls.Add(_progress, 0, 5);
        _status = new Label { Text = "Selecciona un vídeo y genera o elige su narración.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AccessibleName = "Estado del montaje de vídeo" };
        root.Controls.Add(_status, 0, 6);
        Controls.Add(root);
        _browseVideo.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "Vídeos|*.mp4;*.mov;*.m4v;*.mkv;*.avi;*.webm;*.wmv|Todos los archivos|*.*", CheckFileExists = true, Title = "Seleccionar vídeo original" };
            if (dialog.ShowDialog(this) == DialogResult.OK) SelectVideoFile(dialog.FileName);
        };
        _browseAudio.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "Archivos de audio|*.wav;*.mp3;*.m4a;*.aac;*.flac;*.ogg;*.wma|Todos los archivos|*.*", CheckFileExists = true, Title = "Seleccionar narración para el vídeo" };
            if (dialog.ShowDialog(this) == DialogResult.OK) SelectAudioFile(dialog.FileName);
        };
        _browseOutput.Click += (_, _) => SelectOutput();
        _applyButton.Click += async (_, _) => await ApplyAsync();
        _cancel.Click += (_, _) => CancelOperation();
        _play.Click += (_, _) => { if (File.Exists(_lastOutput)) _player.LoadFile(_lastOutput!, play: true); };
        _folder.Click += (_, _) => OpenResult(true);
        UpdateControls();
    }

    private static Button ActionButton(string text) => new() { Text = text, Dock = DockStyle.Fill, Margin = new Padding(2, 3, 2, 3) };

    private static Control PathRow(string title, string name, string buttonText, out TextBox path, out Button button)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var label = new Label { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        path = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Name = name, AccessibleName = title };
        button = new Button { Text = buttonText, Dock = DockStyle.Fill };
        row.Controls.Add(label, 0, 0);
        row.SetColumnSpan(label, 2);
        row.Controls.Add(path, 0, 1);
        row.Controls.Add(button, 1, 1);
        return row;
    }

    internal void SelectAudioFile(string path)
    {
        if (IsBusy || _audioBusy) return;
        _audioPath.Text = path;
        UpdateControls();
    }

    public void SetGeneratedAudio(string? path)
    {
        _audioPath.Text = path ?? "";
        UpdateControls();
    }

    public void SetAudioBusy(bool busy)
    {
        _audioBusy = busy;
        UpdateControls();
    }

    internal void SelectVideoFile(string path)
    {
        if (IsBusy || _audioBusy) return;
        _videoPath.Text = path;
        _player.LoadFile(path);
        var extension = Path.GetExtension(path).ToLowerInvariant() is ".mp4" or ".mov" or ".m4v" ? ".mp4" : ".mkv";
        _outputPath.Text = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "-narrado" + extension);
        UpdateControls();
    }

    private void SelectOutput()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "Vídeo MP4 (*.mp4)|*.mp4|Vídeo Matroska (*.mkv)|*.mkv", DefaultExt = "mp4", AddExtension = true,
            FilterIndex = Path.GetExtension(_outputPath.Text).Equals(".mkv", StringComparison.OrdinalIgnoreCase) ? 2 : 1,
            FileName = string.IsNullOrWhiteSpace(_outputPath.Text) ? "video-narrado" : Path.GetFileNameWithoutExtension(_outputPath.Text),
            InitialDirectory = Path.GetDirectoryName(_outputPath.Text), OverwritePrompt = false
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) _outputPath.Text = dialog.FileName;
    }

    internal async Task ApplyAsync()
    {
        if (IsBusy || _audioBusy) return;
        if (!File.Exists(_audioPath.Text)) { SetStatus("Selecciona un archivo de audio o genera la narración antes de aplicar el montaje.", true); UpdateControls(); return; }
        if (!File.Exists(_videoPath.Text)) { SetStatus("Selecciona un archivo de vídeo existente.", true); UpdateControls(); return; }
        var overwrite = File.Exists(_outputPath.Text);
        if (overwrite && MessageBox.Show(this, "El vídeo de destino ya existe. ¿Quieres sustituirlo al finalizar?", "Sustituir vídeo", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _player.Unload();
        _operation = new CancellationTokenSource();
        var operation = _operation;
        UpdateControls();
        _progress.Value = 0;
        _progress.Style = ProgressBarStyle.Marquee;
        SetStatus("Aplicando la narración al vídeo…");
        BusyChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            var progress = new Progress<TimeSpan>(time =>
            {
                if (_operation == operation && !IsDisposed && !Disposing)
                    SetStatus($"Aplicando la narración · procesado {time:hh\\:mm\\:ss}…");
            });
            await _apply.ExecuteAsync(_videoPath.Text, _audioPath.Text, _outputPath.Text, overwrite, progress, operation.Token);
            _lastOutput = _outputPath.Text;
            _player.LoadFile(_lastOutput);
            _progress.Value = 100;
            SetStatus("Vídeo con narración guardado. Puedes reproducirlo o abrir su carpeta.");
        }
        catch (OperationCanceledException) { SetStatus("Montaje cancelado."); }
        catch (Exception ex)
        {
            SetStatus(ex switch
            {
                NarrationException => ex.Message,
                UnauthorizedAccessException => "No tienes permisos para leer o guardar el vídeo en esa ubicación.",
                IOException => "No se pudo guardar el vídeo. Comprueba el espacio disponible y si está abierto en otra aplicación.",
                _ => "No se pudo completar el montaje del vídeo."
            }, true);
        }
        finally
        {
            operation.Dispose();
            _operation = null;
            _progress.Style = ProgressBarStyle.Continuous;
            UpdateControls();
            BusyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void CancelOperation()
    {
        _operation?.Cancel();
        _cancel.Enabled = false;
        SetStatus("Cancelando el montaje…");
    }

    private void UpdateControls()
    {
        if (IsDisposed || Disposing) return;
        var available = !IsBusy && !_audioBusy;
        _browseVideo.Enabled = available;
        _browseAudio.Enabled = available;
        _player.SetInteractionEnabled(available);
        _browseOutput.Enabled = available && !string.IsNullOrWhiteSpace(_videoPath.Text);
        _applyButton.Enabled = available && File.Exists(_videoPath.Text) && File.Exists(_audioPath.Text);
        _cancel.Enabled = IsBusy;
        _play.Enabled = available && File.Exists(_lastOutput);
        _folder.Enabled = available && File.Exists(_lastOutput);
    }

    private void OpenResult(bool folder)
    {
        if (!File.Exists(_lastOutput)) { SetStatus("El vídeo generado ya no está disponible.", true); UpdateControls(); return; }
        try { Process.Start(new ProcessStartInfo(folder ? Path.GetDirectoryName(_lastOutput)! : _lastOutput!) { UseShellExecute = true }); }
        catch (Exception) { SetStatus("Windows no pudo abrir el vídeo o su carpeta.", true); }
    }

    private void SetStatus(string text, bool error = false)
    {
        _status.Text = text;
        _status.ForeColor = error ? Color.FromArgb(164, 40, 40) : Color.FromArgb(40, 65, 78);
    }
}
