using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Scaffolding;

// Навчальний старт ЛР 02. Запускати лише з локальними штучними даними.
public static class Lab02Endpoints
{
    public static void MapLab02Endpoints(this WebApplication app)
    {
        app.MapGet("/api/incidents/search", async (string? q, string? sortBy, SecureLabDbContext db) =>
        {
            var sortKey = string.IsNullOrWhiteSpace(sortBy) ? "createdAtUtc" : sortBy.Trim();
            if (sortKey != "createdAtUtc" && sortKey != "severity" && sortKey != "status")
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["sortBy"] = new[] { "Дозволені значення: createdAtUtc, severity, status." }
                });
            }

            IQueryable<Incident> query = db.Incidents.AsNoTracking();

            var term = q?.Trim();
            if (!string.IsNullOrEmpty(term))
            {
                var pattern = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                query = query.Where(i =>
                    EF.Functions.ILike(i.Title, pattern, "\\") ||
                    EF.Functions.ILike(i.Description, pattern, "\\"));
            }

            IOrderedQueryable<Incident> ordered = sortKey switch
            {
                "severity" => query.OrderByDescending(i =>
                    i.Severity == IncidentSeverity.Critical ? 3 :
                    i.Severity == IncidentSeverity.High ? 2 :
                    i.Severity == IncidentSeverity.Medium ? 1 : 0),
                "status" => query.OrderBy(i =>
                    i.Status == IncidentStatus.New ? 0 :
                    i.Status == IncidentStatus.Triaged ? 1 :
                    i.Status == IncidentStatus.InProgress ? 2 :
                    i.Status == IncidentStatus.Resolved ? 3 : 4),
                _ => query.OrderByDescending(i => i.CreatedAtUtc)
            };

            var items = await ordered
                .ThenBy(i => i.Id)
                .Take(50)
                .Select(i => new SearchIncidentResponse(
                    i.Id, i.Title, i.Severity.ToString(), i.Status.ToString(), i.CreatedAtUtc))
                .ToListAsync();

            return Results.Ok(items);
        });
        app.MapPost("/api/incidents", async (
    CreateIncidentRequest request, SecureLabDbContext db, CancellationToken ct) =>
        {
            var now = DateTimeOffset.UtcNow;
            var errors = new Dictionary<string, string[]>();

            // title
            if (string.IsNullOrWhiteSpace(request.Title))
                errors["title"] = ["Title обов'язковий."];
            else if (request.Title.Length > 160)
                errors["title"] = ["Title не довший за 160 символів."];

            // description
            if (string.IsNullOrWhiteSpace(request.Description))
                errors["description"] = ["Description обов'язковий."];
            else if (request.Description.Length > 4000)
                errors["description"] = ["Description не довший за 4000 символів."];

            // severity: TryParse + IsDefined
            var severityIsValid =
                Enum.TryParse<IncidentSeverity>(request.Severity, ignoreCase: true, out var severity)
                && Enum.IsDefined(severity);
            if (!severityIsValid)
                errors["severity"] = ["Допустимі значення: Low, Medium, High, Critical."];

            // occurredAtUtc
            if (request.OccurredAtUtc is null)
                errors["occurredAtUtc"] = ["occurredAtUtc обов'язковий."];
            else if (request.OccurredAtUtc > now.AddMinutes(5))
                errors["occurredAtUtc"] = ["Дата не може бути більш ніж на 5 хвилин у майбутньому."];

            // нормалізація один раз
            var title = request.Title?.Trim() ?? "";
            var description = request.Description?.Trim() ?? "";

            // [Д] cross-field: High/Critical => description >= 40 символів
            if (severityIsValid
                && (severity == IncidentSeverity.High || severity == IncidentSeverity.Critical)
                && !errors.ContainsKey("description")
                && description.Length < 40)
                errors["description"] = ["Для High/Critical опис має містити щонайменше 40 символів."];

            // усі помилки контракту — одним 400, до звернення до БД
            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            // [Д] предметний конфлікт
            var conflict = await db.Incidents.AnyAsync(
                item => item.Title == title && item.Status != IncidentStatus.Closed, ct);
            if (conflict)
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Конфлікт даних",
                    detail: "Інцидент з таким заголовком уже існує.");

            // server-managed поля задає тільки сервер
            var incident = new Incident
            {
                Id = Guid.NewGuid(),
                OwnerUserId = DbSeeder.AliceId,
                Title = title,
                Description = description,
                Severity = severity,
                Status = IncidentStatus.New,
                OccurredAtUtc = request.OccurredAtUtc!.Value.ToUniversalTime(),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Incidents.Add(incident);
            await db.SaveChangesAsync(ct);

            var response = new CreatedIncidentResponse(
                incident.Id, incident.Title, incident.Severity.ToString(),
                incident.Status.ToString(), incident.OccurredAtUtc, incident.CreatedAtUtc);
            return Results.Created($"/api/incidents/{incident.Id}", response);
        });
    }
}


public sealed record CreateIncidentRequest(
    string? Title, string? Description, string? Severity, DateTimeOffset? OccurredAtUtc);
public sealed record CreatedIncidentResponse(
    Guid Id, string Title, string Severity, string Status,
    DateTimeOffset OccurredAtUtc, DateTimeOffset CreatedAtUtc);
public sealed record SearchIncidentResponse(Guid Id, string Title, string Severity, string Status, DateTimeOffset CreatedAtUtc);