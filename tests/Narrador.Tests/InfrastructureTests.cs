using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Narrador.Application;
using Narrador.Infrastructure.Audio;
using Narrador.Infrastructure.Files;
using Narrador.Infrastructure.Security;

namespace Narrador.Tests;

public sealed class InfrastructureTests
{
    [Fact]
    public void PreferencesAndEncryptedKeyRoundTripIndependently()
    {
        using var directory = new TestDirectory();
        var settingsStore = new JsonSettingsStore(directory.Path);
        var secrets = new WindowsSecretStore(directory.Path);
        var settings = new AppSettings(RememberApiKey: true, Speed: 2.0);
        settingsStore.Save(settings);
        secrets.SaveApiKey("elevenlabs", "test-key-for-dpapi");
        var loaded = settingsStore.Load();
        Assert.Equal(settings.Speed, loaded.Speed);
        Assert.Equal(settings.VoiceId, loaded.VoiceId);
        Assert.Equal(settings.RememberApiKey, loaded.RememberApiKey);
        Assert.Equal(settings.Voices, loaded.Voices);
        Assert.Equal("test-key-for-dpapi", secrets.ReadApiKey("elevenlabs"));
        Assert.DoesNotContain("test-key", File.ReadAllText(Path.Combine(directory.Path, "settings.json")));
        Assert.False(File.Exists(Path.Combine(directory.Path, "elevenlabs.key")));
        settingsStore.Save(loaded with { Speed = 1.5 });
        Assert.Equal("test-key-for-dpapi", secrets.ReadApiKey("elevenlabs"));
        Assert.Equal(1.5, settingsStore.Load().Speed);
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.Path, "settings.json")));
        Assert.Equal(3, json.RootElement.GetProperty("Voices").GetArrayLength());
        Assert.True(json.RootElement.GetProperty("ProtectedApiKeys").TryGetProperty("elevenlabs", out _));
        secrets.SaveApiKey("elevenlabs", null);
        Assert.Null(secrets.ReadApiKey("elevenlabs"));
    }

    [Fact]
    public void PreferencesFromPreviousVersionsUseNormalSpeed()
    {
        using var directory = new TestDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "settings.json"), "{\"VoiceId\":\"previous-voice\",\"Stability\":0.5,\"SimilarityBoost\":0.75}");
        var settings = new JsonSettingsStore(directory.Path).Load();
        Assert.Equal("previous-voice", settings.VoiceId);
        Assert.Equal(1.0, settings.Speed);
        Assert.Equal(SavedVoice.Defaults, settings.Voices);
    }

    [Fact]
    public void LegacyEncryptedKeyMigratesToSettingsWithoutLosingPreferences()
    {
        using var directory = new TestDirectory();
        var settings = new JsonSettingsStore(directory.Path);
        settings.Save(new AppSettings(VoiceId: "custom-voice", Speed: 1.17));
        var legacy = Path.Combine(directory.Path, "elevenlabs.key");
        File.WriteAllBytes(legacy, ProtectedData.Protect(Encoding.UTF8.GetBytes("legacy-test-key"), null, DataProtectionScope.CurrentUser));
        var secrets = new WindowsSecretStore(directory.Path);
        Assert.Equal("legacy-test-key", secrets.ReadApiKey("elevenlabs"));
        Assert.True(File.Exists(legacy));
        secrets.SaveApiKey("elevenlabs", "legacy-test-key");
        Assert.False(File.Exists(legacy));
        Assert.Equal("legacy-test-key", secrets.ReadApiKey("elevenlabs"));
        Assert.Equal("custom-voice", settings.Load().VoiceId);
        Assert.Equal(1.17, settings.Load().Speed);
    }

    [Fact]
    public void SavedVoiceCatalogRoundTripsWithCustomEntriesAndEncryptedKeys()
    {
        using var directory = new TestDirectory();
        var store = new JsonSettingsStore(directory.Path);
        var voices = SavedVoice.Defaults.Append(new SavedVoice("Personal", "custom-id")).ToArray();
        new WindowsSecretStore(directory.Path).SaveApiKey("elevenlabs", "test-key");
        store.Save(new AppSettings(VoiceId: "custom-id") { Voices = voices });
        var restored = store.Load();
        Assert.Equal(voices, restored.Voices);
        Assert.Equal("custom-id", restored.VoiceId);
        Assert.Equal("test-key", new WindowsSecretStore(directory.Path).ReadApiKey("elevenlabs"));
    }

    [Fact]
    public async Task ExportConcatenatesDecodedAudioIntoOnePlayableMp3()
    {
        using var directory = new TestDirectory();
        var source = CreateTestMp3(directory.Path);
        var path = Path.Combine(directory.Path, "narracion.mp3");
        var exporter = new Mp3AudioExporter();
        await using (var session = await exporter.BeginAsync(path, false, CancellationToken.None))
        {
            await session.AppendAsync(source, CancellationToken.None);
            await session.AppendAsync(source, CancellationToken.None);
            var duration = await session.CommitAsync(CancellationToken.None);
            Assert.True(duration > TimeSpan.Zero);
        }
        using var reader = new MediaFoundationReader(path);
        Assert.InRange(reader.TotalTime.TotalSeconds, 0.35, 1.0);
        Assert.Empty(Directory.GetDirectories(directory.Path, ".narrador-*"));
    }

    [Fact]
    public async Task SingleClipPreservesExactProviderBytes()
    {
        using var directory = new TestDirectory();
        var source = CreateTestMp3(directory.Path);
        var path = Path.Combine(directory.Path, "narracion.mp3");
        await using (var session = await new Mp3AudioExporter().BeginAsync(path, false, CancellationToken.None))
        {
            await session.AppendAsync(source, CancellationToken.None);
            await session.CommitAsync(CancellationToken.None);
        }
        Assert.Equal(source, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task ContinuousExportAlsoAppliesLocalTempoForASingleClip()
    {
        using var directory = new TestDirectory();
        var source = CreateTestMp3(directory.Path);
        var sourcePath = Path.Combine(directory.Path, "reference.mp3");
        await File.WriteAllBytesAsync(sourcePath, source);
        double originalDuration;
        using (var reference = new MediaFoundationReader(sourcePath)) originalDuration = reference.TotalTime.TotalSeconds;
        var path = Path.Combine(directory.Path, "faster.mp3");
        await using (var session = await new Mp3AudioExporter().BeginAsync(path, false, CancellationToken.None))
        {
            await session.AppendAsync(source, CancellationToken.None, tempo: 2.0 / 1.2);
            await session.CommitAsync(CancellationToken.None);
        }
        using var reader = new MediaFoundationReader(path);
        Assert.InRange(Math.Abs(reader.TotalTime.TotalSeconds - originalDuration / (2.0 / 1.2)), 0, 0.07);
    }

    [Fact]
    public async Task CancelledExportPreservesExistingDestinationAndRemovesTemporaryFiles()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "existente.mp3");
        await File.WriteAllTextAsync(path, "existing-audio");
        using var cancellation = new CancellationTokenSource();
        await using (var session = await new Mp3AudioExporter().BeginAsync(path, true, CancellationToken.None))
        {
            await session.AppendAsync([1, 2, 3], CancellationToken.None);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.CommitAsync(cancellation.Token));
        }
        Assert.Equal("existing-audio", await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetDirectories(directory.Path, ".narrador-*"));
    }

    [Fact]
    public async Task DestinationCannotBeOverwrittenWithoutConsent()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "existente.mp3");
        await File.WriteAllTextAsync(path, "existing-audio");
        await Assert.ThrowsAsync<NarrationException>(() => new Mp3AudioExporter().BeginAsync(path, false, CancellationToken.None));
        Assert.Equal("existing-audio", await File.ReadAllTextAsync(path));
    }

    private static byte[] CreateTestMp3(string directory)
    {
        var path = Path.Combine(directory, "source-" + Guid.NewGuid().ToString("N") + ".mp3");
        var signal = new SignalGenerator(44100, 1) { Gain = 0.05, Frequency = 440, Type = SignalGeneratorType.Sin };
        var shortSignal = new OffsetSampleProvider(signal) { Take = TimeSpan.FromMilliseconds(250) };
        MediaFoundationEncoder.EncodeToMp3(new SampleToWaveProvider16(shortSignal), path, 128000);
        return File.ReadAllBytes(path);
    }
}
