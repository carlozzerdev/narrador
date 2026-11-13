using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Narrador.Application;
using Narrador.Domain;
using Narrador.Infrastructure.ElevenLabs;

namespace Narrador.Tests;

public sealed class ElevenLabsTests
{
    [Theory]
    [InlineData(0.50, 0.70)]
    [InlineData(0.70, 0.70)]
    [InlineData(1.00, 1.00)]
    [InlineData(1.15, 1.15)]
    [InlineData(1.20, 1.20)]
    [InlineData(1.50, 1.20)]
    [InlineData(2.00, 1.20)]
    public async Task RequestMatchesElevenLabsContractAndPreservesBinaryAudio(double speed, double nativeSpeed)
    {
        byte[] bytes = [0x49, 0x44, 0x33, 0, 1, 2];
        using var client = new HttpClient(new StubHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://api.elevenlabs.io/v1/text-to-speech/voice-id?output_format=mp3_44100_128", request.RequestUri!.ToString());
            Assert.Equal("test-key", Assert.Single(request.Headers.GetValues("xi-api-key")));
            Assert.Contains(request.Headers.Accept, value => value.MediaType == "audio/mpeg");
            Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
            using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync());
            Assert.Equal("¡Hola, Zaragoza!", body.RootElement.GetProperty("text").GetString());
            Assert.Equal("eleven_multilingual_v2", body.RootElement.GetProperty("model_id").GetString());
            Assert.Equal(0.5, body.RootElement.GetProperty("voice_settings").GetProperty("stability").GetDouble());
            Assert.Equal(0.75, body.RootElement.GetProperty("voice_settings").GetProperty("similarity_boost").GetDouble());
            Assert.Equal(nativeSpeed, body.RootElement.GetProperty("voice_settings").GetProperty("speed").GetDouble());
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
            return response;
        }));
        var audio = await new ElevenLabsSynthesizer(client).SynthesizeAsync(Request(speed), CancellationToken.None);
        Assert.Equal(bytes, audio);
    }

    [Theory]
    [InlineData(401, "rejected", "API key")]
    [InlineData(401, "quota_exceeded", "cuota")]
    [InlineData(403, "rejected", "permisos")]
    [InlineData(404, "rejected", "ID de voz")]
    [InlineData(429, "rejected", "límite")]
    [InlineData(500, "rejected", "HTTP 500")]
    public async Task ErrorsAreTranslatedWithoutExposingResponseOrKey(int code, string status, string expected)
    {
        using var client = new HttpClient(new StubHandler(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)code)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { detail = new { status, message = "test-key" } }), Encoding.UTF8, "application/json")
        })));
        var ex = await Assert.ThrowsAsync<NarrationException>(() => new ElevenLabsSynthesizer(client).SynthesizeAsync(Request(), CancellationToken.None));
        Assert.Contains(expected, ex.Message);
        Assert.DoesNotContain("test-key", ex.Message);
    }

    [Fact]
    public async Task JsonResponseIsNotTreatedAsAudio()
    {
        using var client = new HttpClient(new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        })));
        await Assert.ThrowsAsync<NarrationException>(() => new ElevenLabsSynthesizer(client).SynthesizeAsync(Request(), CancellationToken.None));
    }

    private static SpeechRequest Request(double speed = 1.0) => new("¡Hola, Zaragoza!", "test-key", "voice-id", "eleven_multilingual_v2", new VoiceSettings(speed: speed));
    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
    }
}
