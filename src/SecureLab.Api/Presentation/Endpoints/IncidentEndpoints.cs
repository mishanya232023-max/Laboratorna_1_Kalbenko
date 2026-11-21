using System.Diagnostics;
using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Presentation.Endpoints;

public static class IncidentEndpoints
{
    public static IEndpointRouteBuilder MapIncidentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Уся група вимагає authenticated principal: анонімний запит -> 401 (без redirect).
        var group = endpoints.MapGroup("/api/incidents")
            .WithTags("Incidents")
            .RequireAuthorization();

        group.MapGet("/", GetListAsync)
            .WithName("GetIncidents")
            .Produces<IReadOnlyList<IncidentListItemResponse>>()
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", GetDetailsAsync)
            .WithName("GetIncidentDetails")
            .Produces<IncidentDetailsResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", UpdateAsync)
     .WithName("UpdateIncident")
     .ProducesProblem(StatusCodes.Status404NotFound)
     .AddEndpointFilter<SecureLab.Api.Security.AntiforgeryFilter>();

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteIncident")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .AddEndpointFilter<SecureLab.Api.Security.AntiforgeryFilter>();

        group.MapPost("/{id:guid}/triage", TriageAsync)
            .WithName("TriageIncident")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .AddEndpointFilter<SecureLab.Api.Security.AntiforgeryFilter>();

        group.MapGet("/severity-summary", GetSeveritySummaryAsync)
            .WithName("GetIncidentSeveritySummary")
            .Produces<IReadOnlyList<IncidentSeveritySummaryResponse>>()
            .ProducesValidationProblem();

        endpoints.MapGet("/api/incident-comments/{id:guid}", GetCommentAsync)
            .WithTags("Incidents")
            .WithName("GetIncidentComment")
            .Produces<IncidentCommentResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        return endpoints;
    }

    // Один helper для невідомого і для чужого ресурсу: однакові status, Content-Type і body.
    private static IResult IncidentNotFound() => Results.Problem(
        title: "Інцидент не знайдено",
        statusCode: StatusCodes.Status404NotFound);

    private static IResult CommentNotFound() => Results.Problem(
        title: "Коментар не знайдено",
        statusCode: StatusCodes.Status404NotFound);

    private static void AuditDenied(
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        IncidentActor actor,
        string action,
        Guid resourceId)
    {
        // Безпечна audit-подія: лише server-verified subject, дія, ресурс, серверний час і trace id.
        // Без cookie, password, token, тіла запиту та stack trace.
        loggerFactory.CreateLogger("SecureLab.Audit").LogWarning(
            "authorization.denied subject={SubjectId} action={Action} resource={ResourceId} occurredAtUtc={OccurredAtUtc:O} traceId={TraceId}",
            actor.UserId,
            action,
            resourceId,
            DateTimeOffset.UtcNow,
            Activity.Current?.Id ?? httpContext.TraceIdentifier);
    }

    private static IResult Finish(
        CommandOutcome outcome,
        string action,
        Guid id,
        IncidentActor actor,
        HttpContext httpContext,
        ILoggerFactory loggerFactory)
    {
        switch (outcome)
        {
            case CommandOutcome.Done:
                return Results.NoContent();
            case CommandOutcome.NotFound:
                return IncidentNotFound();
            case CommandOutcome.Forbidden:
                AuditDenied(loggerFactory, httpContext, actor, action, id);
                return Results.Forbid();
            default:
                return Results.Problem(
                    title: "Операція недоступна для поточного стану інциденту",
                    statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static bool TryParseStatus(string? status, out IncidentStatus? parsedStatus)
    {
        parsedStatus = null;
        if (status is null)
        {
            return true;
        }

        if (!Enum.TryParse<IncidentStatus>(status, ignoreCase: true, out var candidate)
            || !Enum.IsDefined(candidate))
        {
            return false;
        }

        parsedStatus = candidate;
        return true;
    }

    private static IResult InvalidStatus() => Results.ValidationProblem(new Dictionary<string, string[]>
    {
        ["status"] = ["Допустимі значення: New, Triaged, InProgress, Resolved, Closed."]
    });

    private static async Task<IResult> GetListAsync(
        string? status,
        HttpContext httpContext,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        var actor = IncidentActor.From(httpContext.User);
        if (actor is null)
        {
            return Results.Challenge();
        }

        if (!TryParseStatus(status, out var parsedStatus))
        {
            return InvalidStatus();
        }

        return Results.Ok(await queries.GetListAsync(parsedStatus, actor, cancellationToken));
    }

    private static async Task<IResult> GetSeveritySummaryAsync(
        string? status,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        if (!TryParseStatus(status, out var parsedStatus))
        {
            return InvalidStatus();
        }

        return Results.Ok(await queries.GetSeveritySummaryAsync(parsedStatus, cancellationToken));
    }

    private static async Task<IResult> GetDetailsAsync(
        Guid id,
        HttpContext httpContext,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        var actor = IncidentActor.From(httpContext.User);
        if (actor is null)
        {
            return Results.Challenge();
        }

        var incident = await queries.GetDetailsAsync(id, actor, cancellationToken);
        return incident is null ? IncidentNotFound() : Results.Ok(incident);
    }

    private static async Task<IResult> GetCommentAsync(
        Guid id,
        HttpContext httpContext,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        var actor = IncidentActor.From(httpContext.User);
        if (actor is null)
        {
            return Results.Challenge();
        }

        var comment = await queries.GetCommentAsync(id, actor, cancellationToken);
        return comment is null ? CommentNotFound() : Results.Ok(comment);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateIncidentRequest request,
        HttpContext httpContext,
        IncidentCommands commands,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var actor = IncidentActor.From(httpContext.User);
        if (actor is null)
        {
            return Results.Challenge();
        }

        var title = request.Title?.Trim();
        var description = request.Description?.Trim();
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrEmpty(title) || title.Length > 160)
        {
            errors["title"] = ["Заголовок: від 1 до 160 символів."];
        }

        if (string.IsNullOrEmpty(description) || description.Length > 4000)
        {
            errors["description"] = ["Опис: від 1 до 4000 символів."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var outcome = await commands.UpdateContentAsync(id, actor, title!, description!, cancellationToken);
        return Finish(outcome, "incident.update", id, actor, httpContext, loggerFactory);
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        HttpContext httpContext,
        IncidentCommands commands,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var actor = IncidentActor.From(httpContext.User);
        if (actor is null)
        {
            return Results.Challenge();
        }

        var outcome = await commands.DeleteAsync(id, actor, cancellationToken);
        return Finish(outcome, "incident.delete", id, actor, httpContext, loggerFactory);
    }

    private static async Task<IResult> TriageAsync(
        Guid id,
        HttpContext httpContext,
        IncidentCommands commands,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var actor = IncidentActor.From(httpContext.User);
        if (actor is null)
        {
            return Results.Challenge();
        }

        var outcome = await commands.TriageAsync(id, actor, cancellationToken);
        return Finish(outcome, "incident.triage", id, actor, httpContext, loggerFactory);
    }
}
