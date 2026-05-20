using SmartNarrator.Application.Analysis;

namespace SmartNarrator.Application.Ports;

public interface IStructuredStoryAnalysisClient
{
    /// <summary>Analyze one UTF-16 excerpt slice of canonical prose (offsets in JSON relative to excerpt only).</summary>
    /// <param name="primaryAnnotationFocusStartRelativeUtf16">
    /// When non-zero, the excerpt begins with earlier prose for continuity; model should prioritize spans from this UTF-16 index onward (relative to the excerpt).
    /// </param>
    Task<StoryAnalysisResultDto> AnalyzeExcerptAsync(
        string excerptUtf16,
        int excerptGlobalStartUtf16,
        int fullCanonicalUtf16Length,
        IReadOnlyList<SegmentBoundaryDto> segmentsRelativeToExcerpt,
        string? priorCharacterRegistryJson,
        int primaryAnnotationFocusStartRelativeUtf16,
        CancellationToken cancellationToken);
}
