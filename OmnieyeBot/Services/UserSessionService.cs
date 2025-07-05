using System.Collections.Concurrent;
using OmnieyeBot.Models; // To reference UserSession

namespace OmnieyeBot.Services
{
    public class UserSessionService
    {
        private readonly ConcurrentDictionary<long, UserSession> _userSessions = new();

        public UserSession GetSession(long chatId)
        {
            // GetOrAdd ensures thread safety and that a session is always returned
            return _userSessions.GetOrAdd(chatId, _ => new UserSession());
        }

        // Example of how you might clear a session or parts of it
        public void ClearSessionModule(long chatId)
        {
            if (_userSessions.TryGetValue(chatId, out UserSession session))
            {
                session.CurrentModuleId = null;
                // If using ViewState:
                // session.CurrentView = ViewState.MainMenu; // Or appropriate default
            }
        }
    }
}
