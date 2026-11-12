using System.Collections.Concurrent;

namespace SecureLab.Api.Scaffolding;

public static class LoginThrottle
{
    public const int MaxFailures = 5;
    public static TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    private static readonly ConcurrentDictionary<string, (int Failures, DateTimeOffset WindowStart)> Attempts = new();

    public static string Key(HttpContext http, string userName)
        => $"{http.Connection.RemoteIpAddress?.ToString() ?? "local"}|{userName.ToLowerInvariant()}";

    public static bool IsBlocked(string key, out int retryAfterSeconds)
    {
        retryAfterSeconds = 0;
        if (!Attempts.TryGetValue(key, out var entry)) return false;

        var elapsed = DateTimeOffset.UtcNow - entry.WindowStart;
        if (elapsed >= Window)
        {
            Attempts.TryRemove(key, out _);
            return false;
        }
        if (entry.Failures < MaxFailures) return false;

        retryAfterSeconds = (int)Math.Ceiling((Window - elapsed).TotalSeconds);
        return true;
    }

    public static void RegisterFailure(string key)
    {
        var now = DateTimeOffset.UtcNow;
        Attempts.AddOrUpdate(
            key,
            _ => (1, now),
            (_, old) => now - old.WindowStart >= Window ? (1, now) : (old.Failures + 1, old.WindowStart));

        if (Attempts.Count > 10_000)
        {
            foreach (var item in Attempts.Where(i => now - i.Value.WindowStart >= Window).ToArray())
                Attempts.TryRemove(item.Key, out _);
        }
    }

    public static void Reset(string key) => Attempts.TryRemove(key, out _);
}