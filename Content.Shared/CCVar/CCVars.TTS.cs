using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    public static readonly CVarDef<bool> TTSEnabled =
        CVarDef.Create("tts.enabled", false, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    public static readonly CVarDef<string> TTSApiUrl =
        CVarDef.Create("tts.api_url", string.Empty, CVar.SERVERONLY | CVar.CONFIDENTIAL | CVar.ARCHIVE);

    public static readonly CVarDef<string> TTSApiToken =
        CVarDef.Create("tts.api_token", string.Empty, CVar.SERVERONLY | CVar.CONFIDENTIAL);

    public static readonly CVarDef<int> TTSApiTimeout =
        CVarDef.Create("tts.api_timeout", 5, CVar.SERVERONLY | CVar.ARCHIVE);

    public static readonly CVarDef<bool> TTSApiUsePost =
        CVarDef.Create("tts.api_use_post", false, CVar.SERVERONLY | CVar.ARCHIVE);

    public static readonly CVarDef<int> TTSMaxResponseBytes =
        CVarDef.Create("tts.max_response_bytes", 4 * 1024 * 1024, CVar.SERVERONLY | CVar.ARCHIVE);

    public static readonly CVarDef<float> TTSVolume =
        CVarDef.Create("tts.volume", 1f, CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<float> TTSRadioVolume =
        CVarDef.Create("tts.radio_volume", 1f, CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<int> TTSRadioQueueLimit =
        CVarDef.Create("tts.radio_queue_limit", 8, CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<bool> LocalTTSEnabled =
        CVarDef.Create("tts.local_enabled", true, CVar.CLIENT | CVar.ARCHIVE | CVar.REPLICATED);

    public static readonly CVarDef<bool> LocalRadioTTSEnabled =
        CVarDef.Create("tts.local_radio_enabled", true, CVar.CLIENT | CVar.ARCHIVE | CVar.REPLICATED);

    public static readonly CVarDef<int> TTSMaxCache =
        CVarDef.Create("tts.max_cache", 250, CVar.SERVERONLY | CVar.ARCHIVE);

    public static readonly CVarDef<int> TTSMaxCacheBytes =
        CVarDef.Create("tts.max_cache_bytes", 64 * 1024 * 1024, CVar.SERVERONLY | CVar.ARCHIVE);

    public static readonly CVarDef<int> TTSCacheTtl =
        CVarDef.Create("tts.cache_ttl", 900, CVar.SERVERONLY | CVar.ARCHIVE);

    public static readonly CVarDef<int> TTSCircuitBreakerFailures =
        CVarDef.Create("tts.circuit_breaker_failures", 5, CVar.SERVERONLY | CVar.ARCHIVE);

    public static readonly CVarDef<int> TTSCircuitBreakerSeconds =
        CVarDef.Create("tts.circuit_breaker_seconds", 30, CVar.SERVERONLY | CVar.ARCHIVE);

    public static readonly CVarDef<float> TTSRateLimitPeriod =
        CVarDef.Create("tts.rate_limit_period", 2f, CVar.SERVERONLY);

    public static readonly CVarDef<int> TTSRateLimitCount =
        CVarDef.Create("tts.rate_limit_count", 3, CVar.SERVERONLY);
}
