using Narrador.Application;

namespace Narrador.WinForms;

// Service tabs supply configuration; the shared panel owns the audio workflow.
public interface IServiceConfiguration
{
    string ProviderId { get; }
    string? OutputDirectory { get; set; }
    SpeechRequest ReadConfiguration();
    bool TrySavePreferences();
    void SaveOnClose();
}
