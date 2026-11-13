using System.Drawing.Imaging;
using System.Runtime.ExceptionServices;
using Narrador.Application;
using Narrador.Domain;
using Narrador.Infrastructure.Files;
using Narrador.WinForms;

namespace Narrador.Tests;

[Collection("Windows UI")]
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
                var provider = new CapturingProvider();
                var secondProvider = new CapturingProvider("another-service");
                var generate = new GenerateSynchronizedNarration([provider, secondProvider], new TestExporter());
                var videoComposer = new CapturingVideoComposer();
                using var form = new MainForm(load, generate, preferences, preferences, new ApplyNarrationToVideo(videoComposer));
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = Point.Empty;
                form.Opacity = 0;
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
                            Assert.False(grid.Columns["CueStart"]!.ReadOnly);
                            Assert.False(grid.Columns["CueEnd"]!.ReadOnly);
                            Assert.False(grid.Columns["CueText"]!.ReadOnly);
                            foreach (var button in Descendants(form).OfType<Button>())
                                Assert.True(button.Right <= button.Parent!.ClientSize.Width, $"El botón {button.Text} queda fuera de su contenedor.");
                        }

                        var tab = Descendants(form).OfType<ElevenLabsTab>().Single();
                        var audio = Descendants(form).OfType<AudioGenerationPanel>().Single();
                        var video = Descendants(form).OfType<VideoCompositionPanel>().Single();
                        var columns = Descendants(form).OfType<SplitContainer>().Single();
                        Assert.True(columns.Panel1.Contains(audio.Parent!));
                        Assert.True(columns.Panel2.Contains(video));
                        Assert.True(video.PointToScreen(Point.Empty).X > audio.PointToScreen(Point.Empty).X);
                        Assert.DoesNotContain(Ancestors(video), control => control is TabPage);
                        Assert.False(Descendants(video).OfType<Button>().Single(button => button.Text == "Aplicar narración al vídeo").Enabled);
                        var editor = Descendants(form).OfType<SubtitleEditor>().Single();
                        Assert.DoesNotContain(Ancestors(audio), control => control is TabPage);
                        foreach (var text in new[] { "Generar audio", "Cancelar", "Reproducir audio", "Abrir carpeta" })
                            Assert.DoesNotContain(Ancestors(Descendants(audio).OfType<Button>().Single(button => button.Text == text)), control => control is TabPage);
                        foreach (var text in new[] { "Guardar configuración", "Cargar configuración" })
                            Assert.Contains(Ancestors(Descendants(tab).OfType<Button>().Single(button => button.Text == text)), control => control is TabPage);
                        Assert.NotNull(form.Icon);
                        var voices = Descendants(tab).OfType<ComboBox>().Single(control => control.Name == "SavedVoices");
                        var voiceId = Descendants(tab).OfType<TextBox>().Single(control => control.Name == "VoiceId");
                        Assert.False(voiceId.ReadOnly);
                        Assert.Equal(new[] { "StoreTeller", "Mari", "Nico (PRO)" }, voices.Items.Cast<SavedVoice>().Select(voice => voice.Name));
                        foreach (var (name, id) in new[]
                        {
                            ("StoreTeller", "JBFqnCBsd6RMkjVDRZzb"), ("Mari", "uShnBlXHAqA27srS28g2"), ("Nico (PRO)", "FXn3GKRRt9FCEyTbrkaL")
                        })
                        {
                            voices.SelectedItem = voices.Items.Cast<SavedVoice>().Single(voice => voice.Name == name);
                            Assert.Equal(id, voiceId.Text);
                        }
                        voiceId.Text = "my-custom-voice";
                        Assert.Equal(-1, voices.SelectedIndex);
                        speed.Value = 1.27M;
                        var apiKey = Descendants(tab).OfType<TextBox>().Single(control => control.AccessibleName == "API key");
                        apiKey.Text = "test-key";
                        Descendants(tab).OfType<CheckBox>().Single(control => control.Text == "Recordar en este usuario de Windows").Checked = true;
                        Descendants(tab).OfType<Button>().Single(button => button.Text == "Guardar configuración").PerformClick();
                        Assert.Equal("my-custom-voice", preferences.LastSaved?.VoiceId);
                        Assert.Equal(SavedVoice.Defaults, preferences.LastSaved?.Voices);
                        voiceId.Text = "temporary-edit";
                        speed.Value = 0.8M;
                        apiKey.Text = "temporary-key";
                        Descendants(tab).OfType<Button>().Single(button => button.Text == "Cargar configuración").PerformClick();
                        Assert.Equal("my-custom-voice", voiceId.Text);
                        Assert.Equal(-1, voices.SelectedIndex);
                        Assert.Equal(1.27M, speed.Value);
                        Assert.Equal("test-key", apiKey.Text);
                        voices.SelectedIndex = 1;
                        Assert.DoesNotContain(Descendants(tab), control => control is DataGridView || control is SubtitleEditor);
                        Assert.DoesNotContain(Ancestors(editor), control => control is TabPage);
                        await editor.LoadSrtFileAsync(Path.Combine(root, "samples", "tutorial.srt"));
                        using var mediaDirectory = new TestDirectory();
                        var audioFile = Path.Combine(mediaDirectory.Path, "narracion.wav");
                        Descendants(audio).OfType<TextBox>().Single(control => control.Name == "OutputPath").Text = audioFile;
                        var loadedGrid = Descendants(form).OfType<DataGridView>().Single();
                        Assert.Equal(3, loadedGrid.Rows.Count);
                        Assert.Equal("00:00:00.000", loadedGrid.Rows[0].Cells["CueStart"].Value);
                        Assert.Contains("Bienvenido", loadedGrid.Rows[0].Cells["CueText"].Value!.ToString());
                        Assert.True(loadedGrid.Enabled);
                        loadedGrid.Rows[0].Cells["CueText"].Value = "Texto corregido.";
                        Assert.Equal("Texto corregido.", loadedGrid.Rows[0].Cells["CueText"].Value);
                        var originals = editor.ReadEditedSubtitles();
                        var adjusted = new AdaptiveSubtitleTimeline();
                        for (var index = 0; index < originals.Count; index++)
                            adjusted.Append(originals[index], originals[index].End - originals[index].Start
                                + (index == 0 ? TimeSpan.FromMilliseconds(899) : TimeSpan.Zero));
                        editor.ApplyAdjustedTimings(adjusted.Cues);
                        Assert.Equal("00:00:04.899", loadedGrid.Rows[0].Cells["AudioEnd"].Value);
                        Assert.Equal("00:00:04.899", loadedGrid.Rows[1].Cells["AudioStart"].Value);
                        Assert.NotEqual(Color.Empty, loadedGrid.Rows[1].Cells["AudioStart"].Style.BackColor);
                        Assert.Equal("00:00:09.500", loadedGrid.Rows[2].Cells["AudioStart"].Value);
                        Assert.Equal(Color.Empty, loadedGrid.Rows[2].Cells["AudioStart"].Style.BackColor);
                        var nextGenerationInput = editor.ReadEditedSubtitles();
                        Assert.Equal(originals[1].Start, nextGenerationInput[1].Start);
                        Assert.Equal("Texto corregido.", nextGenerationInput[0].Text);
                        editor.ApplyAdjustedTimings(nextGenerationInput);
                        Assert.Equal("00:00:04.500", loadedGrid.Rows[1].Cells["CueStart"].Value);

                        // Manual times become the next generation's input, without using the audio's adapted times.
                        loadedGrid.Rows[0].Cells["CueStart"].Value = "00:00:00,250";
                        loadedGrid.Rows[0].Cells["CueEnd"].Value = "00:00:04.200";
                        Assert.False(loadedGrid.Columns["AudioStart"]!.Visible);
                        Assert.Equal(TimeSpan.FromMilliseconds(250), editor.ReadEditedSubtitles()[0].Start);
                        Assert.Equal(TimeSpan.FromMilliseconds(4200), editor.ReadEditedSubtitles()[0].End);

                        loadedGrid.Rows[0].Cells["CueEnd"].Value = "00:00:00.100";
                        Assert.Throws<NarrationException>(() => editor.ReadEditedSubtitles());
                        await audio.GenerateAsync();
                        Assert.Empty(provider.Requests);
                        loadedGrid.Rows[0].Cells["CueEnd"].Value = "00:00:05.000";
                        Assert.Throws<NarrationException>(() => editor.ReadEditedSubtitles());
                        loadedGrid.Rows[0].Cells["CueEnd"].Value = "00:65:04.200";
                        Assert.Throws<NarrationException>(() => editor.ReadEditedSubtitles());
                        loadedGrid.Rows[0].Cells["CueEnd"].Value = "00:00:04.200";
                        loadedGrid.Rows[0].Cells["CueText"].Value = " ";
                        Assert.Throws<NarrationException>(() => editor.ReadEditedSubtitles());
                        loadedGrid.Rows[0].Cells["CueText"].Value = "Texto corregido.";

                        loadedGrid.ClearSelection();
                        loadedGrid.Rows[1].Selected = true;
                        editor.DeleteSelectedBlocks();
                        var edited = editor.ReadEditedSubtitles();
                        Assert.Equal(2, edited.Count);
                        Assert.Equal(new[] { 1, 2 }, edited.Select(cue => cue.Number));
                        Assert.Equal(originals[2].Text, edited[1].Text);
                        Assert.Equal(originals[2].Start, edited[1].Start);

                        Descendants(tab).OfType<TextBox>().Single(control => control.AccessibleName == "API key").Text = "test-key";
                        await audio.GenerateAsync();
                        Assert.Equal(edited.Select(cue => cue.Text), provider.Requests.Select(request => request.Text));
                        Assert.All(provider.Requests, request => Assert.Equal("uShnBlXHAqA27srS28g2", request.VoiceId));
                        Assert.True(loadedGrid.Columns["AudioStart"]!.Visible);
                        Assert.Equal("00:00:00.250", loadedGrid.Rows[0].Cells["AudioStart"].Value);
                        Assert.Equal(audioFile, Descendants(video).OfType<TextBox>().Single(control => control.Name == "GeneratedAudioForVideo").Text);
                        var customAudio = Path.Combine(mediaDirectory.Path, "narracion-personal.mp3");
                        File.WriteAllBytes(customAudio, [2]);
                        video.SelectAudioFile(customAudio);
                        Assert.Equal(customAudio, Descendants(video).OfType<TextBox>().Single(control => control.Name == "GeneratedAudioForVideo").Text);
                        var inputVideo = Path.Combine(mediaDirectory.Path, "tutorial.mp4");
                        File.WriteAllText(inputVideo, "original-video");
                        video.SelectVideoFile(inputVideo);
                        Assert.True(Descendants(video).OfType<Button>().Single(button => button.Text == "Aplicar narración al vídeo").Enabled);
                        video.BusyChanged += (_, _) =>
                        {
                            if (video.IsBusy)
                                Assert.False(Descendants(audio).OfType<Button>().Single(button => button.Text == "Generar audio").Enabled);
                        };
                        await video.ApplyAsync();
                        Assert.Equal(inputVideo, videoComposer.VideoPath);
                        Assert.Equal(customAudio, videoComposer.AudioPath);
                        Assert.Equal("original-video", File.ReadAllText(inputVideo));
                        Assert.True(File.Exists(Path.Combine(mediaDirectory.Path, "tutorial-narrado.mp4")));
                        Assert.True(Descendants(video).OfType<Button>().Single(button => button.Text == "Reproducir vídeo").Enabled);
                        using (var bitmap = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                            bitmap.Save(Path.Combine(output, "winforms-video.png"), ImageFormat.Png);
                        }

                        using (var bitmap = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                            bitmap.Save(Path.Combine(output, "winforms-edited-subtitles.png"), ImageFormat.Png);
                        }
                        // The same shared buttons dispatch to whichever service tab is selected.
                        var tabs = Descendants(form).OfType<TabControl>().Single();
                        using (var otherPage = new TabPage("Otro servicio"))
                        using (var otherService = new TestServiceConfiguration())
                        {
                            otherPage.Controls.Add(otherService);
                            tabs.TabPages.Add(otherPage);
                            tabs.SelectedTab = otherPage;
                            File.Delete(audioFile);
                        await audio.GenerateAsync();
                            Assert.Equal(edited.Count, secondProvider.Requests.Count);
                            Assert.Equal(audioFile, Descendants(video).OfType<TextBox>().Single(control => control.Name == "GeneratedAudioForVideo").Text);
                            Assert.All(secondProvider.Requests, request => Assert.Equal("another-voice", request.VoiceId));
                            Assert.Equal(edited.Count, provider.Requests.Count);
                            tabs.SelectedIndex = 0;
                            tabs.TabPages.Remove(otherPage);
                        }
                        editor.SetGenerationBusy(true);
                        Assert.False(loadedGrid.Enabled);
                        Assert.False(Descendants(audio).OfType<Button>().Single(button => button.Text == "Generar audio").Enabled);
                        loadedGrid.SelectAll();
                        editor.DeleteSelectedBlocks();
                        Assert.Equal(2, loadedGrid.Rows.Count);
                        editor.SetGenerationBusy(false);
                        loadedGrid.SelectAll();
                        editor.DeleteSelectedBlocks();
                        Assert.Empty(loadedGrid.Rows.Cast<DataGridViewRow>());
                        Assert.Throws<NarrationException>(() => editor.ReadEditedSubtitles());
                        Assert.False(Descendants(audio).OfType<Button>().Single(button => button.Text == "Generar audio").Enabled);
                        Assert.Equal(3, new SrtParser().Parse(File.ReadAllText(Path.Combine(root, "samples", "tutorial.srt"))).Count);
                        speed.Value = 2.00M;
                        Descendants(video).OfType<EmbeddedVideoPlayer>().Single().Unload();
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "La comprobación visual no terminó.");
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

    private static IEnumerable<Control> Ancestors(Control control)
    {
        while (control.Parent is { } parent) { yield return parent; control = parent; }
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
        public AppSettings Load() => LastSaved ?? new(Speed: 1.12);
        public void Save(AppSettings settings) => LastSaved = settings;
        private string? _key;
        public string? ReadApiKey(string providerId) => _key;
        public void SaveApiKey(string providerId, string? apiKey) => _key = apiKey;
    }

    private sealed class CapturingProvider(string id = "elevenlabs") : ISpeechSynthesizer
    {
        public string Id => id;
        public int MaxCharacters => 10000;
        public List<SpeechRequest> Requests { get; } = [];
        public Task<byte[]> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new byte[] { 1 });
        }
    }

    private sealed class TestServiceConfiguration : UserControl, IServiceConfiguration
    {
        public string ProviderId => "another-service";
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string? OutputDirectory { get; set; }
        public SpeechRequest ReadConfiguration() => new("", "test-key", "another-voice", "model", new VoiceSettings());
        public bool TrySavePreferences() => true;
        public void SaveOnClose() { }
    }

    private sealed class TestExporter : ITimedAudioExporter
    {
        public Task<ITimedAudioExportSession> BeginAsync(string outputPath, bool overwrite, CancellationToken cancellationToken) =>
            Task.FromResult<ITimedAudioExportSession>(new TestSession(outputPath));
    }

    private sealed class TestSession(string outputPath) : ITimedAudioExportSession
    {
        public Task<TimeSpan> AppendAsync(SubtitleCue cue, byte[] mp3, CancellationToken cancellationToken, double tempo = 1) => Task.FromResult(TimeSpan.FromSeconds(1));
        public Task<TimeSpan> CommitAsync(IReadOnlyList<SubtitleCue> timeline, CancellationToken cancellationToken)
        {
            File.WriteAllBytes(outputPath, [1]);
            return Task.FromResult(timeline[^1].End);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CapturingVideoComposer : IVideoAudioComposer
    {
        public string? VideoPath { get; private set; }
        public string? AudioPath { get; private set; }
        public async Task ComposeAsync(string videoPath, string audioPath, string outputPath, bool overwrite,
            IProgress<TimeSpan>? progress, CancellationToken cancellationToken)
        {
            VideoPath = videoPath;
            AudioPath = audioPath;
            await Task.Yield();
            File.WriteAllText(outputPath, "video-with-narration");
        }
    }
}
