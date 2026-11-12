using Microsoft.AspNetCore.Identity;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace SecureLab.Api.Scaffolding;

public static class Lab03Identity
{
    public static void AddLab03Identity(this WebApplicationBuilder builder)
    {
        builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 10;
        }).AddEntityFrameworkStores<SecureLabDbContext>().AddDefaultTokenProviders();
        builder.Services.AddAuthorization();
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
    }

    public static void MapLab03Identity(this WebApplication app)
    {
        app.MapPost("/api/auth/register", async (
            RegisterUserRequest input, UserManager<ApplicationUser> users) =>
        {
            var userName = input.UserName?.Trim();
            var email = input.Email?.Trim();
            var displayName = input.DisplayName?.Trim();

            if (string.IsNullOrWhiteSpace(userName) || userName.Length > 256
                || string.IsNullOrWhiteSpace(email) || email.Length > 256
                || string.IsNullOrWhiteSpace(displayName) || displayName.Length > 120
                || string.IsNullOrEmpty(input.Password) || input.Password.Length > 128)
            {
                return Results.BadRequest(new { message = "Registration failed" });
            }

            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = userName,
                Email = email,
                DisplayName = displayName
            };

            var result = await users.CreateAsync(user, input.Password);
            if (!result.Succeeded)
            {
                var codes = result.Errors.Select(error => error.Code).ToArray();
                if (codes.Contains("DuplicateUserName") || codes.Contains("DuplicateEmail"))
                    return Results.Conflict(new { message = "Registration conflict" });
                return Results.BadRequest(new { message = "Registration failed" });
            }

            await users.AddToRoleAsync(user, "Reporter");
            return Results.Created("/api/me",
                new RegisterUserResponse(user.Id, user.UserName!, user.DisplayName));
        });

        app.MapPost("/api/auth/login", async (
            LoginAttemptRequest input, HttpContext http,
            SignInManager<ApplicationUser> signIn, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AuthEvents");
            var userName = input.UserName?.Trim() ?? string.Empty;
            var fingerprint = Fingerprint(userName);
            var key = LoginThrottle.Key(http, userName);

            if (LoginThrottle.IsBlocked(key, out var retryAfterSeconds))
            {
                http.Response.Headers["Retry-After"] = retryAfterSeconds.ToString();
                logger.LogWarning("AuthEvent {Event} user={User} at={At:O} trace={Trace}",
                    "login_throttled", fingerprint, DateTimeOffset.UtcNow, http.TraceIdentifier);
                return Results.Problem(statusCode: StatusCodes.Status429TooManyRequests,
                    title: "Too Many Requests");
            }

            var succeeded = false;
            var inputIsValid = userName.Length is > 0 and <= 256
                && !string.IsNullOrEmpty(input.Password) && input.Password.Length <= 128;
            if (inputIsValid)
            {
                var result = await signIn.PasswordSignInAsync(
                    userName, input.Password!, isPersistent: false, lockoutOnFailure: false);
                succeeded = result.Succeeded;
            }

            if (!succeeded)
            {
                LoginThrottle.RegisterFailure(key);
                logger.LogWarning("AuthEvent {Event} user={User} at={At:O} trace={Trace}",
                    "login_failed", fingerprint, DateTimeOffset.UtcNow, http.TraceIdentifier);
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                    title: "Unauthorized");
            }

            LoginThrottle.Reset(key);
            logger.LogInformation("AuthEvent {Event} user={User} at={At:O} trace={Trace}",
                "login_succeeded", fingerprint, DateTimeOffset.UtcNow, http.TraceIdentifier);
            return Results.NoContent();
        });

        app.MapGet("/api/auth/session-check", () => Results.NoContent())
            .RequireAuthorization();

        app.MapGet("/api/me", GetCurrentUser).RequireAuthorization();

        app.MapPost("/api/auth/logout", async (SignInManager<ApplicationUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.NoContent();
        });
    }

    private static IResult GetCurrentUser(HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userId, out var verifiedUserId))
        {
            return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);
        }

        var userName = httpContext.User.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;
        var role = httpContext.User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

        return Results.Ok(new
        {
            UserId = verifiedUserId,
            UserName = userName,
            Role = role
        });
    }

    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant())))[..8];
}

public sealed record RegistrationInput(string? UserName, string? Email, string? DisplayName, string? Password);
public sealed record LoginInput(string? UserName, string? Password);
public sealed record RegisterUserRequest(string? UserName, string? Email, string? DisplayName, string? Password);
public sealed record RegisterUserResponse(Guid Id, string UserName, string DisplayName);
public sealed record LoginAttemptRequest(string? UserName, string? Password);