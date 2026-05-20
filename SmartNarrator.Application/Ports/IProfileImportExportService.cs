namespace SmartNarrator.Application.Ports;

public sealed class ProfileBundleDto
{
    public int Version { get; init; } = 1;
    public Guid SourceWorkId { get; init; }
    public string Title { get; init; } = string.Empty;
    public IReadOnlyList<CharacterBundleItemDto> Characters { get; init; } = Array.Empty<CharacterBundleItemDto>();
    public IReadOnlyList<NarratorBundleDto> Narrators { get; init; } = Array.Empty<NarratorBundleDto>();
}

public sealed class CharacterBundleItemDto
{
    public Guid? Id { get; init; }
    public string? AiExternalKey { get; init; }
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<string>? Aliases { get; init; }
    public string? PersonalitySummary { get; init; }
    public string? SpeechStyleSummary { get; init; }
    public string GenderPresentation { get; init; } = "";
    public string Tone { get; init; } = "";
    public string Accent { get; init; } = "";
    public string Breathiness { get; init; } = "";
    public string SpeakingPace { get; init; } = "";
}

public sealed class NarratorBundleDto
{
    public Guid? NarratorCharacterId { get; init; }
    public int StartOffset { get; init; }
    public int EndOffset { get; init; }
    public string PerspectiveNotes { get; init; } = "";
    public string GenderPresentation { get; init; } = "";
    public string Tone { get; init; } = "";
    public string Accent { get; init; } = "";
    public string Breathiness { get; init; } = "";
    public string SpeakingPace { get; init; } = "";
}

public interface IProfileImportExportService
{
    Task<byte[]> ExportJsonAsync(Guid workId, CancellationToken cancellationToken);
    Task ImportIntoWorkAsync(Guid workId, Stream json, CancellationToken cancellationToken);
}
