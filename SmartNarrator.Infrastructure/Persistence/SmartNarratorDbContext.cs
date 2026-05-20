using Microsoft.EntityFrameworkCore;
using SmartNarrator.Domain.Entities;

namespace SmartNarrator.Infrastructure.Persistence;

public sealed class SmartNarratorDbContext(DbContextOptions<SmartNarratorDbContext> options) : DbContext(options)
{
    public DbSet<WorkEntity> Works => Set<WorkEntity>();
    public DbSet<SourceDocumentEntity> SourceDocuments => Set<SourceDocumentEntity>();
    public DbSet<TextSegmentEntity> Segments => Set<TextSegmentEntity>();
    public DbSet<CharacterProfileEntity> Characters => Set<CharacterProfileEntity>();
    public DbSet<UtteranceEntity> Utterances => Set<UtteranceEntity>();
    public DbSet<NarrativePassageEntity> NarrativePassages => Set<NarrativePassageEntity>();
    public DbSet<StoryStructureSectionEntity> StoryStructureSections => Set<StoryStructureSectionEntity>();
    public DbSet<WorkChapterEntity> WorkChapters => Set<WorkChapterEntity>();
    public DbSet<DialogueSpanEntity> DialogueSpans => Set<DialogueSpanEntity>();
    public DbSet<BackgroundJobEntity> BackgroundJobs => Set<BackgroundJobEntity>();
    public DbSet<AudioArtifactEntity> AudioArtifacts => Set<AudioArtifactEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkEntity>(entity =>
        {
            entity.ToTable("works");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(512).IsRequired();
            entity.Property(x => x.Language).HasMaxLength(32).IsRequired();
            entity.Property(x => x.CanonicalText).IsRequired();
            entity.Property(x => x.CreatedUtc).IsRequired();
        });

