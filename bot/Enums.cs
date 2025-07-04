namespace Omnieye.Bot.States
{
    public enum UserCurrentState
    {
        MainMenu,
        ViewingLessonList,
        ViewingLessonDetail,
        ViewingTestList,
        ViewingTestDetail,
        TakingTest,
        WaitingForNameInput // New state for setting name
    }

    public enum TestDifficulty
    {
        Easy,
        Medium,
        Hard
    }
}
