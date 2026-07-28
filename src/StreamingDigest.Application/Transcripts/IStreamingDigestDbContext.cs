using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using StreamingDigest.Domain;

namespace StreamingDigest.Application.Transcripts;

public interface IStreamingDigestDbContext
{
    DbSet<Video> Videos { get; }
    DbSet<VideoTranscript> VideoTranscripts { get; }
    DbSet<TranscriptCue> TranscriptCues { get; }
    DbSet<DomainEvent> DomainEvents { get; }
    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