        modelBuilder.Entity<SourceDocumentEntity>(entity =>
        {
            entity.ToTable("source_documents");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.StoredRelativePath).HasMaxLength(1024);
            entity.Property(x => x.OriginalFileName).HasMaxLength(512);
            entity.HasOne(x => x.Work)
                .WithMany(w => w.SourceDocuments)
                .HasForeignKey(x => x.WorkId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TextSegmentEntity>(entity =>
        {
            entity.ToTable("text_segments");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.OrderIndex).IsRequired();
            entity.Property(x => x.StartOffset).IsRequired();
            entity.Property(x => x.EndOffset).IsRequired();
            entity.HasIndex(x => new { x.WorkId, x.OrderIndex }).IsUnique();
            entity.HasOne(x => x.Work)
                .WithMany(w => w.Segments)
                .HasForeignKey(x => x.WorkId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CharacterProfileEntity>(entity =>
        {
            entity.ToTable("characters");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AiExternalKey).HasMaxLength(160);
            entity.Property(x => x.Name).HasMaxLength(256).IsRequired();
            entity.Property(x => x.PersonalitySummary);
            entity.Property(x => x.SpeechStyleSummary);
            entity.HasIndex(x => new { x.WorkId, x.AiExternalKey }).IsUnique();
            foreach (var p in new[]
                     {
                         nameof(CharacterProfileEntity.GenderPresentation),
                         nameof(CharacterProfileEntity.Tone),
                         nameof(CharacterProfileEntity.Accent),
                         nameof(CharacterProfileEntity.Breathiness),
                         nameof(CharacterProfileEntity.SpeakingPace),
                     })
            {
                entity.Property(p).HasMaxLength(256);
            }

            entity.HasOne(x => x.Work)
                .WithMany(w => w.Characters)
                .HasForeignKey(x => x.WorkId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UtteranceEntity>(entity =>
        {
            entity.ToTable("utterances");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SpeakerKind).HasConversion<string>().HasMaxLength(32);

            entity.HasOne(x => x.Work)
                .WithMany(w => w.Utterances)
                .HasForeignKey(x => x.WorkId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Character)
                .WithMany()
                .HasForeignKey(x => x.CharacterId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(x => new { x.WorkId, x.StartOffset });
        });

        modelBuilder.Entity<NarrativePassageEntity>(entity =>
        {
            entity.ToTable("narrative_passages");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PerspectiveNotes).HasMaxLength(1024);

            foreach (var p in new[]
                     {
                         nameof(NarrativePassageEntity.GenderPresentation),
                         nameof(NarrativePassageEntity.Tone),
                         nameof(NarrativePassageEntity.Accent),
                         nameof(NarrativePassageEntity.Breathiness),
                         nameof(NarrativePassageEntity.SpeakingPace),
                     })
            {
                entity.Property(p).HasMaxLength(256);
            }

            entity.HasOne(x => x.Work)
                .WithMany(w => w.NarrativePassages)
                .HasForeignKey(x => x.WorkId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.NarratorCharacter)
                .WithMany()
                .HasForeignKey(x => x.NarratorCharacterId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(x => new { x.WorkId, x.StartOffset });
        });

        modelBuilder.Entity<StoryStructureSectionEntity>(entity =>
        {
            entity.ToTable("story_structure_sections");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(48).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(512);
            entity.Property(x => x.Notes).HasMaxLength(2048).IsRequired();

            entity.HasOne(x => x.Work)
                .WithMany(w => w.StoryStructureSections)
                .HasForeignKey(x => x.WorkId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.WorkId, x.StartOffset });
        });

        modelBuilder.Entity<WorkChapterEntity>(entity =>
        {
            entity.ToTable("work_chapters");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(512);
            entity.Property(x => x.Notes).HasMaxLength(2048).IsRequired();
            entity.HasIndex(x => new { x.WorkId, x.OrderIndex }).IsUnique();

            entity.HasOne(x => x.Work)
                .WithMany(w => w.WorkChapters)
                .HasForeignKey(x => x.WorkId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DialogueSpanEntity>(entity =>
        {
            entity.ToTable("dialogue_spans");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SpeakerKind).HasConversion<string>().HasMaxLength(32);

            entity.HasOne(x => x.Work)
                .WithMany(w => w.DialogueSpans)
                .HasForeignKey(x => x.WorkId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Chapter)
                .WithMany(c => c.DialogueSpans)
                .HasForeignKey(x => x.ChapterId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.WorkId, x.StartOffset });
            entity.HasIndex(x => new { x.ChapterId, x.OrderIndexInChapter }).IsUnique();
        });

        modelBuilder.Entity<BackgroundJobEntity>(entity =>
        {
            entity.ToTable("background_jobs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(64).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.PayloadJson);
            entity.Property(x => x.ErrorMessage).HasMaxLength(8192);
            entity.Property(x => x.ProgressPhase).HasMaxLength(1024);
            entity.Property(x => x.CancellationRequested).HasDefaultValue(false);
            entity.Property(x => x.UpdatedUtc).IsRequired();

            entity.HasOne(x => x.Work)
                .WithMany(w => w.BackgroundJobs)
                .HasForeignKey(x => x.WorkId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.Status, x.CreatedUtc });
        });

        modelBuilder.Entity<AudioArtifactEntity>(entity =>
        {
            entity.ToTable("audio_artifacts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RelativePath).HasMaxLength(2048).IsRequired();
            entity.Property(x => x.MimeType).HasMaxLength(128).IsRequired();

            entity.HasOne(x => x.Work)
                .WithMany(w => w.AudioArtifacts)
                .HasForeignKey(x => x.WorkId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.WorkId);
        });
    }

    private void StampBackgroundJobUpdates()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var e in ChangeTracker.Entries<BackgroundJobEntity>())
        {
            if (e.State == EntityState.Added)
            {
                if (e.Entity.UpdatedUtc == default)
                    e.Entity.UpdatedUtc = e.Entity.CreatedUtc != default ? e.Entity.CreatedUtc : now;
            }
            else if (e.State == EntityState.Modified)
                e.Entity.UpdatedUtc = now;
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampBackgroundJobUpdates();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        StampBackgroundJobUpdates();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
