using System.ComponentModel;
using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;

namespace Narrador.WinForms;

public sealed class EmbeddedVideoPlayer : UserControl
{
    private readonly IVideoFileSharer _sharer;
    private readonly VideoView _view;
    private readonly Label _placeholder;
    private readonly Button _play;
    private readonly Button _pause;
    private readonly Button _mute;
    private readonly Button _share;
    private readonly TrackBar _position;
    private readonly TrackBar _volume;
    private readonly Label _time;
    private readonly Label _status;
    private readonly System.Windows.Forms.Timer _timer;
    private LibVLC? _vlc;
    private MediaPlayer? _player;
    private Media? _media;
    private bool _updating;
    private bool _interactionEnabled = true;
    private bool _resumeRequested;
    private bool _draggingPosition;
    private bool _resourcesDisposed;

    public string? CurrentPath { get; private set; }
    public bool IsPlaying => _player?.IsPlaying == true;
    public bool IsPaused => _player?.State == VLCState.Paused;
    public long DurationMilliseconds => Math.Max(0, _player?.Length ?? 0);
    public long PositionMilliseconds => Math.Max(0, _player?.Time ?? 0);
    public int Volume => _player?.Volume ?? _volume.Value;
    public bool IsMuted => _player?.Mute == true;

    public EmbeddedVideoPlayer(IVideoFileSharer? sharer = null)
    {
        _sharer = sharer ?? new WindowsVideoFileSharer();
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        var screen = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(19, 27, 36) };
        _view = new VideoView { Dock = DockStyle.Fill, BackColor = Color.Black, Name = "VideoSurface", AccessibleName = "Imagen del reproductor de vídeo" };
        _placeholder = new Label { Text = "Selecciona un vídeo para verlo aquí", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(190, 204, 216), BackColor = screen.BackColor };
        screen.Controls.Add(_view);
        screen.Controls.Add(_placeholder);
        root.Controls.Add(screen, 0, 0);
        var seek = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        seek.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        seek.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        _position = new TrackBar { Dock = DockStyle.Fill, Minimum = 0, Maximum = 1000, TickStyle = TickStyle.None, Name = "VideoPosition", AccessibleName = "Posición del vídeo" };
        _time = new Label { Dock = DockStyle.Fill, Text = "00:00 / 00:00", TextAlign = ContentAlignment.MiddleRight };
        seek.Controls.Add(_position, 0, 0);
        seek.Controls.Add(_time, 1, 0);
        root.Controls.Add(seek, 0, 1);
        var transport = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, Margin = Padding.Empty };
        foreach (var width in new[] { 72, 72, 100 }) transport.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width));
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        _play = TransportButton("Play", "VideoPlay");
        _pause = TransportButton("Pausa", "VideoPause");
        _mute = TransportButton("Silenciar", "VideoMute");
        _share = TransportButton("Compartir", "VideoShare");
        _volume = new TrackBar { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100, Value = 80, TickStyle = TickStyle.None, Name = "VideoVolume", AccessibleName = "Volumen del vídeo" };
        transport.Controls.Add(_play, 0, 0);
        transport.Controls.Add(_pause, 1, 0);
        transport.Controls.Add(_mute, 2, 0);
        transport.Controls.Add(_volume, 3, 0);
        transport.Controls.Add(_share, 4, 0);
        root.Controls.Add(transport, 0, 2);
        _status = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, AccessibleName = "Estado del reproductor" };
        root.Controls.Add(_status, 0, 3);
        Controls.Add(root);
        _play.Click += (_, _) => Play();
        _pause.Click += (_, _) => Pause();
        _mute.Click += (_, _) =>
        {
            if (_player is null) return;
            _player.Mute = !_player.Mute;
            RefreshPlayback();
        };
        _volume.ValueChanged += (_, _) => { if (_player is not null) _player.Volume = _volume.Value; };
        _position.ValueChanged += (_, _) =>
        {
            if (!_updating && !_draggingPosition && _player is not null && _player.IsSeekable)
                SeekTo(TimeSpan.FromMilliseconds(DurationMilliseconds * _position.Value / 1000));
        };
        _position.MouseDown += (_, _) => _draggingPosition = true;
        _position.MouseUp += (_, _) =>
        {
            _draggingPosition = false;
            SeekTo(TimeSpan.FromMilliseconds(DurationMilliseconds * _position.Value / 1000));
        };
        _share.Click += async (_, _) => await ShareAsync();
        _timer = new System.Windows.Forms.Timer { Interval = 200 };
        _timer.Tick += (_, _) => RefreshPlayback();
        RefreshPlayback();
    }

    private static Button TransportButton(string text, string name) => new() { Text = text, Name = name, Dock = DockStyle.Fill, Margin = new Padding(2, 3, 2, 3) };

    public void LoadFile(string path, bool play = false)
    {
        if (_resourcesDisposed) return;
        if (!File.Exists(path)) { _status.Text = "El archivo de vídeo no está disponible."; return; }
        try
        {
            Unload();
            if (_vlc is null)
            {
                Core.Initialize();
                _vlc = new LibVLC("--no-video-title-show", "--quiet");
                _player = new MediaPlayer(_vlc) { Volume = _volume.Value, EnableMouseInput = false, EnableKeyInput = false };
                _view.MediaPlayer = _player;
            }
            CurrentPath = Path.GetFullPath(path);
            _media = new Media(_vlc, new Uri(CurrentPath));
            if (!play) _media.AddOption(":start-paused");
            _player!.Media = _media;
            _placeholder.Visible = false;
            _status.Text = Path.GetFileName(path);
            _player.Play();
            _timer.Start();
            RefreshPlayback();
        }
        catch (Exception ex) when (ex is VLCException or DllNotFoundException or BadImageFormatException or InvalidOperationException)
        {
            _status.Text = "No se pudo abrir el reproductor. Comprueba que se ha copiado la carpeta completa de la aplicación.";
            _placeholder.Visible = true;
        }
    }

    public void Play()
    {
        if (_player is null || CurrentPath is null) return;
        _resumeRequested = true;
        if (_player.State == VLCState.Paused) _player.SetPause(false);
        else _player.Play();
        RefreshPlayback();
    }

    public void Pause()
    {
        _resumeRequested = false;
        _player?.SetPause(true);
        RefreshPlayback();
    }

    public void SeekTo(TimeSpan position)
    {
        if (_player?.IsSeekable == true) _player.Time = Math.Clamp((long)position.TotalMilliseconds, 0, DurationMilliseconds);
    }

    internal bool SaveFrame(string path) => _player?.TakeSnapshot(0, path, 0, 0) == true;

    internal async Task ShareAsync()
    {
        if (CurrentPath is not { } path || !File.Exists(path)) return;
        try { await _sharer.ShareAsync(FindForm() ?? (IWin32Window)this, path); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _status.Text = "No se pudo abrir Compartir. Puedes abrir la carpeta del vídeo para compartirlo desde Windows.";
        }
    }

    public void SetInteractionEnabled(bool enabled)
    {
        _interactionEnabled = enabled;
        RefreshPlayback();
    }

    public void Unload()
    {
        if (_resourcesDisposed) return;
        _timer.Stop();
        _player?.Stop();
        if (_player is not null) _player.Media = null;
        _media?.Dispose();
        _media = null;
        _resumeRequested = false;
        CurrentPath = null;
        _placeholder.Visible = true;
        RefreshPlayback();
    }

    private void RefreshPlayback()
    {
        if (_resourcesDisposed || IsDisposed || Disposing) return;
        if (_resumeRequested && _player?.State == VLCState.Paused) _player.SetPause(false);
        if (IsPlaying) _resumeRequested = false;
        var loaded = CurrentPath is not null && _player is not null && _interactionEnabled;
        _play.Enabled = loaded && !IsPlaying;
        _pause.Enabled = loaded && IsPlaying;
        _mute.Enabled = loaded;
        _volume.Enabled = loaded;
        _share.Enabled = loaded && File.Exists(CurrentPath);
        _position.Enabled = loaded && _player!.IsSeekable;
        _mute.Text = _player?.Mute == true ? "Activar audio" : "Silenciar";
        _time.Text = FormatTime(PositionMilliseconds) + " / " + FormatTime(DurationMilliseconds);
        _updating = true;
        try { if (!_draggingPosition) _position.Value = DurationMilliseconds > 0 ? (int)Math.Clamp(PositionMilliseconds * 1000 / DurationMilliseconds, 0, 1000) : 0; }
        finally { _updating = false; }
        if (_player?.State == VLCState.Error)
        {
            _status.Text = "No se pudo reproducir el archivo seleccionado.";
            _timer.Stop();
        }
    }

    private static string FormatTime(long milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(milliseconds);
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"mm\:ss");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_resourcesDisposed)
        {
            Unload();
            _resourcesDisposed = true;
            _timer.Dispose();
            _view.MediaPlayer = null;
            _player?.Dispose();
            _vlc?.Dispose();
            _player = null;
            _vlc = null;
            if (_sharer is IDisposable disposable) disposable.Dispose();
        }
        base.Dispose(disposing);
    }
}
