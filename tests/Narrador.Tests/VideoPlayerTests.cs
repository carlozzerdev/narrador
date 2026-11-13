using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.ExceptionServices;
using Narrador.WinForms;

namespace Narrador.Tests;

[CollectionDefinition("Windows UI", DisableParallelization = true)]
public sealed class WindowsUiCollection;

[Collection("Windows UI")]
public sealed class VideoPlayerTests
{
    [Fact]
    public async Task EmbeddedPlayerPlaysPausesSeeksChangesVolumeAndSharesTheLoadedFile()
    {
        using var directory = new TestDirectory();
        var video = Path.Combine(directory.Path, "vídeo de prueba.mp4");
        var root = FindRepositoryRoot();
        using (var process = new Process
        {
            StartInfo = new ProcessStartInfo(Path.Combine(root, ".tools", "ffmpeg", "ffmpeg.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true
            }
        })
        {
            foreach (var argument in new[] { "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=10:duration=8", "-f", "lavfi", "-i", "sine=frequency=440:duration=8", "-c:v", "mpeg4", "-g", "10", "-bf", "0", "-c:a", "aac", video })
                process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, await error);
        }

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                System.Windows.Forms.Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                using var form = new Form { ClientSize = new Size(720, 560), ShowInTaskbar = false, Opacity = 0, StartPosition = FormStartPosition.Manual, Location = Point.Empty };
                var sharer = new CapturingSharer();
                using var player = new EmbeddedVideoPlayer(sharer) { Dock = DockStyle.Fill };
                form.Controls.Add(player);
                form.Shown += async (_, _) =>
                {
                    try
                    {
                        var controls = Descendants(player).ToArray();
                        controls.OfType<TrackBar>().Single(control => control.Name == "VideoVolume").Value = 0;
                        player.LoadFile(video);
                        await WaitUntilAsync(() => player.DurationMilliseconds >= 7900 && player.IsPaused, "No se cargó la vista previa en pausa.");
                        Assert.Equal(Path.GetFullPath(video), player.CurrentPath);
                        Assert.False(player.IsPlaying);
                        controls.OfType<Button>().Single(button => button.Name == "VideoPlay").PerformClick();
                        await WaitUntilAsync(() => player.IsPlaying && player.PositionMilliseconds >= 200, "Play no inició la reproducción.");
                        controls.OfType<Button>().Single(button => button.Name == "VideoPause").PerformClick();
                        await WaitUntilAsync(() => player.IsPaused, "Pausa no detuvo la reproducción.");
                        var position = player.PositionMilliseconds;
                        await Task.Delay(250);
                        Assert.InRange(Math.Abs(player.PositionMilliseconds - position), 0, 100);
                        player.SeekTo(TimeSpan.FromSeconds(3));
                        await WaitUntilAsync(() => player.PositionMilliseconds is >= 2800 and <= 3500, "No se pudo avanzar a tres segundos.");
                        var seek = controls.OfType<TrackBar>().Single(control => control.Name == "VideoPosition");
                        seek.Value = 750;
                        await WaitUntilAsync(() => player.PositionMilliseconds is >= 5500 and <= 6500, "La barra de posición no cambió el instante de reproducción.");
                        controls.OfType<TrackBar>().Single(control => control.Name == "VideoVolume").Value = 37;
                        await WaitUntilAsync(() => player.Volume == 37, "El control de volumen no actualizó el reproductor.");
                        controls.OfType<Button>().Single(button => button.Name == "VideoMute").PerformClick();
                        await WaitUntilAsync(() => player.IsMuted, "No se pudo silenciar el reproductor.");
                        controls.OfType<Button>().Single(button => button.Name == "VideoMute").PerformClick();
                        await WaitUntilAsync(() => !player.IsMuted, "No se pudo activar el audio.");
                        await player.ShareAsync();
                        Assert.Equal(video, sharer.Path);
                        Assert.Same(form, sharer.Owner);
                        var frame = Path.Combine(root, "artifacts", "qa", "video-frame.png");
                        if (File.Exists(frame)) File.Delete(frame);
                        Assert.True(player.SaveFrame(frame), "No se pudo capturar una imagen del vídeo reproducido.");
                        await WaitUntilAsync(() => File.Exists(frame) && new FileInfo(frame).Length > 0, "El reproductor no produjo una imagen del vídeo.");
                        using (var bitmap = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                            bitmap.Save(Path.Combine(root, "artifacts", "qa", "video-player.png"), ImageFormat.Png);
                        }
                        player.Unload();
                        Assert.Null(player.CurrentPath);
                        Assert.False(controls.OfType<Button>().Single(button => button.Name == "VideoShare").Enabled);
                        using var exclusive = new FileStream(video, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    }
                    catch (Exception ex) { failure = ex; }
                    finally { player.Unload(); form.Close(); }
                };
                System.Windows.Forms.Application.Run(form);
            }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "La prueba del reproductor no terminó.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string message)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(50);
        Assert.True(condition(), message);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "Narrador.sln")))
            directory = directory.Parent ?? throw new InvalidOperationException("No se encuentra la solución.");
        return directory.FullName;
    }

    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (Control child in control.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private sealed class CapturingSharer : IVideoFileSharer
    {
        public string? Path { get; private set; }
        public IWin32Window? Owner { get; private set; }
        public Task ShareAsync(IWin32Window owner, string path)
        {
            Owner = owner;
            Path = path;
            return Task.CompletedTask;
        }
    }
}
