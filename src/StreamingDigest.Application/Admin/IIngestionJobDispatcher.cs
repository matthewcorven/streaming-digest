using StreamingDigest.Application.Orchestration;

namespace StreamingDigest.Application.Admin;

/// <summary>
/// Dispatches ingestion jobs to the background job queue (Hangfire).
/// Abstracted so the Application layer has no dependency on Hangfire.
/// The API registers <c>HangfireIngestionJobDispatcher</c> as the production implementation.
/// </summary>
public interface IIngestionJobDispatcher
{
    /// <summary>
    /// Enqueues a channel ingestion job and returns the Hangfire job ID.
    /// </summary>
    string EnqueueChannelIngestion(ChannelIngestionRequest request);
}
