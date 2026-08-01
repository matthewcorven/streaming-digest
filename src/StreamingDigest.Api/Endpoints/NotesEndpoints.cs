using Microsoft.EntityFrameworkCore;
using StreamingDigest.Application;
using StreamingDigest.Domain;
using StreamingDigest.Infrastructure.Persistence.EntityFramework;

namespace StreamingDigest.Api.Endpoints;

internal static class NotesEndpoints
{
    public static void MapNoteEndpoints(this WebApplication app)
    {
        app.MapGet("/api/notes", async (string? targetType, Guid? targetId, StreamingDigestDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var query = dbContext.Notes.AsNoTracking().Where(n => n.DeletedAt == null);
            if (!string.IsNullOrWhiteSpace(targetType))
            {
                query = query.Where(n => n.TargetType == targetType);
            }
            if (targetId.HasValue)
            {
                query = query.Where(n => n.TargetId == targetId.Value);
            }

            var notes = await query
                .OrderByDescending(n => n.UpdatedAt)
                .Select(n => new NoteResponse(n.Id, n.TargetType, n.TargetId, n.Title, n.Markdown, n.EmbeddingStatus, n.CreatedAt, n.UpdatedAt))
                .ToListAsync(cancellationToken);

            return Results.Ok(new { items = notes });
        });

        app.MapPost("/api/notes", async (CreateNoteRequest request, StreamingDigestDbContext dbContext, ISearchDocumentRegenerationService regenerationService, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.TargetType))
            {
                return Results.BadRequest(new { error = "targetType is required." });
            }

            if (request.TargetId == Guid.Empty)
            {
                return Results.BadRequest(new { error = "targetId is required." });
            }

            if (string.IsNullOrWhiteSpace(request.Markdown))
            {
                return Results.BadRequest(new { error = "markdown is required." });
            }

            var liveNoteExists = await dbContext.Notes
                .AnyAsync(n => n.TargetType == request.TargetType && n.TargetId == request.TargetId && n.DeletedAt == null, cancellationToken);

            if (liveNoteExists)
            {
                return Results.Conflict(new { error = "A live note already exists for this target. Use PUT to update it." });
            }

            var note = new Note
            {
                TargetType = request.TargetType.Trim(),
                TargetId = request.TargetId,
                Title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim(),
                Markdown = request.Markdown.Trim(),
                EmbeddingStatus = "stale"
            };

            dbContext.Notes.Add(note);
            await dbContext.SaveChangesAsync(cancellationToken);
            await regenerationService.RegenerateForEntityAsync("note", note.Id, cancellationToken);

            var response = new NoteResponse(note.Id, note.TargetType, note.TargetId, note.Title, note.Markdown, note.EmbeddingStatus, note.CreatedAt, note.UpdatedAt);
            return Results.Created($"/api/notes/{note.Id}", response);
        });

        app.MapGet("/api/notes/{noteId:guid}", async (Guid noteId, StreamingDigestDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var note = await dbContext.Notes
                .AsNoTracking()
                .Where(n => n.Id == noteId && n.DeletedAt == null)
                .Select(n => new NoteResponse(n.Id, n.TargetType, n.TargetId, n.Title, n.Markdown, n.EmbeddingStatus, n.CreatedAt, n.UpdatedAt))
                .SingleOrDefaultAsync(cancellationToken);

            return note is null ? Results.NotFound() : Results.Ok(note);
        });

        app.MapPut("/api/notes/{noteId:guid}", async (Guid noteId, UpdateNoteRequest request, StreamingDigestDbContext dbContext, ISearchDocumentRegenerationService regenerationService, CancellationToken cancellationToken) =>
        {
            var note = await dbContext.Notes.SingleOrDefaultAsync(n => n.Id == noteId && n.DeletedAt == null, cancellationToken);
            if (note is null)
            {
                return Results.NotFound();
            }

            if (request.Title is not null)
            {
                note.Title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim();
            }

            if (request.Markdown is not null)
            {
                if (string.IsNullOrWhiteSpace(request.Markdown))
                {
                    return Results.BadRequest(new { error = "markdown cannot be blank." });
                }

                note.Markdown = request.Markdown.Trim();
            }

            note.EmbeddingStatus = "stale";
            await dbContext.SaveChangesAsync(cancellationToken);
            await regenerationService.RegenerateForEntityAsync("note", note.Id, cancellationToken);

            var response = new NoteResponse(note.Id, note.TargetType, note.TargetId, note.Title, note.Markdown, note.EmbeddingStatus, note.CreatedAt, note.UpdatedAt);
            return Results.Ok(new { status = "updated", entityType = "note", entityId = note.Id, resource = response });
        });

        app.MapDelete("/api/notes/{noteId:guid}", async (Guid noteId, StreamingDigestDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var note = await dbContext.Notes.SingleOrDefaultAsync(n => n.Id == noteId && n.DeletedAt == null, cancellationToken);
            if (note is null)
            {
                return Results.NotFound();
            }

            note.DeletedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Ok(new { status = "deleted", entityType = "note", entityId = noteId });
        });
    }
}

internal sealed record CreateNoteRequest(string TargetType, Guid TargetId, string? Title, string Markdown);
internal sealed record UpdateNoteRequest(string? Title, string? Markdown);
internal sealed record NoteResponse(Guid Id, string TargetType, Guid TargetId, string? Title, string Markdown, string EmbeddingStatus, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);