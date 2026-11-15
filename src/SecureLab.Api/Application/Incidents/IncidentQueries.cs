using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Application.Incidents;

public sealed class IncidentQueries(SecureLabDbContext dbContext, ILogger<IncidentQueries> logger)
{
    public async Task<IReadOnlyList<IncidentListItemResponse>> GetListAsync(
        IncidentStatus? status,
        IncidentActor actor,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading incidents with status filter {Status}", status);

        // Query scoping: недозволені рядки не завантажуються взагалі.
        var query = dbContext.Incidents.AsNoTracking().VisibleTo(actor);
        if (status is not null)
        {
            query = query.Where(incident => incident.Status == status);
        }

        return await query
            .OrderByDescending(incident => incident.CreatedAtUtc)
            .Select(incident => new IncidentListItemResponse(
                incident.Id,
                incident.Title,
                incident.Severity.ToString(),
                incident.Status.ToString(),
                incident.OccurredAtUtc,
                incident.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public Task<IncidentDetailsResponse?> GetDetailsAsync(
        Guid id,
        IncidentActor actor,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading incident {IncidentId}", id);

        // Internal comment бачить лише Analyst, і лише після перевірки доступу до батьківського incident.
        var includeInternal = actor.IsAnalyst;

        return dbContext.Incidents
            .AsNoTracking()
            .VisibleTo(actor)
            .Where(incident => incident.Id == id)
            .Select(incident => new IncidentDetailsResponse(
                incident.Id,
                incident.Title,
                incident.Description,
                incident.Severity.ToString(),
                incident.Status.ToString(),
                incident.OccurredAtUtc,
                incident.CreatedAtUtc,
                incident.Owner.DisplayName,
                incident.Comments
                    .Where(comment => includeInternal || !comment.IsInternal)
                    .OrderBy(comment => comment.CreatedAtUtc)
                    .Select(comment => new IncidentCommentResponse(
                        comment.Id,
                        comment.Author.DisplayName,
                        comment.Text,
                        comment.CreatedAtUtc))
                    .ToList()))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<IncidentCommentResponse?> GetCommentAsync(
        Guid id,
        IncidentActor actor,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading incident comment {CommentId}", id);

        // Те саме правило, що й у details: батьківський incident має бути доступним,
        // а internal comment доступний лише Analyst.
        var userId = actor.UserId;
        var isAnalyst = actor.IsAnalyst;

        return dbContext.IncidentComments
            .AsNoTracking()
            .Where(comment => comment.Id == id)
            .Where(comment => isAnalyst || (!comment.IsInternal && comment.Incident.OwnerUserId == userId))
            .Select(comment => new IncidentCommentResponse(
                comment.Id,
                comment.Author.DisplayName,
                comment.Text,
                comment.CreatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<IncidentSeveritySummaryResponse>> GetSeveritySummaryAsync(
        IncidentStatus? status,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Loading incident severity summary with status filter {Status}", status);

        var query = dbContext.Incidents.AsNoTracking();
        if (status is not null)
        {
            query = query.Where(incident => incident.Status == status);
        }

        var grouped = await query
            .GroupBy(incident => incident.Severity)
            .Select(g => new { Severity = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var bySeverity = grouped.ToDictionary(x => x.Severity, x => x.Count);

        var result = Enum.GetValues<IncidentSeverity>()
            .OrderBy(severity => (int)severity)
            .Select(severity => new IncidentSeveritySummaryResponse(
                severity.ToString(),
                bySeverity.TryGetValue(severity, out var count) ? count : 0))
            .ToList();

        logger.LogInformation(
            "Loaded incident severity summary with {GroupCount} groups", result.Count);

        return result;
    }
}
