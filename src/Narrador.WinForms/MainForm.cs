using Narrador.Application;

namespace Narrador.WinForms;

public sealed class MainForm : Form
{
    private readonly ElevenLabsTab _elevenLabs;
    private readonly SubtitleEditor _subtitles;
    private readonly AudioGenerationPanel _audio;
    private readonly VideoCompositionPanel _video;
    private bool _closeWhenIdle;

    public MainForm(LoadSrt loadSrt, GenerateSynchronizedNarration generate, ISettingsStore settings, ISecretStore secrets,
        ApplyNarrationToVideo applyVideo)
    {
        Text = "Narrador · Audio para videotutoriales";
        using (var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("Narrador.AppIcon")!)
        using (var icon = new Icon(iconStream)) Icon = (Icon)icon.Clone();
        Font = new Font("Segoe UI", 10F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1600, 980);
        MinimumSize = new Size(1420, 900);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(244, 247, 250);

        var columns = new SplitContainer
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 8,
            Size = new Size(1568, 948), Panel1MinSize = 720, Panel2MinSize = 560,
            SplitterDistance = 850, Name = "NarratorVideoColumns"
        };
        var outer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 14, 16, 18) };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(0, 0, 8, 0) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 360));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 155));
        root.Controls.Add(new Label
        {
            Text = "Narrador", AutoSize = true, Font = new Font("Segoe UI", 22F, FontStyle.Bold),
            ForeColor = Color.FromArgb(24, 48, 72), Margin = Padding.Empty
        }, 0, 0);
        root.Controls.Add(new Label
        {
            Text = "Convierte el guion de tu videotutorial en una narración profesional.", AutoSize = true,
            ForeColor = Color.FromArgb(75, 90, 105), Margin = new Padding(2, 3, 0, 0)
        }, 0, 1);

        _subtitles = new SubtitleEditor(loadSrt) { Dock = DockStyle.Fill };
        root.Controls.Add(_subtitles, 0, 2);
        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(18, 7) };
        var page = new TabPage("ElevenLabs") { BackColor = Color.White, Padding = new Padding(14) };
        _elevenLabs = new ElevenLabsTab(settings, secrets) { Dock = DockStyle.Fill };
        page.Controls.Add(_elevenLabs);
        tabs.TabPages.Add(page);
        root.Controls.Add(tabs, 0, 3);
        _audio = new AudioGenerationPanel(_subtitles, generate,
            () => tabs.SelectedTab?.Controls.OfType<IServiceConfiguration>().FirstOrDefault()) { Dock = DockStyle.Fill };
        root.Controls.Add(_audio, 0, 4);
        _video = new VideoCompositionPanel(applyVideo) { Dock = DockStyle.Fill };
        columns.Panel2.Controls.Add(_video);
        tabs.SelectedIndexChanged += (_, _) => _audio.ServiceChanged();
        _audio.BusyChanged += (_, _) =>
        {
            tabs.Enabled = !_audio.IsBusy;
            _video.SetAudioBusy(_audio.IsBusy);
        };
        _audio.AudioGenerated += (_, _) => _video.SetGeneratedAudio(_audio.LastOutputPath);
        _video.BusyChanged += (_, _) => _audio.SetVideoBusy(_video.IsBusy);
        columns.Panel1.Controls.Add(root);
        outer.Controls.Add(columns);
        Controls.Add(outer);

        FormClosing += (_, e) =>
        {
            if (_audio.IsBusy || _subtitles.IsBusy || _video.IsBusy)
            {
                e.Cancel = true;
                _closeWhenIdle = true;
                if (_audio.IsBusy) _audio.CancelOperation();
                _subtitles.CancelOperation();
                if (_video.IsBusy) _video.CancelOperation();
            }
            else foreach (var service in tabs.TabPages.Cast<TabPage>().SelectMany(tab => tab.Controls.OfType<IServiceConfiguration>())) service.SaveOnClose();
        };
        void CloseWhenIdle(object? sender, EventArgs args)
        {
            if (_closeWhenIdle && !_audio.IsBusy && !_subtitles.IsBusy && !_video.IsBusy && !IsDisposed)
                BeginInvoke(Close);
        }
        _audio.BusyChanged += CloseWhenIdle;
        _subtitles.BusyChanged += CloseWhenIdle;
        _video.BusyChanged += CloseWhenIdle;
    }
}
