using Narrador.Application;

namespace Narrador.WinForms;

public sealed class MainForm : Form
{
    private readonly ElevenLabsTab _elevenLabs;
    private bool _closeWhenIdle;

    public MainForm(LoadSrt loadSrt, GenerateSynchronizedNarration generate, ISettingsStore settings, ISecretStore secrets)
    {
        Text = "Narrador · Audio para videotutoriales";
        Font = new Font("Segoe UI", 10F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1120, 900);
        MinimumSize = new Size(1000, 880);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(244, 247, 250);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22, 14, 22, 18), ColumnCount = 1, RowCount = 3 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
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

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(18, 7) };
        var page = new TabPage("ElevenLabs") { BackColor = Color.White, Padding = new Padding(14) };
        _elevenLabs = new ElevenLabsTab(loadSrt, generate, settings, secrets) { Dock = DockStyle.Fill };
        page.Controls.Add(_elevenLabs);
        tabs.TabPages.Add(page);
        root.Controls.Add(tabs, 0, 2);
        Controls.Add(root);

        FormClosing += (_, e) =>
        {
            if (_elevenLabs.IsBusy)
            {
                e.Cancel = true;
                _closeWhenIdle = true;
                _elevenLabs.CancelOperation();
            }
            else _elevenLabs.SaveOnClose();
        };
        _elevenLabs.BusyChanged += (_, _) =>
        {
            if (_closeWhenIdle && !_elevenLabs.IsBusy && !IsDisposed)
                BeginInvoke(Close);
        };
    }
}
