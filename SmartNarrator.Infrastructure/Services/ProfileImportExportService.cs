using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartNarrator.Application.Ports;
using SmartNarrator.Domain.Entities;
using SmartNarrator.Infrastructure.Persistence;

namespace SmartNarrator.Infrastructure.Services;

public sealed class ProfileImportExportService(SmartNarratorDbContext db) : IProfileImportExportService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<byte[]> ExportJsonAsync(Guid workId, CancellationToken cancellationToken)
    {
        var work = await db.Works.Include(w => w.Characters).AsNoTracking().FirstOrDefaultAsync(w => w.Id == workId, cancellationToken)
                   ?? throw new InvalidOperationException("Work missing.");

        var characters = work.Characters.Select(c => new CharacterBundleItemDto
        {
            Id = c.Id,
            AiExternalKey = c.AiExternalKey,
            Name = c.Name,
            Aliases = DeserializeAliases(c.AliasesJson),
            PersonalitySummary = c.PersonalitySummary,
            SpeechStyleSummary = c.SpeechStyleSummary,
            GenderPresentation = c.GenderPresentation,
            Tone = c.Tone,
            Accent = c.Accent,
            Breathiness = c.Breathiness,
            SpeakingPace = c.SpeakingPace,
        }).ToArray();

        var narrators = await db.NarrativePassages.Where(p => p.WorkId == workId).OrderBy(p => p.StartOffset)
            .Select(p => new NarratorBundleDto
            {
                NarratorCharacterId = p.NarratorCharacterId,
                StartOffset = p.StartOffset,
                EndOffset = p.EndOffset,
                PerspectiveNotes = p.PerspectiveNotes,
                GenderPresentation = p.GenderPresentation,
                Tone = p.Tone,
                Accent = p.Accent,
                Breathiness = p.Breathiness,
                SpeakingPace = p.SpeakingPace,
            }).ToArrayAsync(cancellationToken);

        var bundle = new ProfileBundleDto { SourceWorkId = work.Id, Title = work.Title, Characters = characters, Narrators = narrators };

        return JsonSerializer.SerializeToUtf8Bytes(bundle, JsonOptions);
    }

    public async Task ImportIntoWorkAsync(Guid workId, Stream json, CancellationToken cancellationToken)
    {
        var bundle = await JsonSerializer.DeserializeAsync<ProfileBundleDto>(json, JsonOptions, cancellationToken)
                     ?? throw new InvalidOperationException("Invalid JSON bundle.");

        var workExists = await db.Works.AnyAsync(w => w.Id == workId, cancellationToken);
        if (!workExists)
            throw new InvalidOperationException("Work missing.");

        await db.AudioArtifacts.Where(a => a.WorkId == workId).ExecuteDeleteAsync(cancellationToken);
        await db.Utterances.Where(u => u.WorkId == workId).ExecuteDeleteAsync(cancellationToken);
        await db.NarrativePassages.Where(p => p.WorkId == workId).ExecuteDeleteAsync(cancellationToken);
        await db.Characters.Where(c => c.WorkId == workId).ExecuteDeleteAsync(cancellationToken);

        var idRemap = new Dictionary<Guid, Guid>();

        foreach (var c in bundle.Characters)
        {
            var newId = Guid.NewGuid();

            if (c.Id.HasValue && c.Id.Value != Guid.Empty)
                idRemap[c.Id.Value] = newId;

            var aliasesJson = c.Aliases is { Count: > 0 } ? JsonSerializer.Serialize(c.Aliases) : null;

            db.Characters.Add(new CharacterProfileEntity
            {
                Id = newId,
                WorkId = workId,
                AiExternalKey = string.IsNullOrWhiteSpace(c.AiExternalKey) ? null : c.AiExternalKey,
                Name = string.IsNullOrWhiteSpace(c.Name) ? "Unnamed" : c.Name.Trim(),
                AliasesJson = aliasesJson,
                PersonalitySummary = string.IsNullOrWhiteSpace(c.PersonalitySummary) ? null : c.PersonalitySummary.Trim(),
                SpeechStyleSummary =
                    string.IsNullOrWhiteSpace(c.SpeechStyleSummary) ? null : c.SpeechStyleSummary.Trim(),
                GenderPresentation =
                    string.IsNullOrWhiteSpace(c.GenderPresentation) ? "unspecified" : c.GenderPresentation,
                Tone = string.IsNullOrWhiteSpace(c.Tone) ? "neutral" : c.Tone,
                Accent = string.IsNullOrWhiteSpace(c.Accent) ? "none" : c.Accent,
                Breathiness = string.IsNullOrWhiteSpace(c.Breathiness) ? "normal" : c.Breathiness,
                SpeakingPace = string.IsNullOrWhiteSpace(c.SpeakingPace) ? "normal" : c.SpeakingPace,
                IsAiSuggested = false,
                UserApproved = true,
            });
        }

        foreach (var n in bundle.Narrators)
        {
            Guid? narratorId = null;
            if (n.NarratorCharacterId is { } nid && idRemap.TryGetValue(nid, out var mapped))
                narratorId = mapped;

            db.NarrativePassages.Add(new NarrativePassageEntity
            {
                Id = Guid.NewGuid(),
                WorkId = workId,
                StartOffset = n.StartOffset,
                EndOffset = n.EndOffset,
                NarratorCharacterId = narratorId,
                PerspectiveNotes = n.PerspectiveNotes ?? string.Empty,
                GenderPresentation =
                    string.IsNullOrWhiteSpace(n.GenderPresentation) ? "unspecified" : n.GenderPresentation,
                Tone = string.IsNullOrWhiteSpace(n.Tone) ? "neutral" : n.Tone,
                Accent = string.IsNullOrWhiteSpace(n.Accent) ? "none" : n.Accent,
                Breathiness = string.IsNullOrWhiteSpace(n.Breathiness) ? "normal" : n.Breathiness,
                SpeakingPace = string.IsNullOrWhiteSpace(n.SpeakingPace) ? "normal" : n.SpeakingPace,
                IsAiSuggested = false,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static List<string>? DeserializeAliases(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json);
        }
        catch
        {
            return null;
        }
    }
}
