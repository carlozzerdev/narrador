using Narrador.Application;
using Narrador.Infrastructure.Audio;
using Narrador.Infrastructure.ElevenLabs;
using Narrador.Infrastructure.Files;
using Narrador.Infrastructure.Security;

namespace Narrador.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        // Composition root: only this entry point knows the concrete infrastructure adapters.
        var settingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Narrador");
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var provider = new ElevenLabsSynthesizer(httpClient);
        var loadSrt = new LoadSrt(new TextFileReader(), new SrtParser());
        var generate = new GenerateSynchronizedNarration([provider], new SrtAudioExporter());
        using var form = new MainForm(loadSrt, generate,
            new JsonSettingsStore(settingsDirectory), new WindowsSecretStore(settingsDirectory));
        System.Windows.Forms.Application.Run(form);
    }
}
