using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared.CCVar;
using Prometheus;
using Robust.Shared.Configuration;

namespace Content.Server.TTS;

/// <summary>
/// Requests generated speech from the configured TTS service and keeps a bounded in-memory LRU cache.
/// </summary>
public sealed partial class TTSManager
{
    private static readonly Histogram RequestTimings = Metrics.CreateHistogram(
        "tts_request_duration_seconds",
        "Duration of TTS API requests.",
        new HistogramConfiguration
        {
            LabelNames = ["result"],
            Buckets = Histogram.ExponentialBuckets(.1, 1.5, 10),
        });

    private static readonly Counter RequestedLines = Metrics.CreateCounter(
        "tts_requested_lines_total",
        "Number of requested TTS lines.");

    private static readonly Counter ReusedLines = Metrics.CreateCounter(
        "tts_reused_lines_total",
        "Number of TTS lines reused from cache or an in-flight request.");

    [Dependency] private IConfigurationManager _cfg = default!;

    private readonly HttpClient _httpClient = new();
    private readonly Dictionary<string, CacheEntry> _cache = new();
    private readonly LinkedList<string> _cacheOrder = new();
    private readonly Dictionary<string, Task<byte[]?>> _pendingRequests = new();
    private readonly object _lock = new();

    private ISawmill _sawmill = default!;
    private int _maxCachedCount = 250;
    private int _maxCachedBytes = 64 * 1024 * 1024;
    private int _cacheTtlSeconds = 900;
    private int _maxResponseBytes = 4 * 1024 * 1024;
    private int _circuitBreakerFailures = 5;
    private int _circuitBreakerSeconds = 30;
    private int _cachedBytes;
    private int _consecutiveFailures;
    private DateTime _circuitOpenUntil;
    private string _apiUrl = string.Empty;
    private string _apiToken = string.Empty;
    private bool _usePost;

    public void Initialize()
    {
        _sawmill = Logger.GetSawmill("tts");

        _cfg.OnValueChanged(CCVars.TTSMaxCache, value =>
        {
            _maxCachedCount = Math.Max(0, value);
            TrimCache();
        }, true);
        _cfg.OnValueChanged(CCVars.TTSMaxCacheBytes, value =>
        {
            _maxCachedBytes = Math.Max(0, value);
            TrimCache();
        }, true);
        _cfg.OnValueChanged(CCVars.TTSCacheTtl, value => _cacheTtlSeconds = Math.Max(0, value), true);
        _cfg.OnValueChanged(CCVars.TTSMaxResponseBytes, value => _maxResponseBytes = Math.Max(1, value), true);
        _cfg.OnValueChanged(CCVars.TTSCircuitBreakerFailures, value => _circuitBreakerFailures = Math.Max(1, value), true);
        _cfg.OnValueChanged(CCVars.TTSCircuitBreakerSeconds, value => _circuitBreakerSeconds = Math.Max(1, value), true);
        _cfg.OnValueChanged(CCVars.TTSApiUrl, value =>
        {
            _apiUrl = value.Trim();
            ResetCache();
            ResetCircuitBreaker();
        }, true);
        _cfg.OnValueChanged(CCVars.TTSApiToken, value => _apiToken = value, true);
        _cfg.OnValueChanged(CCVars.TTSApiUsePost, value => _usePost = value, true);
    }

    public Task<byte[]?> ConvertTextToSpeech(string speaker, string text)
    {
        RequestedLines.Inc();

        var cacheKey = GenerateCacheKey(speaker, text);
        lock (_lock)
        {
            if (TryGetCached(cacheKey, out var cached))
            {
                ReusedLines.Inc();
                return Task.FromResult<byte[]?>(cached);
            }

            if (_pendingRequests.TryGetValue(cacheKey, out var pending))
            {
                ReusedLines.Inc();
                return pending;
            }

            var request = ConvertTextToSpeechInternal(speaker, text, cacheKey);
            _pendingRequests[cacheKey] = request;
            return request;
        }
    }

