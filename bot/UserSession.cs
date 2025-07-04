using Omnieye.Bot.Models; // For UserTestState
using Newtonsoft.Json;

namespace Omnieye.Bot.States
{
    // UserCurrentState enum will be in Enums.cs or directly here if preferred
    // For this operation, assuming Enums.cs is created and namespace Omnieye.Bot.States is used.

    public class UserSession
    {
        public long UserId { get; set; } // Telegram User ID
        public bool IsAuthenticated { get; set; } = false;

        // Null if no active test
        public UserTestState? CurrentTestState { get; set; }

        public UserCurrentState CurrentState { get; set; } = UserCurrentState.MainMenu;

        // Optional: to remember which item was selected, if needed beyond just state
        public int? ViewingItemId { get; set; }


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
