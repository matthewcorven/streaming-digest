namespace StreamingDigest.Domain;

public sealed class Digest : AuditedEntity
{
    public Digest(Guid ingestionRunId, string runType)
    {
        IngestionRunId = ingestionRunId;
        RunType = runType;
    }

    public Guid Id { get; set; }
    public Guid IngestionRunId { get; set; }
    public string RunType { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
}
