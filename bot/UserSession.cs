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

        // Optional: to remember which item was selected for detail view
        public int? ViewingItemId { get; set; }

        // Properties for active test taking
        public int? ActiveTestId { get; set; } = null;
        public int CurrentQuestionIndex { get; set; } = 0; // Index for the question being currently displayed/answered
        public int CurrentTestScore { get; set; } = 0;


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

        // This method was for the old test structure (from tests_junior_admin.json)
        // It might be deprecated or adapted if the new TestData structure is used exclusively for test taking.
        // For now, let's assume CurrentTestState (UserTestState from Models) is for the old system,
        // and ActiveTestId, CurrentQuestionIndex, CurrentTestScore are for the new system.
        // Or, ideally, unify them. The task implies a new test structure.
        // Let's remove the old StartTest/EndTest related to UserTestState from Models.Omnieye.Bot.Models.Test
        // and focus on the new structure.

        public void StartNewTest(int testId) // Renamed for clarity with new system
        {
            ActiveTestId = testId;
            CurrentQuestionIndex = 0;
            CurrentTestScore = 0;
            CurrentState = UserCurrentState.TakingTest; // Set state when test starts
        }

        public void EndCurrentTest() // Renamed for clarity
        {
            ActiveTestId = null;
            CurrentQuestionIndex = 0;
            CurrentTestScore = 0;
            // CurrentTestState = null; // This was for the old structure. Remove if UserTestState is fully replaced.
                                    // For now, let's assume we might have both for a bit or it's being phased out.
                                    // The original CurrentTestState was based on the JSON file, the new one is in-memory.
                                    // The task asks for a new structure, so we should ensure this doesn't conflict.
                                    // For now, let's assume UserTestState (from Models) is separate and might be for a different feature or old code.
                                    // The prompt indicates "Сохранять в сессии пользователя ID теста и текущий индекс вопроса = 0" for the new test.
            CurrentState = UserCurrentState.MainMenu; // Default to main menu after test, can be overridden by caller.
        }
    }
}
