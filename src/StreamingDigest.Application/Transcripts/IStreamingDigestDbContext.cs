using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using StreamingDigest.Domain;

namespace StreamingDigest.Application.Transcripts;

public interface IStreamingDigestDbContext
{
    DbSet<Video> Videos { get; }
    DbSet<Segment> Segments { get; }
    DbSet<VideoTranscript> VideoTranscripts { get; }
    DbSet<TranscriptCue> TranscriptCues { get; }
    DbSet<ExternalResource> ExternalResources { get; }
    DbSet<RepositoryRecord> Repositories { get; }
    DbSet<Note> Notes { get; }
    DbSet<DomainEvent> DomainEvents { get; }
    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
