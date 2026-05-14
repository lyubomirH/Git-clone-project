using System.Security.Cryptography;
using GitClone.Models;

namespace GitClone.Core;

public class SessionManager
{
    private readonly Dictionary<string, Session> _sessions = new();
    private readonly TimeSpan _sessionTimeout = TimeSpan.FromHours(8);
    private readonly object _lock = new();

    public string CreateSession(string username)
    {
        lock (_lock)
        {
            var token = GenerateSessionToken();
            var session = new Session
            {
                Token = token,
                Username = username,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(_sessionTimeout)
            };

            _sessions[token] = session;
            CleanExpiredSessions();
            return token;
        }
    }

    public bool ValidateSession(string token)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(token, out var session))
            {
                if (session.ExpiresAt > DateTime.UtcNow)
                {
                    session.LastActivity = DateTime.UtcNow;
                    return true;
                }
                else
                {
                    _sessions.Remove(token);
                    return false;
                }
            }
            return false;
        }
    }

    public void DestroySession(string token)
    {
        lock (_lock)
        {
            _sessions.Remove(token);
        }
    }

    public string? GetUsernameFromToken(string token)
    {
        lock (_lock)
        {
            return _sessions.TryGetValue(token, out var session) ? session.Username : null;
        }
    }

    private static string GenerateSessionToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }

    private void CleanExpiredSessions()
    {
        var expired = _sessions.Where(s => s.Value.ExpiresAt <= DateTime.UtcNow).ToList();
        foreach (var session in expired)
        {
            _sessions.Remove(session.Key);
        }
    }

    public List<Session> GetActiveSessions()
    {
        lock (_lock)
        {
            CleanExpiredSessions();
            return _sessions.Values.ToList();
        }
    }
}