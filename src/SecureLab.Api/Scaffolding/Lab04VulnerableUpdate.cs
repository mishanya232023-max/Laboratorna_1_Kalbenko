using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;

namespace SecureLab.Api.Scaffolding;

// INTENTIONALLY VULNERABLE (local study only, not part of lab/4-authorization):
// finds the incident by id without comparing it with the current principal.
public static class Lab04VulnerableUpdate
{
    public static IEndpointRouteBuilder MapLab04VulnerableUpdate(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/api/incidents/{id:guid}", async (
            Guid id,
            VulnerableUpdateRequest request,
            SecureLabDbContext db,
            CancellationToken ct) =>
        {
            var incident = await db.Incidents.SingleOrDefaultAsync(item => item.Id == id, ct);
            if (incident is null) return Results.NotFound();
            incident.Title = request.Title;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization();
        return endpoints;
    }
}

public sealed record VulnerableUpdateRequest(string Title);
