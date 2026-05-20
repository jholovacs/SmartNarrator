using SmartNarrator.Application.Analysis;

namespace SmartNarrator.Application.Ports;

public interface IWorkAnalysisCoordinator
{
    Task<Guid> EnqueueAnalyzeAsync(Guid workId, CancellationToken cancellationToken);
}

public interface ISpeechRenderingCoordinator
{
    Task<Guid> EnqueueRenderAsync(Guid workId, CancellationToken cancellationToken);
}

public interface IStoryAnalysisResultApplier
{
    Task ApplyAsync(Guid workId, StoryAnalysisResultDto result, CancellationToken cancellationToken);
}
