using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Application.Incidents;

public sealed class IncidentCommands(SecureLabDbContext dbContext)
{
    public async Task<CommandOutcome> UpdateContentAsync(
        Guid id,
        IncidentActor actor,
        string title,
        string description,
        CancellationToken cancellationToken)
    {
        // Tracked entity: зміни зберігаються лише після рішення про доступ.
        var incident = await dbContext.Incidents
            .VisibleTo(actor)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (incident is null)
        {
            return CommandOutcome.NotFound;
        }

        if (!IncidentAccess.CanEditContent(actor, incident))
        {
            return CommandOutcome.Forbidden;
        }

        // Лише дозволені поля змісту. Owner, status, role не змінюються цією операцією.
        incident.Title = title;
        incident.Description = description;
        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return CommandOutcome.Done;
    }

    public async Task<CommandOutcome> DeleteAsync(
        Guid id,
        IncidentActor actor,
        CancellationToken cancellationToken)
    {
        var incident = await dbContext.Incidents
            .VisibleTo(actor)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (incident is null)
        {
            return CommandOutcome.NotFound;
        }

        if (!IncidentAccess.CanDelete(actor, incident))
        {
            return CommandOutcome.Forbidden;
        }

        if (incident.Status != IncidentStatus.New)
        {
            return CommandOutcome.Conflict;
        }

        dbContext.Incidents.Remove(incident);
        await dbContext.SaveChangesAsync(cancellationToken);
        return CommandOutcome.Done;
    }

    public async Task<CommandOutcome> TriageAsync(
        Guid id,
        IncidentActor actor,
        CancellationToken cancellationToken)
    {
        var incident = await dbContext.Incidents
            .VisibleTo(actor)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (incident is null)
        {
            return CommandOutcome.NotFound;
        }

        if (!IncidentAccess.CanTriage(actor))
        {
            return CommandOutcome.Forbidden;
        }

        if (incident.Status != IncidentStatus.New)
        {
            return CommandOutcome.Conflict;
        }

        // Наступний статус визначає сервер; клієнт його не передає.
        var now = DateTimeOffset.UtcNow;
        dbContext.IncidentStatusHistory.Add(new IncidentStatusHistory
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            ChangedByUserId = actor.UserId,
            OldStatus = IncidentStatus.New,
            NewStatus = IncidentStatus.Triaged,
            Note = "Triaged by analyst",
            CreatedAtUtc = now
        });
        incident.Status = IncidentStatus.Triaged;
        incident.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return CommandOutcome.Done;
    }
}
