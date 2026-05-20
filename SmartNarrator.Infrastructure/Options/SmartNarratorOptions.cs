namespace SmartNarrator.Infrastructure.Options;

public sealed class StorageOptions
{
    public string RelativeRoot { get; init; } = "App_Data/storage";
}

public sealed class OllamaOptions
{
    public Uri BaseUri { get; init; } = new("http://localhost:11434");
    public string Model { get; init; } = "llama3.2";
    /// <summary>Characters of canonical prose per analyze chunk; full stories run as sequential overlapping chunks.</summary>
    public int MaxCharactersPerRequest { get; init; } = 56000;

    /// <summary>UTF-16 overlap between consecutive chunks so dialogue near boundaries is not lost.</summary>
    public int AnalysisChunkOverlapUtf16 { get; init; } = 640;

    /// <summary>
    /// Maximum UTF-16 units to extend each chunk backward when adding the previous paragraph (and/or prior segment) for continuity.
    /// </summary>
    public int AnalysisPriorParagraphMaxBackUtf16 { get; init; } = 24000;

    /// <summary>Utterances below this confidence (or missing attribution) require speaker review in the UI.</summary>
    public double SpeakerConfidenceNeedsReviewThreshold { get; init; } = 0.62;

    /// <summary>
    /// At or above this confidence, resolved speaker links skip human review unless attribution could not be resolved.
    /// Also enables fuzzy matching by character display name / aliases when the AI key does not match <see cref="CharacterProfileEntity.AiExternalKey"/>.
    /// </summary>
    public double SpeakerConfidenceAutoTrustThreshold { get; init; } = 0.8;
    /// <summary>Sampling temperature for analysis only (0 = deterministic; slightly higher improves nuanced inference at cost of consistency).</summary>
    public double AnalysisTemperature { get; init; } = 0.28;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// When true, append each phased-analysis / ingest-shift LLM prompt and assistant payload to NDJSON files under Storage:RelativeRoot/llm-diagnostics.
    /// </summary>
    public bool CaptureAnalyzeLlmTurns { get; init; }

    /// <summary>Max UTF-16 code units stored per captured prompt / response field (very large excerpts are truncated).</summary>
    public int AnalyzeDiagnosticsMaxCharsPerField { get; init; } = 200_000;
}

public sealed class SpeechSynthesisOptions
{
    /// <summary>Base URL for an OpenAI-compatible speech endpoint.</summary>
    public Uri BaseUri { get; init; } = new("http://localhost:8899");
    public string RelativePath { get; init; } = "/v1/audio/speech";
    public string DefaultModel { get; init; } = "tts";
    public string DefaultVoiceId { get; init; } = "alloy";
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
}

/// <summary>Background job dispatch from API (<see cref="DispatchMode"/>).</summary>
public sealed class JobsOptions
{
    public const string SectionName = "Jobs";

    /// <summary><c>InProcess</c> — poll Postgres from API host; <c>RabbitMq</c> — publish to broker.</summary>
    public string DispatchMode { get; init; } = "InProcess";

    /// <summary>Worker HTTP callbacks use this base URL (must reach this API instance).</summary>
    public string NotifyApiBaseUrl { get; init; } = "http://localhost:5022/";

    /// <summary>Shared secret for POST /internal/jobs/notify from worker containers.</summary>
    public string InternalNotifyApiKey { get; init; } = "";

    /// <summary>Prefetch count for non-AI jobs queue (ingest).</summary>
    public int RabbitMqGeneralPrefetch { get; init; } = 4;
}

/// <summary>RabbitMQ broker connection.</summary>
public sealed class RabbitMqConnectionOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; init; } = "localhost";

    public int Port { get; init; } = 5672;

    public string VirtualHost { get; init; } = "/";

    public string UserName { get; init; } = "guest";

    public string Password { get; init; } = "guest";
}
