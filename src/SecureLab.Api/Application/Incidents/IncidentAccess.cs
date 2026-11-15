using System.Security.Claims;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Application.Incidents;

public enum CommandOutcome
{
    Done,
    NotFound,
    Forbidden,
    Conflict
}

/// <summary>Суб'єкт рішення, побудований лише з verified principal (не з body, URL чи заголовків).</summary>
public sealed record IncidentActor(Guid UserId, bool IsAnalyst)
{
    public static IncidentActor? From(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId)
            ? new IncidentActor(userId, principal.IsInRole(DbSeeder.AnalystRole))
            : null;
    }
}

/// <summary>
/// Єдине централізоване місце правил доступу до Incident (baseline 2-A).
/// Усі запити та команди проходять через ці правила.
/// </summary>
public static class IncidentAccess
{
    // Видимість: Analyst бачить усі інциденти для аналізу; Reporter і Administrator — лише власні.
    // Невидимий ресурс не завантажується, тому для клієнта він не відрізняється від неіснуючого (політика 404).
    public static IQueryable<Incident> VisibleTo(this IQueryable<Incident> incidents, IncidentActor actor)
    {
        if (actor.IsAnalyst)
        {
            return incidents;
        }

        var userId = actor.UserId;
        return incidents.Where(incident => incident.OwnerUserId == userId);
    }

    // Зміст редагує лише власник; право читання Analyst не дає права змінювати чужий зміст.
    public static bool CanEditContent(IncidentActor actor, Incident incident) =>
        incident.OwnerUserId == actor.UserId;

    // Видаляє лише власник і лише поки інцидент має статус New.
    public static bool CanDelete(IncidentActor actor, Incident incident) =>
        incident.OwnerUserId == actor.UserId;

    // Привілейована дія triage (New -> Triaged) — лише Analyst.
    public static bool CanTriage(IncidentActor actor) => actor.IsAnalyst;
}
