using System.Drawing.Imaging;
using System.Runtime.ExceptionServices;
using Narrador.Application;
using Narrador.Domain;
using Narrador.Infrastructure.Files;
using Narrador.WinForms;

namespace Narrador.Tests;

public sealed class WinFormsTests
{
    [Fact]
    public void MainWindowRendersLoadsSrtAndClosesAfterEditing()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.SystemAware);
                System.Windows.Forms.Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                System.Windows.Forms.Application.EnableVisualStyles();
                var preferences = new MemoryPreferences();
                var load = new LoadSrt(new TextFileReader(), new SrtParser());
                var generate = new GenerateSynchronizedNarration([], new UnusedExporter());
                using var form = new MainForm(load, generate, preferences, preferences);
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-20000, -20000);
                form.Shown += async (_, _) =>
                {
                    try
                    {
                        var root = FindRepositoryRoot();
                        var speed = Descendants(form).OfType<NumericUpDown>().Single(control => control.Name == "NarrationSpeed");
                        Assert.Equal(1.12M, speed.Value);
                        Assert.Equal(0.50M, speed.Minimum);
                        Assert.Equal(2.00M, speed.Maximum);
                        var output = Path.Combine(root, "artifacts", "qa");
                        Directory.CreateDirectory(output);
                        foreach (var (name, size) in new[]
                        {
                            ("winforms", form.Size), ("winforms-minimum", form.MinimumSize)
                        })
                        {
                            form.Size = size;
                            form.PerformLayout();
                            using var bitmap = new Bitmap(form.Width, form.Height);
                            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                            bitmap.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
                            var grid = Descendants(form).OfType<DataGridView>().Single();
                            Assert.True(grid.Height >= 60, $"La tabla debe conservar altura útil: {grid.Height}.");
                            Assert.True(grid.Columns["CueStart"]!.ReadOnly);
                            Assert.True(grid.Columns["CueEnd"]!.ReadOnly);
                            Assert.False(grid.Columns["CueText"]!.ReadOnly);
                            foreach (var button in Descendants(form).OfType<Button>())
                                Assert.True(button.Right <= button.Parent!.ClientSize.Width, $"El botón {button.Text} queda fuera de su contenedor.");
                        }

                        var tab = Descendants(form).OfType<ElevenLabsTab>().Single();
                        await tab.LoadSrtFileAsync(Path.Combine(root, "samples", "tutorial.srt"));
                        var loadedGrid = Descendants(form).OfType<DataGridView>().Single();
                        Assert.Equal(3, loadedGrid.Rows.Count);
                        Assert.Equal("00:00:00.000", loadedGrid.Rows[0].Cells["CueStart"].Value);
                        Assert.Contains("Bienvenido", loadedGrid.Rows[0].Cells["CueText"].Value!.ToString());
                        Assert.True(loadedGrid.Enabled);
                        loadedGrid.Rows[0].Cells["CueText"].Value = "Texto corregido.";
                        Assert.Equal("Texto corregido.", loadedGrid.Rows[0].Cells["CueText"].Value);
                        var originals = tab.ReadEditedSubtitles();
                        var adjusted = new AdaptiveSubtitleTimeline();
                        for (var index = 0; index < originals.Count; index++)
                            adjusted.Append(originals[index], originals[index].End - originals[index].Start
                                + (index == 0 ? TimeSpan.FromMilliseconds(899) : TimeSpan.Zero));
                        tab.ApplyAdjustedTimings(adjusted.Cues);
                        Assert.Equal("00:00:04.899", loadedGrid.Rows[0].Cells["CueEnd"].Value);
                        Assert.Equal("00:00:04.899", loadedGrid.Rows[1].Cells["CueStart"].Value);
                        Assert.NotEqual(Color.Empty, loadedGrid.Rows[1].Cells["CueStart"].Style.BackColor);
                        Assert.Equal("00:00:09.500", loadedGrid.Rows[2].Cells["CueStart"].Value);
                        Assert.Equal(Color.Empty, loadedGrid.Rows[2].Cells["CueStart"].Style.BackColor);
                        var nextGenerationInput = tab.ReadEditedSubtitles();
                        Assert.Equal(originals[1].Start, nextGenerationInput[1].Start);
                        Assert.Equal("Texto corregido.", nextGenerationInput[0].Text);
                        tab.ApplyAdjustedTimings(nextGenerationInput);
                        Assert.Equal("00:00:04.500", loadedGrid.Rows[1].Cells["CueStart"].Value);
                        speed.Value = 2.00M;
                    }
                    catch (Exception ex) { failure = ex; }
                    finally { form.Close(); }
                };
                // Run a native message loop for asynchronous loading and control cleanup.
                System.Windows.Forms.Application.Run(form);
                Assert.Equal(2.00, preferences.LastSaved?.Speed);
            }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "La comprobación visual no terminó.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (Control child in control.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "Narrador.sln")))
            directory = directory.Parent ?? throw new InvalidOperationException("No se encuentra la solución.");
        return directory.FullName;
    }

    private sealed class MemoryPreferences : ISettingsStore, ISecretStore
    {
        public AppSettings? LastSaved { get; private set; }
        public AppSettings Load() => new(Speed: 1.12);
        public void Save(AppSettings settings) => LastSaved = settings;
        public string? ReadApiKey(string providerId) => null;
        public void SaveApiKey(string providerId, string? apiKey) { }
    }

    private sealed class UnusedExporter : ITimedAudioExporter
    {
        public Task<ITimedAudioExportSession> BeginAsync(string outputPath, bool overwrite, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("La prueba visual no genera audio.");
    }
}
