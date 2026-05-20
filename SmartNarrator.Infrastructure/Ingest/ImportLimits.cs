namespace SmartNarrator.Infrastructure.Ingest;

public static class ImportLimits
{
    public const long MaxUrlDownloadBytes = 52_428_800; // 50 MiB — rough cap beyond headers
}
