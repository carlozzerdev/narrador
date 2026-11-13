namespace Narrador.WinForms;

public sealed partial class ElevenLabsTab
{
    private TextBox _srtPath = null!;
    private TextBox _voiceId = null!;
    private TextBox _apiKey = null!;
    private ComboBox _model = null!;
    private NumericUpDown _stability = null!;
    private NumericUpDown _similarity = null!;
    private NumericUpDown _speed = null!;
    private CheckBox _showKey = null!;
    private CheckBox _rememberKey = null!;
    private DataGridView _editor = null!;
    private TextBox _outputPath = null!;
    private Label _summary = null!;
    private Label _status = null!;
    private ProgressBar _progress = null!;
    private Button _browseSrt = null!;
    private Button _browseOutput = null!;
    private Button _generateButton = null!;
    private Button _cancelButton = null!;
    private Button _playButton = null!;
    private Button _folderButton = null!;
    private Button _saveSettingsButton = null!;
    private GroupBox _configuration = null!;

    private void InitializeComponent()
    {
        SuspendLayout();
        BackColor = Color.White;
        AutoScaleMode = AutoScaleMode.Dpi;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 240));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 14));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        var srt = CreatePathRow("Archivo de subtítulos (.srt)", out _srtPath, out _browseSrt);
        _browseSrt.Text = "Seleccionar SRT…";
        root.Controls.Add(srt, 0, 0);

        _configuration = new GroupBox { Text = "Voz y conexión", Dock = DockStyle.Fill, Padding = new Padding(12, 24, 12, 10) };
        var config = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 5 };
        config.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        config.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        config.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106));
        config.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        for (var i = 0; i < 5; i++) config.RowStyles.Add(new RowStyle(SizeType.Percent, 20));

        _voiceId = new TextBox { Dock = DockStyle.Fill, Name = "VoiceId", AccessibleName = "ID de voz" };
        _model = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Modelo" };
        _model.Items.Add("eleven_multilingual_v2");
        _model.SelectedIndex = 0;
        config.Controls.Add(FieldLabel("ID de voz"), 0, 0);
        config.Controls.Add(_voiceId, 1, 0);
        config.Controls.Add(FieldLabel("Modelo"), 2, 0);
        config.Controls.Add(_model, 3, 0);

        _apiKey = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true, AccessibleName = "API key", MaxLength = 512 };
        config.Controls.Add(FieldLabel("API key"), 0, 1);
        config.Controls.Add(_apiKey, 1, 1);
        config.SetColumnSpan(_apiKey, 3);

        _showKey = new CheckBox { Text = "Mostrar clave", AutoSize = true };
        _rememberKey = new CheckBox { Text = "Recordar en este usuario de Windows", AutoSize = true };
        var secrets = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        secrets.Controls.AddRange([_showKey, _rememberKey]);
        config.Controls.Add(secrets, 1, 2);
        config.SetColumnSpan(secrets, 3);

        _stability = CreateVoiceSetting(0.50M, "Estabilidad");
        _similarity = CreateVoiceSetting(0.75M, "Similitud");
        config.Controls.Add(FieldLabel("Estabilidad"), 0, 3);
        config.Controls.Add(_stability, 1, 3);
        config.Controls.Add(FieldLabel("Similitud"), 2, 3);
        config.Controls.Add(_similarity, 3, 3);
        _speed = new NumericUpDown
        {
            Dock = DockStyle.Fill, Minimum = 0.50M, Maximum = 2.00M, Increment = 0.01M,
            DecimalPlaces = 2, Value = 1.00M, Name = "NarrationSpeed", AccessibleName = "Velocidad de narración"
        };
        config.Controls.Add(FieldLabel("Velocidad (×)"), 0, 4);
        config.Controls.Add(_speed, 1, 4);
        var speedHint = new Label
        {
            Text = "0,50×–2,00× · ajuste local fuera de 0,70×–1,20×", Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(80, 93, 108)
        };
        config.Controls.Add(speedHint, 2, 4);
        config.SetColumnSpan(speedHint, 2);
        _configuration.Controls.Add(config);
        root.Controls.Add(_configuration, 0, 1);

        var previewLabel = new Label
        {
            Text = "Subtítulos · texto editable · los tiempos se adaptan en memoria al generar", AutoSize = true,
            Anchor = AnchorStyles.Left, Font = new Font(Font, FontStyle.Bold)
        };
        root.Controls.Add(previewLabel, 0, 2);
        _editor = new DataGridView
        {
            Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle,
            BackgroundColor = Color.FromArgb(250, 252, 254),
            AccessibleName = "Subtítulos con sus tiempos y texto editable", Name = "NarrationText", Enabled = false,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToOrderColumns = false,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect, MultiSelect = false,
            AutoGenerateColumns = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 36
        };
        _editor.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _editor.DefaultCellStyle.Padding = new Padding(4);
        _editor.Columns.Add(new DataGridViewTextBoxColumn { Name = "CueNumber", HeaderText = "N.º", Width = 55, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        _editor.Columns.Add(new DataGridViewTextBoxColumn { Name = "CueStart", HeaderText = "Inicio", Width = 128, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        _editor.Columns.Add(new DataGridViewTextBoxColumn { Name = "CueEnd", HeaderText = "Fin", Width = 128, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        _editor.Columns.Add(new DataGridViewTextBoxColumn { Name = "CueDuration", HeaderText = "Duración", Width = 95, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        _editor.Columns.Add(new DataGridViewTextBoxColumn { Name = "CueText", HeaderText = "Texto de narración (editable)", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, SortMode = DataGridViewColumnSortMode.NotSortable });
        root.Controls.Add(_editor, 0, 3);
        _summary = new Label
        {
            Text = "Tiempos adaptables · los silencios absorben los retrasos y permiten volver al horario del SRT.",
            AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Color.FromArgb(80, 93, 108)
        };
        root.Controls.Add(_summary, 0, 4);

        var output = CreatePathRow("Archivo de salida · WAV recomendado para montaje · MP3 también disponible", out _outputPath, out _browseOutput);
        _browseOutput.Text = "Guardar como…";
        root.Controls.Add(output, 0, 5);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        _generateButton = ActionButton("Generar audio", 150);
        _generateButton.BackColor = Color.FromArgb(0, 108, 129);
        _generateButton.ForeColor = Color.White;
        _generateButton.FlatStyle = FlatStyle.Flat;
        _generateButton.FlatAppearance.BorderSize = 0;
        _generateButton.Enabled = false;
        _cancelButton = ActionButton("Cancelar", 95);
        _cancelButton.Enabled = false;
        _playButton = ActionButton("Reproducir audio", 145);
        _playButton.Enabled = false;
        _folderButton = ActionButton("Abrir carpeta", 125);
        _folderButton.Enabled = false;
        _saveSettingsButton = ActionButton("Guardar configuración", 185);
        actions.Controls.AddRange([_generateButton, _cancelButton, _playButton, _folderButton, _saveSettingsButton]);
        root.Controls.Add(actions, 0, 6);
        _progress = new ProgressBar { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100, Margin = new Padding(3, 1, 3, 1) };
        root.Controls.Add(_progress, 0, 7);
        _status = new Label { Text = "Selecciona un SRT para comenzar.", Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, AccessibleName = "Estado" };
        root.Controls.Add(_status, 0, 8);
        Controls.Add(root);
        ResumeLayout(true);
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 4, 0)
    };

    private static NumericUpDown CreateVoiceSetting(decimal value, string accessibleName) => new()
    {
        Dock = DockStyle.Fill, Minimum = 0, Maximum = 1, Increment = 0.05M, DecimalPlaces = 2,
        Value = value, AccessibleName = accessibleName
    };

    private static Button ActionButton(string text, int width) => new()
    {
        Text = text, Width = width, Height = 35, Margin = new Padding(0, 3, 8, 0), UseVisualStyleBackColor = true
    };

    private static TableLayoutPanel CreatePathRow(string caption, out TextBox path, out Button browse)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var label = new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0) };
        row.Controls.Add(label, 0, 0);
        row.SetColumnSpan(label, 2);
        path = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, AccessibleName = caption, BackColor = Color.White };
        browse = new Button { Dock = DockStyle.Fill, Margin = new Padding(8, 1, 0, 4), UseVisualStyleBackColor = true };
        row.Controls.Add(path, 0, 1);
        row.Controls.Add(browse, 1, 1);
        return row;
    }
}
