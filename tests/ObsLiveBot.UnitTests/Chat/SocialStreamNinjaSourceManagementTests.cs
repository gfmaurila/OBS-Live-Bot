using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Chat;
using ObsLiveBot.Infrastructure.Chat;
using ObsLiveBot.Infrastructure.Configuration;

namespace ObsLiveBot.UnitTests.Chat;

public sealed class SocialStreamNinjaSourceManagementTests
{
    private const string CurrentVideoId = "WH7l_CMYCfU";
    private const string LiveChatUrl = "https://www.youtube.com/live_chat?is_popout=1&v=WH7l_CMYCfU";

    [Fact]
    public void Locator_AcceptsOfficialLiveChatUrlAndProducesStablePerLiveKey()
    {
        Assert.True(YouTubeLiveChatSourceLocator.TryCreate(LiveChatUrl, out var first));
        Assert.True(YouTubeLiveChatSourceLocator.TryCreate(
            "https://youtube.com/live_chat?v=WH7l_CMYCfU&is_popout=1", out var second));

        Assert.Equal(LiveChatUrl, first!.Url);
        Assert.Equal(CurrentVideoId, first.VideoId);
        Assert.Equal(first.IdempotencyKey, second!.IdempotencyKey);
        Assert.NotEqual(first.IdempotencyKey, CreateLocator("https://www.youtube.com/live_chat?is_popout=1&v=abc123def45").IdempotencyKey);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=WH7l_CMYCfU")]
    [InlineData("https://www.youtube.com/live_chat_replay?is_popout=1&v=WH7l_CMYCfU")]
    [InlineData("http://www.youtube.com/live_chat?is_popout=1&v=WH7l_CMYCfU")]
    [InlineData("https://youtube.com.evil.example/live_chat?is_popout=1&v=WH7l_CMYCfU")]
    [InlineData("https://www.youtube.com/live_chat?is_popout=1&v=WH7l_CMYCfU&token=secret")]
    public void Locator_RejectsUnsupportedOrCredentialBearingUrls(string url)
    {
        Assert.False(YouTubeLiveChatSourceLocator.TryCreate(url, out _));
    }

    [Fact]
    public async Task Connect_EnsuresSameLiveChatSourceIdempotentlyAndOnlyRetiresKnownSameLiveDuplicate()
    {
        var handler = new FakeSsnHandler(CurrentVideoId);
        var clients = new SingleClientFactory(handler);
        var options = Options.Create(new SocialStreamNinjaOptions
        {
            Enabled = true,
            ConfigureSources = true,
            Endpoint = "http://ssn:17778",
            Twitch = new() { Enabled = true, Channel = "gfmaurila" },
            YouTube = new() { Enabled = true, Channel = "gfmaurila", AuthMode = "oauth", LiveChatUrl = LiveChatUrl },
            Kick = new() { Enabled = true, Channel = "gfmaurila" }
        });
        var provider = new SocialStreamNinjaLiveChatProvider(
            options,
            clients,
            new SocialStreamNinjaMessageMapper(options),
            sender: null!,
            NullLogger<SocialStreamNinjaLiveChatProvider>.Instance);

        await provider.ConnectAsync(CancellationToken.None);
        await provider.ConnectAsync(CancellationToken.None);

        Assert.Equal(1, handler.AddedLiveChatSources);
        Assert.Equal(2, handler.YoutubeAutoAddConfigurations);
        Assert.All(handler.Sources.Where(source => source.Target is "twitch" or "kick"), source => Assert.True(source.Active));
        Assert.True(handler.Sources.Single(source => source.Id == "youtube-url-managed-live").Active);
        Assert.False(handler.Sources.Single(source => source.Id == "youtube-url-same-live-watch").Active);
        Assert.True(handler.Sources.Single(source => source.Id == "youtube-url-older-live").Active);
    }

    private static YouTubeLiveChatSourceLocator CreateLocator(string url)
    {
        Assert.True(YouTubeLiveChatSourceLocator.TryCreate(url, out var locator));
        return locator!;
    }

    private sealed class SingleClientFactory(FakeSsnHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("http://ssn:17778/")
        };
    }

