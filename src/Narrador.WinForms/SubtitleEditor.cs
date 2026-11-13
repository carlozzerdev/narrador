using System.Globalization;
using Narrador.Application;
using Narrador.Domain;

namespace Narrador.WinForms;

public sealed class SubtitleEditor : UserControl
{
    private readonly LoadSrt _loadSrt;
    private readonly TextBox _path;
    private readonly Button _browse;
    private readonly Button _delete;
    private readonly DataGridView _grid;
    private readonly Label _summary;
    private readonly Label _status;
    private CancellationTokenSource? _loading;
    private bool _updating;
    private bool _generationBusy;

    public string? SourcePath { get; private set; }
    public bool HasSubtitles => _grid.Rows.Count > 0;
    public bool IsBusy => _loading is not null || _generationBusy;
    public event EventHandler? Changed;
    public event EventHandler? BusyChanged;

    public SubtitleEditor(LoadSrt loadSrt)
    {
        _loadSrt = loadSrt;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        var source = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        source.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        source.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
        _path = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, AccessibleName = "Archivo SRT compartido" };
        _browse = new Button { Text = "Seleccionar SRT…", Dock = DockStyle.Fill };
        source.Controls.Add(_path, 0, 0);
        source.Controls.Add(_browse, 1, 0);
        root.Controls.Add(source, 0, 0);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        _delete = new Button { Text = "Eliminar bloques seleccionados", AutoSize = true, Height = 30, Enabled = false };
        toolbar.Controls.Add(_delete);
        toolbar.Controls.Add(new Label { Text = "Edita inicio, fin y texto · selecciona filas para eliminar", AutoSize = true, Margin = new Padding(12, 8, 0, 0) });
        root.Controls.Add(toolbar, 0, 1);
        _grid = new DataGridView
        {
            Dock = DockStyle.Fill, Name = "NarrationText", AccessibleName = "Subtítulos compartidos: tiempos y texto editables",
            BackgroundColor = Color.White, RowHeadersVisible = true, RowHeadersWidth = 28,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToOrderColumns = false,
            SelectionMode = DataGridViewSelectionMode.RowHeaderSelect, MultiSelect = true,
            AutoGenerateColumns = false, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, ColumnHeadersHeight = 36,
            Enabled = false
        };
        _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        AddColumn("CueNumber", "N.º", 45, readOnly: true);
        AddColumn("CueStart", "Inicio", 115);
        AddColumn("CueEnd", "Fin", 115);
        AddColumn("CueDuration", "Duración", 80, readOnly: true);
        AddColumn("AudioStart", "Inicio audio", 115, readOnly: true, visible: false);
        AddColumn("AudioEnd", "Fin audio", 115, readOnly: true, visible: false);
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "CueText", HeaderText = "Texto (editable)", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 150, SortMode = DataGridViewColumnSortMode.NotSortable });
        root.Controls.Add(_grid, 0, 2);
        _summary = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        _status = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Text = "Selecciona un SRT. Los cambios se comparten entre todos los servicios." };
        root.Controls.Add(_summary, 0, 3);
        root.Controls.Add(_status, 0, 4);
        Controls.Add(root);
        _browse.Click += async (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "Subtítulos SRT (*.srt)|*.srt", CheckFileExists = true };
            if (dialog.ShowDialog(this) == DialogResult.OK) await LoadSrtFileAsync(dialog.FileName);
        };
        _delete.Click += (_, _) => DeleteSelectedBlocks();
        _grid.SelectionChanged += (_, _) => UpdateControls();
        _grid.CellValueChanged += (_, e) =>
        {
            if (_updating || Disposing || IsDisposed) return;
            if (e.ColumnIndex < 0 || _grid.Columns[e.ColumnIndex].Name is not ("CueStart" or "CueEnd" or "CueText")) return;
            ClearAudioTimings();
            ValidateRows();
            UpdateSummary();
        };
        _grid.DataError += (_, e) => { e.ThrowException = false; SetStatus("Revisa el valor introducido.", true); };
        UpdateSummary();
    }

    private void AddColumn(string name, string title, int width, bool readOnly = false, bool visible = true) =>
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = title, Width = width, ReadOnly = readOnly, Visible = visible, SortMode = DataGridViewColumnSortMode.NotSortable });

    internal async Task LoadSrtFileAsync(string path)
    {
        if (IsBusy || _generationBusy) return;
        _loading = new CancellationTokenSource();
        UpdateControls();
        BusyChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            SetStatus("Leyendo los subtítulos…");
            var loaded = await _loadSrt.ExecuteAsync(path, _loading.Token);
            _updating = true;
            try
            {
                _grid.Rows.Clear();
                ClearAudioTimings();
                foreach (var cue in loaded.Cues)
                    _grid.Rows.Add(cue.Number, FormatTime(cue.Start), FormatTime(cue.End), FormatDuration(cue.End - cue.Start), "", "", cue.Text);
                SourcePath = path;
                _path.Text = path;
            }
            finally { _updating = false; }
            ValidateRows();
            UpdateSummary();
        }
        catch (OperationCanceledException) { SetStatus("Carga cancelada."); }
        catch (Exception ex) when (ex is NarrationException or IOException or UnauthorizedAccessException)
        {
            SetStatus(ex is NarrationException ? ex.Message : "No se pudo leer el archivo SRT. Comprueba la ruta y los permisos.", true);
        }
        finally
        {
            _loading.Dispose();
            _loading = null;
            UpdateControls();
            BusyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void CancelOperation() => _loading?.Cancel();

    public void SetGenerationBusy(bool busy)
    {
        _generationBusy = busy;
        UpdateControls();
        BusyChanged?.Invoke(this, EventArgs.Empty);
    }

    internal IReadOnlyList<SubtitleCue> ReadEditedSubtitles()
    {
        _grid.EndEdit();
        return ValidateRows() ?? throw new NarrationException(_status.Text);
    }

    private IReadOnlyList<SubtitleCue>? ValidateRows()
    {
        var cues = new List<SubtitleCue>();
        string? firstError = null;
        foreach (DataGridViewRow row in _grid.Rows)
        {
            row.ErrorText = "";
            var number = Convert.ToInt32(row.Cells["CueNumber"].Value, CultureInfo.InvariantCulture);
            var text = row.Cells["CueText"].Value?.ToString()?.Trim();
            string? error = null;
            if (!TryParseTime(row.Cells["CueStart"].Value?.ToString(), out var start)
                || !TryParseTime(row.Cells["CueEnd"].Value?.ToString(), out var end))
            {
                row.Cells["CueDuration"].Value = "—";
                error = $"Bloque {number}: utiliza HH:mm:ss.fff (también se admite coma).";
            }
            else
            {
                row.Cells["CueDuration"].Value = end > start ? FormatDuration(end - start) : "—";
                if (end <= start) error = $"Bloque {number}: el fin debe ser posterior al inicio.";
                else if (string.IsNullOrWhiteSpace(text)) error = $"Bloque {number}: el texto no puede estar vacío.";
                else if (cues.Count > 0 && start < cues[^1].End) error = $"Bloque {number}: los tiempos se solapan o no están ordenados.";
                else cues.Add(new SubtitleCue(number, start, end, text));
            }
            if (error is not null) { row.ErrorText = error; firstError ??= error; }
        }
        firstError ??= _grid.Rows.Count == 0 ? "Carga un SRT con bloques antes de generar." : null;
        SetStatus(firstError ?? "Tiempos y texto válidos · los cambios no modifican el archivo SRT de origen.", firstError is not null);
        return firstError is null ? cues.AsReadOnly() : null;
    }

    internal void DeleteSelectedBlocks()
    {
        if (IsBusy || _generationBusy) return;
        var rows = _grid.SelectedRows.Cast<DataGridViewRow>().OrderByDescending(row => row.Index).ToArray();
        if (rows.Length == 0) return;
        _grid.EndEdit();
        _updating = true;
        try
        {
            foreach (var row in rows) _grid.Rows.RemoveAt(row.Index);
            for (var index = 0; index < _grid.Rows.Count; index++) _grid.Rows[index].Cells["CueNumber"].Value = index + 1;
            ClearAudioTimings();
        }
        finally { _updating = false; }
        ValidateRows();
        UpdateSummary();
    }

    internal void ApplyAdjustedTimings(IReadOnlyList<SubtitleCue> timeline)
    {
        if (timeline.Count != _grid.Rows.Count) throw new ArgumentException("La línea de tiempo no coincide con los subtítulos.", nameof(timeline));
        _updating = true;
        try
        {
            _grid.Columns["AudioStart"]!.Visible = true;
            _grid.Columns["AudioEnd"]!.Visible = true;
            for (var index = 0; index < timeline.Count; index++)
            {
                var cue = timeline[index];
                var row = _grid.Rows[index];
                row.Cells["AudioStart"].Value = FormatTime(cue.Start);
                row.Cells["AudioEnd"].Value = FormatTime(cue.End);
                TryParseTime(row.Cells["CueStart"].Value?.ToString(), out var start);
                TryParseTime(row.Cells["CueEnd"].Value?.ToString(), out var end);
                var color = cue.Start != start || cue.End != end ? Color.FromArgb(255, 246, 209) : Color.Empty;
                row.Cells["AudioStart"].Style.BackColor = color;
                row.Cells["AudioEnd"].Style.BackColor = color;
            }
        }
        finally { _updating = false; }
    }

    private void ClearAudioTimings()
    {
        _grid.Columns["AudioStart"]!.Visible = false;
        _grid.Columns["AudioEnd"]!.Visible = false;
    }

    private void UpdateSummary()
    {
        _summary.Text = $"{_grid.Rows.Count:N0} bloques · {_grid.Rows.Cast<DataGridViewRow>().Sum(row => row.Cells["CueText"].Value?.ToString()?.Length ?? 0):N0} caracteres";
        UpdateControls();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateControls()
    {
        if (Disposing || IsDisposed) return;
        _browse.Enabled = !IsBusy && !_generationBusy;
        _grid.Enabled = !IsBusy && !_generationBusy && HasSubtitles;
        _delete.Enabled = _grid.Enabled && _grid.SelectedRows.Count > 0;
    }

    private void SetStatus(string text, bool error = false)
    {
        _status.Text = text;
        _status.ForeColor = error ? Color.FromArgb(164, 40, 40) : Color.FromArgb(40, 65, 78);
    }

    private static string FormatTime(TimeSpan time) => $"{(long)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}";
    private static string FormatDuration(TimeSpan time) => time.TotalSeconds.ToString("F3", CultureInfo.CurrentCulture) + " s";

    private static bool TryParseTime(string? text, out TimeSpan time)
    {
        time = default;
        var parts = text?.Trim().Replace(',', '.').Split(':', '.');
        if (parts is not { Length: 4 } || parts[0].Length < 2 || parts[1].Length != 2 || parts[2].Length != 2 || parts[3].Length != 3
            || parts.Any(part => part.Any(character => character is < '0' or > '9'))
            || !long.TryParse(parts[0], out var hours) || hours > 100000
            || !int.TryParse(parts[1], out var minutes) || minutes > 59
            || !int.TryParse(parts[2], out var seconds) || seconds > 59
            || !int.TryParse(parts[3], out var milliseconds)) return false;
        time = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(milliseconds);
        return true;
    }
}
