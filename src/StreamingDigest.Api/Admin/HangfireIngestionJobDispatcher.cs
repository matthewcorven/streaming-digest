extern alias StreamingDigestWorker;

using Hangfire;
using StreamingDigest.Application.Admin;
using StreamingDigest.Application.Orchestration;

namespace StreamingDigest.Api.Admin;

/// <summary>
/// Production implementation of <see cref="IIngestionJobDispatcher"/> that enqueues
/// channel ingestion jobs via Hangfire's <see cref="IBackgroundJobClient"/>.
/// </summary>
internal sealed class HangfireIngestionJobDispatcher(IBackgroundJobClient backgroundJobs) : IIngestionJobDispatcher
{
    public string EnqueueChannelIngestion(ChannelIngestionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return backgroundJobs.Enqueue<StreamingDigestWorker::StreamingDigest.Worker.ChannelIngestion.ChannelIngestionJob>(
            job => job.RunAsync(request, CancellationToken.None));
    }
}
