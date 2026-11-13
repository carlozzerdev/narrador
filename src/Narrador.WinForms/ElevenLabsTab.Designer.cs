using Narrador.Application;

namespace Narrador.WinForms;

public sealed partial class ElevenLabsTab
{
    private ComboBox _voices = null!;
    private TextBox _voiceId = null!;
    private TextBox _apiKey = null!;
    private ComboBox _model = null!;
    private NumericUpDown _stability = null!;
    private NumericUpDown _similarity = null!;
    private NumericUpDown _speed = null!;
    private CheckBox _showKey = null!;
    private CheckBox _rememberKey = null!;
    private Label _status = null!;
    private Button _saveSettingsButton = null!;
    private Button _loadSettingsButton = null!;

    private void InitializeComponent()
    {
        SuspendLayout();
        BackColor = Color.White;
        AutoScaleMode = AutoScaleMode.Dpi;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        var configuration = new GroupBox { Text = "Voz y conexión", Dock = DockStyle.Fill, Padding = new Padding(12, 20, 12, 6) };
        var config = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 6 };
        config.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        config.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        config.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106));
        config.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        for (var i = 0; i < 6; i++) config.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / 6));

        _voices = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Name = "SavedVoices", AccessibleName = "Voces guardadas", DisplayMember = nameof(SavedVoice.Name) };
        _model = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Modelo" };
        _model.Items.Add("eleven_multilingual_v2");
        _model.SelectedIndex = 0;
        config.Controls.Add(FieldLabel("Voz guardada"), 0, 0);
        config.Controls.Add(_voices, 1, 0);
        config.Controls.Add(FieldLabel("Modelo"), 2, 0);
        config.Controls.Add(_model, 3, 0);
        _voiceId = new TextBox { Dock = DockStyle.Fill, Name = "VoiceId", AccessibleName = "ID de voz" };
        config.Controls.Add(FieldLabel("ID de voz"), 0, 1);
        config.Controls.Add(_voiceId, 1, 1);
        config.SetColumnSpan(_voiceId, 3);
        _apiKey = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true, AccessibleName = "API key", MaxLength = 512 };
        config.Controls.Add(FieldLabel("API key"), 0, 2);
        config.Controls.Add(_apiKey, 1, 2);
        config.SetColumnSpan(_apiKey, 3);
        _showKey = new CheckBox { Text = "Mostrar clave", AutoSize = true };
        _rememberKey = new CheckBox { Text = "Recordar en este usuario de Windows", AutoSize = true };
        var secrets = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        secrets.Controls.AddRange([_showKey, _rememberKey]);
        config.Controls.Add(secrets, 1, 3);
        config.SetColumnSpan(secrets, 3);
        _stability = CreateVoiceSetting(0.50M, "Estabilidad");
        _similarity = CreateVoiceSetting(0.75M, "Similitud");
        config.Controls.Add(FieldLabel("Estabilidad"), 0, 4);
        config.Controls.Add(_stability, 1, 4);
        config.Controls.Add(FieldLabel("Similitud"), 2, 4);
        config.Controls.Add(_similarity, 3, 4);
        _speed = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 0.50M, Maximum = 2.00M, Increment = 0.01M, DecimalPlaces = 2, Value = 1.00M, Name = "NarrationSpeed", AccessibleName = "Velocidad de narración" };
        config.Controls.Add(FieldLabel("Velocidad (×)"), 0, 5);
        config.Controls.Add(_speed, 1, 5);
        var speedHint = new Label { Text = "0,50×–2,00× · ajuste nativo/local", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(80, 93, 108) };
        config.Controls.Add(speedHint, 2, 5);
        config.SetColumnSpan(speedHint, 2);
        configuration.Controls.Add(config);
        root.Controls.Add(configuration, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        _saveSettingsButton = new Button { Text = "Guardar configuración", Width = 185, Height = 35 };
        _loadSettingsButton = new Button { Text = "Cargar configuración", Width = 185, Height = 35 };
        actions.Controls.AddRange([_saveSettingsButton, _loadSettingsButton]);
        root.Controls.Add(actions, 0, 1);
        _status = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, AccessibleName = "Estado de ElevenLabs" };
        root.Controls.Add(_status, 0, 2);
        Controls.Add(root);
        ResumeLayout(true);
    }

    private static Label FieldLabel(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 4, 0) };
    private static NumericUpDown CreateVoiceSetting(decimal value, string accessibleName) => new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 1, Increment = 0.05M, DecimalPlaces = 2, Value = value, AccessibleName = accessibleName };
}
