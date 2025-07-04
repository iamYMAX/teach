using Omnieye.Bot.Models; // For UserTestState
using Newtonsoft.Json;

namespace Omnieye.Bot.States
{
    public class UserSession
    {
        public long UserId { get; set; } // Telegram User ID
        public bool IsAuthenticated { get; set; } = false;

        // Null if no active test
        public UserTestState? CurrentTestState { get; set; }

        // Could add CurrentLessonId, etc. here later

        [JsonConstructor]
        public UserSession(long userId)
        {
            UserId = userId;
        }

        // Convenience constructor for new sessions
        public UserSession(long userId, bool isAuthenticated)
        {
            UserId = userId;
            IsAuthenticated = isAuthenticated;
        }

        public void StartTest(Test test)
        {
            CurrentTestState = new UserTestState(test);
        }

        public void EndTest()
        {
            CurrentTestState = null;
        }
    }
}