    private async Task<byte[]?> ConvertTextToSpeechInternal(string speaker, string text, string cacheKey)
    {
        var startedAt = DateTime.UtcNow;

        try
        {
            if (string.IsNullOrWhiteSpace(_apiUrl))
            {
                _sawmill.Warning("TTS request skipped because tts.api_url is not configured.");
                return null;
            }

            if (IsCircuitOpen())
            {
                RequestTimings.WithLabels("circuit_open").Observe((DateTime.UtcNow - startedAt).TotalSeconds);
                return null;
            }

            var timeout = Math.Max(1, _cfg.GetCVar(CCVars.TTSApiTimeout));
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
            using var request = CreateRequest(speaker, text);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation.Token);
            if (!response.IsSuccessStatusCode)
            {
                RequestTimings.WithLabels(response.StatusCode == HttpStatusCode.TooManyRequests ? "rate_limited" : "http_error")
                    .Observe((DateTime.UtcNow - startedAt).TotalSeconds);
                _sawmill.Warning($"TTS service returned {response.StatusCode} for speaker '{speaker}'.");
                RegisterFailure();
                return null;
            }

            if (!IsSupportedContentType(response))
            {
                RequestTimings.WithLabels("invalid_content_type").Observe((DateTime.UtcNow - startedAt).TotalSeconds);
                _sawmill.Warning($"TTS service returned unsupported content type for speaker '{speaker}'.");
                RegisterFailure();
                return null;
            }

            var audio = await ReadAudio(response, cancellation.Token);
            if (audio == null)
            {
                RequestTimings.WithLabels("invalid_audio").Observe((DateTime.UtcNow - startedAt).TotalSeconds);
                RegisterFailure();
                return null;
            }

            Cache(cacheKey, audio);
            RegisterSuccess();
            RequestTimings.WithLabels("success").Observe((DateTime.UtcNow - startedAt).TotalSeconds);
            return audio;
        }
        catch (OperationCanceledException)
        {
            RequestTimings.WithLabels("timeout").Observe((DateTime.UtcNow - startedAt).TotalSeconds);
            _sawmill.Warning("TTS request timed out.");
            RegisterFailure();
            return null;
        }
        catch (Exception exception)
        {
            RequestTimings.WithLabels("error").Observe((DateTime.UtcNow - startedAt).TotalSeconds);
            _sawmill.Error($"TTS request failed: {exception}");
            RegisterFailure();
            return null;
        }
        finally
        {
            lock (_lock)
            {
                _pendingRequests.Remove(cacheKey);
            }
        }
    }

    private HttpRequestMessage CreateRequest(string speaker, string text)
    {
        HttpRequestMessage request;
        if (_usePost)
        {
            var body = JsonSerializer.Serialize(new TTSRequest(speaker, text, "ogg"));
            request = new HttpRequestMessage(HttpMethod.Post, _apiUrl)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
        else
        {
            var separator = _apiUrl.Contains('?') ? "&" : "?";
            var url = $"{_apiUrl}{separator}speaker={Uri.EscapeDataString(speaker)}&text={Uri.EscapeDataString(text)}&ext=ogg";
            request = new HttpRequestMessage(HttpMethod.Get, url);
        }

        if (!string.IsNullOrWhiteSpace(_apiToken))
            request.Headers.Authorization = new("Bearer", _apiToken);

        request.Headers.Accept.ParseAdd("audio/ogg, application/ogg, application/octet-stream");
        return request;
    }

    private async Task<byte[]?> ReadAudio(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength is > 0 && contentLength > _maxResponseBytes)
        {
            _sawmill.Warning($"TTS service response exceeded the {_maxResponseBytes} byte limit.");
            return null;
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new MemoryStream(contentLength is > 0 ? (int) contentLength : 0);
        var buffer = ArrayPool<byte>.Shared.Rent(81920);

        try
        {
            while (true)
            {
                var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                    break;

                if (output.Length + read > _maxResponseBytes)
                {
                    _sawmill.Warning($"TTS service response exceeded the {_maxResponseBytes} byte limit.");
                    return null;
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        var audio = output.ToArray();
        if (audio.Length < 4 ||
            audio[0] != 'O' ||
            audio[1] != 'g' ||
            audio[2] != 'g' ||
            audio[3] != 'S')
        {
            _sawmill.Warning("TTS service returned data that is not an OGG stream.");
            return null;
        }

        return audio;
    }

    private static bool IsSupportedContentType(HttpResponseMessage response)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType;
        return contentType == null ||
               contentType.Equals("audio/ogg", StringComparison.OrdinalIgnoreCase) ||
               contentType.Equals("application/ogg", StringComparison.OrdinalIgnoreCase) ||
               contentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsCircuitOpen()
    {
        lock (_lock)
        {
            return _circuitOpenUntil > DateTime.UtcNow;
        }
    }

    private void RegisterSuccess()
    {
        lock (_lock)
        {
            _consecutiveFailures = 0;
            _circuitOpenUntil = DateTime.MinValue;
        }
    }

    private void RegisterFailure()
    {
        lock (_lock)
        {
            _consecutiveFailures++;
            if (_consecutiveFailures < _circuitBreakerFailures)
                return;

            _circuitOpenUntil = DateTime.UtcNow.AddSeconds(_circuitBreakerSeconds);
            _consecutiveFailures = 0;
            _sawmill.Warning($"TTS circuit breaker opened for {_circuitBreakerSeconds} seconds.");
        }
    }

    private void ResetCircuitBreaker()
    {
        lock (_lock)
        {
            _consecutiveFailures = 0;
            _circuitOpenUntil = DateTime.MinValue;
        }
    }

    private void Cache(string cacheKey, byte[] audio)
    {
        lock (_lock)
        {
            if (_maxCachedCount == 0 || _maxCachedBytes == 0 || audio.Length > _maxCachedBytes)
                return;

            RemoveCached(cacheKey);
            var node = _cacheOrder.AddFirst(cacheKey);
            _cache[cacheKey] = new CacheEntry(audio, DateTime.UtcNow.AddSeconds(_cacheTtlSeconds), node);
            _cachedBytes += audio.Length;
            TrimCache();
        }
    }

    private bool TryGetCached(string cacheKey, out byte[]? audio)
    {
        if (!_cache.TryGetValue(cacheKey, out var entry))
        {
            audio = null;
            return false;
        }

        if (entry.ExpiresAt <= DateTime.UtcNow)
        {
            RemoveCached(cacheKey);
            audio = null;
            return false;
        }

        _cacheOrder.Remove(entry.Node);
        _cacheOrder.AddFirst(entry.Node);
        audio = entry.Data;
        return true;
    }

    private void TrimCache()
    {
        lock (_lock)
        {
            while ((_cache.Count > _maxCachedCount || _cachedBytes > _maxCachedBytes) && _cacheOrder.Last is { } oldest)
            {
                RemoveCached(oldest.Value);
            }
        }
    }

    private void RemoveCached(string cacheKey)
    {
        if (!_cache.Remove(cacheKey, out var entry))
            return;

        _cacheOrder.Remove(entry.Node);
        _cachedBytes -= entry.Data.Length;
    }

    public void ResetCache()
    {
        lock (_lock)
        {
            _cache.Clear();
            _cacheOrder.Clear();
            _cachedBytes = 0;
        }
    }

    private static string GenerateCacheKey(string speaker, string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{speaker}/{text}"));
        return Convert.ToHexString(bytes);
    }

    private sealed record CacheEntry(byte[] Data, DateTime ExpiresAt, LinkedListNode<string> Node);

    private sealed record TTSRequest(
        [property: JsonPropertyName("speaker")] string Speaker,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("ext")] string Ext);
}
