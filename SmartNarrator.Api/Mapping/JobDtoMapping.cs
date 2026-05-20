using SmartNarrator.Api.Contracts;
using SmartNarrator.Domain.Entities;

namespace SmartNarrator.Api.Mapping;

public static class JobDtoMapping
{
    public static JobDto FromEntity(BackgroundJobEntity j) =>
        new(
            j.Id,
            j.Type,
            j.Status,
            j.ProgressPercent,
            j.ProgressPhase,
            j.WorkId,
            j.PayloadJson,
            j.ErrorMessage,
            j.CancellationRequested,
            j.CreatedUtc,
            j.UpdatedUtc,
            j.StartedUtc,
            j.CompletedUtc);
}