    private sealed class FakeSsnHandler : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
        private readonly Dictionary<string, FakeSource> _sourcesByIdempotencyKey = new(StringComparer.Ordinal);
        private readonly string _currentVideoId;

        public FakeSsnHandler(string currentVideoId)
        {
            _currentVideoId = currentVideoId;
            Sources =
            [
                new("twitch-user", "twitch", "gfmaurila", "", "classic", true),
                new("kick-user", "kick", "gfmaurila", "", "websocket", true),
                new("youtube-url-same-live-watch", "youtube", "", currentVideoId, "classic", true),
                new("youtube-url-older-live", "youtube", "", "OLDERLIVE01", "classic", true)
            ];
        }

        public List<FakeSource> Sources { get; }
        public int AddedLiveChatSources { get; private set; }
        public int YoutubeAutoAddConfigurations { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/api/v1/capabilities", StringComparison.Ordinal))
                return Json(new { ok = true, payload = new { available = true, ready = true } });

            if (request.RequestUri.AbsolutePath.EndsWith("/api/v1/events", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(string.Empty, Encoding.UTF8, "text/event-stream")
                };

            using var body = await JsonDocument.ParseAsync(
                await request.Content!.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            var action = body.RootElement.GetProperty("action").GetString();
            var value = body.RootElement.GetProperty("value");
            return action switch
            {
                "updateSettings" => UpdateSettings(value),
                "getSources" => Json(new { ok = true, payload = new { sources = Sources } }),
                "addSource" => AddSource(value),
                "startSource" => SetActive(value, true),
                "stopSource" => SetActive(value, false),
                "setSourceConnectionMode" => SetMode(value),
                _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            };
        }

        private HttpResponseMessage UpdateSettings(JsonElement value)
        {
            if (value.GetProperty("settings").GetProperty("youtubeAutoAdd").GetBoolean() == false)
                YoutubeAutoAddConfigurations++;
            return Json(new { ok = true, payload = new { } });
        }

        private HttpResponseMessage AddSource(JsonElement value)
        {
            var key = value.GetProperty("idempotencyKey").GetString()!;
            if (_sourcesByIdempotencyKey.TryGetValue(key, out var previous))
                return Json(new { ok = true, payload = new { source = previous } });

            var url = value.GetProperty("url").GetString()!;
            Assert.StartsWith("https://www.youtube.com/live_chat?is_popout=1&v=", url, StringComparison.Ordinal);
            var source = new FakeSource("youtube-url-managed-live", "youtube", "", _currentVideoId, "classic", false);
            Sources.Add(source);
            _sourcesByIdempotencyKey.Add(key, source);
            AddedLiveChatSources++;
            return Json(new { ok = true, payload = new { source } });
        }

        private HttpResponseMessage SetActive(JsonElement value, bool active)
        {
            var id = value.GetProperty("sourceId").GetString();
            Sources.Single(source => source.Id == id).Active = active;
            return Json(new { ok = true, payload = new { accepted = true } });
        }

        private HttpResponseMessage SetMode(JsonElement value)
        {
            var id = value.GetProperty("sourceId").GetString();
            Sources.Single(source => source.Id == id).ConnectionMode = value.GetProperty("mode").GetString()!;
            return Json(new { ok = true, payload = new { accepted = true } });
        }

        private static HttpResponseMessage Json<T>(T payload) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, WebJson), Encoding.UTF8, "application/json")
        };
    }

    private sealed class FakeSource(
        string id,
        string target,
        string username,
        string videoId,
        string connectionMode,
        bool active)
    {
        public string Id { get; } = id;
        public string Target { get; } = target;
        public string Username { get; } = username;
        public string VideoId { get; } = videoId;
        public string ConnectionMode { get; set; } = connectionMode;
        public bool Active { get; set; } = active;
        public string Status => Active ? "active" : "inactive";
    }
}
