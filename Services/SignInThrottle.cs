using System.Net;
using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// Stops password guessing on both sign-in forms (the helpdesk's /Login and the staff portal's).
// - An account - strictly, an email address as typed, so a lockout says nothing about whether the account exists - is
//   locked for 15 minutes after 5 wrong passwords within 15 minutes.
// - A computer (IP address) is blocked for 15 minutes after 30 wrong passwords within 15 minutes, across any accounts,
//   which catches one guess at each of many accounts. Set high because a whole school can share one address when the
//   helpdesk is hosted outside it.
// While blocked, no password is checked at all, so the hashing cost can't be used to tie the server up either.
// Held in memory: a restart clears it, which only ever helps the person locked out. Each lockout goes in the audit log.
public sealed class SignInThrottle(HelpdeskStore store, ILogger<SignInThrottle> logger)
{
    public const int AccountLimit = 5;
    public const int AddressLimit = 30;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly Dictionary<string, Counter> _counters = new(StringComparer.Ordinal);
    private readonly Lock _sync = new();

    private sealed class Counter
    {
        public readonly Queue<DateTime> Failures = new();
        public DateTime? LockedUntil;
    }

    // Why this sign-in is refused before its password is even looked at, or null to go ahead. `form` is "helpdesk" or
    // "portal", so the same email on the two forms is counted separately (a requester and a technician can share one).
    public string? Refusal(string form, string? email, IPAddress? address)
    {
        var now = DateTime.UtcNow;
        lock (_sync)
        {
            if (Remaining(AddressKey(address), now) is { } addressWait)
                return $"Too many failed sign-ins from this computer. Try again in {Minutes(addressWait)}.";
            if (Remaining(AccountKey(form, email), now) is { } accountWait)
                return $"Too many failed sign-ins for this account. Try again in {Minutes(accountWait)}, or ask {(form == "portal" ? "the IT team" : "an administrator")} to reset the password.";
            return null;
        }
    }

    public void Failed(string form, string? email, IPAddress? address)
    {
        var now = DateTime.UtcNow;
        var locked = new List<AuditEntry>();
        lock (_sync)
        {
            if (_counters.Count > 10_000) Prune(now);
            var who = (email ?? "").Trim();
            var from = address?.ToString() ?? "an unknown address";
            if (Count(AccountKey(form, email), AccountLimit, now))
                locked.Add(new AuditEntry(now, "Sign-in", null, null, who.Length > 0 ? who : "(no email)", "Locked out",
                    $"{AccountLimit} wrong passwords on the {form} sign-in within {Window.TotalMinutes:0} minutes, the last from {from}. Sign-in for this email is refused for {Window.TotalMinutes:0} minutes."));
            if (Count(AddressKey(address), AddressLimit, now))
                locked.Add(new AuditEntry(now, "Sign-in", null, null, from, "Address blocked",
                    $"{AddressLimit} wrong passwords from this address within {Window.TotalMinutes:0} minutes, across any accounts. Every sign-in from it is refused for {Window.TotalMinutes:0} minutes."));
        }
        // Outside the throttle's lock: recording takes the store's lock and a database write.
        foreach (var entry in locked)
        {
            logger.LogWarning("Sign-in {Action}: {Entity} - {Details}", entry.Action, entry.Entity, entry.Details);
            store.RecordEvent(entry with { By = new Actor(null, "Sign-in protection") });
        }
    }

    // A correct password clears the account's count; the address's is left to expire, so an attacker who knows one
    // password can't use it to reset their allowance of guesses at everyone else's.
    public void Succeeded(string form, string? email)
    {
        lock (_sync) _counters.Remove(AccountKey(form, email));
    }

    // A password set by someone else (a technician resetting it) lifts the account's lockout straight away.
    public void Clear(string form, string? email) => Succeeded(form, email);

    private static string AccountKey(string form, string? email) => $"{form}:{(email ?? "").Trim().ToLowerInvariant()}";
    private static string AddressKey(IPAddress? address) => $"ip:{address?.ToString() ?? "unknown"}";

    // Time left on a lock, or null if there isn't one. An expired lock is wiped, so the account starts again with a
    // full allowance rather than being locked again by the next single mistake.
    private TimeSpan? Remaining(string key, DateTime now)
    {
        if (!_counters.TryGetValue(key, out var counter) || counter.LockedUntil is not { } until) return null;
        if (until > now) return until - now;
        _counters.Remove(key);
        return null;
    }

    // Records one failure; true if it has just caused a lock.
    private bool Count(string key, int limit, DateTime now)
    {
        if (!_counters.TryGetValue(key, out var counter)) _counters[key] = counter = new Counter();
        if (counter.LockedUntil > now) return false;
        counter.Failures.Enqueue(now);
        while (counter.Failures.Count > 0 && now - counter.Failures.Peek() > Window) counter.Failures.Dequeue();
        if (counter.Failures.Count < limit) return false;
        counter.LockedUntil = now + Window;
        counter.Failures.Clear();
        return true;
    }

    private void Prune(DateTime now)
    {
        foreach (var (key, counter) in _counters.ToList())
            if (!(counter.LockedUntil > now) && (counter.Failures.Count == 0 || now - counter.Failures.Last() > Window))
                _counters.Remove(key);
    }

    private static string Minutes(TimeSpan wait)
    {
        var minutes = (int)Math.Ceiling(wait.TotalMinutes);
        return minutes == 1 ? "1 minute" : $"{minutes} minutes";
    }
}
