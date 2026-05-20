using Microsoft.Extensions.Logging;
using SmartNarrator.Application.Analysis;
using SmartNarrator.Application.Ports;
using SmartNarrator.Infrastructure.Ai;
using SmartNarrator.Infrastructure.Options;

namespace SmartNarrator.Infrastructure.Ingest;

internal static class IngestChapterShiftDetector
{
    internal static async Task<List<ChapterPartitionNormalizer.GlobalChapterBoundary>> DetectNormalizedAsync(
        OllamaStoryAnalysisClient ollama,
        ILogger logger,
        string canonical,
        IReadOnlyList<SegmentBoundaryDto> segments,
        OllamaOptions opts,
        Func<int, int, int, int, bool, CancellationToken, Task>? pulseChapterChunkAsync,
        Guid jobId,
        Guid workId,
        IAnalyzeLlmDiagnosticsSink llmDiagnostics,
        CancellationToken cancellationToken)
    {
        var maxChunk = Math.Max(2048, opts.MaxCharactersPerRequest);
        var overlap = Math.Clamp(opts.AnalysisChunkOverlapUtf16, 0, maxChunk / 2);
        var priorParagraphMaxBack = opts.AnalysisPriorParagraphMaxBackUtf16;

        var chunks = StoryAnalysisChunkPlanner.Plan(canonical.Length, maxChunk, overlap);
        var collected = new List<ChapterPartitionNormalizer.GlobalChapterBoundary>();
        var storyLen = canonical.Length;
        var totalChunks = Math.Max(chunks.Count, 1);

        for (var i = 0; i < chunks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (plannedStart, lengthUtf16) = chunks[i];
            var excerptStart = StoryAnalysisChunkPlanner.ExcerptStartWithPriorParagraphContext(
                canonical,
                segments,
                plannedStart,
                priorParagraphMaxBack);

            var excerptEnd = Math.Min(canonical.Length, plannedStart + lengthUtf16);
            var excerptLen = excerptEnd - excerptStart;
            if (excerptLen <= 0)
                continue;

            if (pulseChapterChunkAsync is not null)
                await pulseChapterChunkAsync(i, totalChunks, excerptStart, storyLen, false, cancellationToken)
                    .ConfigureAwait(false);

            var excerpt = canonical.Substring(excerptStart, excerptLen);
            var prompt = StoryIngestPrompts.MajorContextShiftPrompt(excerpt, excerptStart, canonical.Length);

            var json =
                await ollama.CompleteStructuredPromptAsync(PhasedAnalysisSchemas.ChapterDetectionRoot(), prompt,
                    cancellationToken).ConfigureAwait(false);

            await llmDiagnostics.RecordTurnAsync(jobId, workId,
                    $"Ingest · major shift detection · chunk {i + 1}/{totalChunks}",
                    string.IsNullOrWhiteSpace(opts.Model) ? "(model)" : opts.Model.Trim(), prompt, json, cancellationToken)
                .ConfigureAwait(false);

            var rows = PhasedAnalysisJsonParser.ParseChapterPhase(json, excerpt.Length, logger);
            foreach (var row in rows)
            {
                var gs = excerptStart + row.Start;
                var ge = excerptStart + row.End;
                int? hs = row.HeadingStart is { } h ? excerptStart + h : null;
                int? he = row.HeadingEnd is { } e ? excerptStart + e : null;
                collected.Add(new ChapterPartitionNormalizer.GlobalChapterBoundary(gs, ge, row.Title, hs, he));
            }

            if (pulseChapterChunkAsync is not null)
                await pulseChapterChunkAsync(i, totalChunks, excerptStart, storyLen, true, cancellationToken)
                    .ConfigureAwait(false);
        }

        return ChapterPartitionNormalizer.NormalizePartition(canonical.Length, collected);
    }
}
