using System.Collections.Concurrent;

public class UserSessionService
{
    private readonly ConcurrentDictionary<long, UserSession> _userSessions = new();

    public UserSession GetSession(long chatId)
    {
        return _userSessions.GetOrAdd(chatId, new UserSession());
    }
}

public class UserSession
{
    public string CurrentModuleId { get; set; }
    // Potentially other session-specific data can be added here
}
