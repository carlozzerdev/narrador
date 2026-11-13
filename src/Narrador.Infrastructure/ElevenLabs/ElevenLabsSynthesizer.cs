using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Narrador.Application;

namespace Narrador.Infrastructure.ElevenLabs;

public sealed class ElevenLabsSynthesizer(HttpClient httpClient) : ISpeechSynthesizer
{
    public const string ProviderId = "elevenlabs";
    public string Id => ProviderId;
    public int MaxCharacters => 10_000;
    public double GetNativeSpeed(double requestedSpeed) => Math.Clamp(requestedSpeed, 0.7, 1.2);

    public async Task<byte[]> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken)
    {
        var uri = new Uri($"https://api.elevenlabs.io/v1/text-to-speech/{Uri.EscapeDataString(request.VoiceId)}?output_format=mp3_44100_128");
        using var message = new HttpRequestMessage(HttpMethod.Post, uri);
        message.Headers.Add("xi-api-key", request.ApiKey);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/mpeg"));
        message.Content = JsonContent.Create(new
        {
            text = request.Text,
            model_id = request.ModelId,
            voice_settings = new
            {
                stability = request.Settings.Stability,
                similarity_boost = request.Settings.SimilarityBoost,
                speed = GetNativeSpeed(request.Settings.Speed)
            },
            previous_text = request.PreviousText,
            next_text = request.NextText
        });

        // No automatic retries: a failed response may follow a billable generation.
        using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var quotaExceeded = false;
            try
            {
                using var error = await JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                if (error.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.Object
                    && detail.TryGetProperty("status", out var status))
                    quotaExceeded = status.GetString() == "quota_exceeded";
            }
            catch (JsonException) { /* Never display the raw response or request credentials. */ }

            var reason = quotaExceeded ? "La cuenta de ElevenLabs no tiene cuota suficiente." : response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "ElevenLabs ha rechazado la API key.",
                HttpStatusCode.Forbidden => "La API key no tiene permisos para esta voz o este modelo.",
                HttpStatusCode.NotFound => "ElevenLabs no encuentra el ID de voz o el recurso solicitado.",
                HttpStatusCode.TooManyRequests => "ElevenLabs ha alcanzado su límite de peticiones. Inténtalo más tarde.",
                HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => "ElevenLabs ha rechazado el texto o los parámetros de voz.",
                _ => $"ElevenLabs ha respondido con el error HTTP {(int)response.StatusCode}."
            };
            throw new NarrationException(reason);
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not ("audio/mpeg" or "audio/mp3"))
            throw new NarrationException("ElevenLabs no devolvió un archivo MP3 válido.");
        var audio = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (audio.Length == 0) throw new NarrationException("ElevenLabs devolvió un audio vacío.");
        return audio;
    }
}
