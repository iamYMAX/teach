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

    public enum LessonLevel
    {
        Beginner,   // уровень 1–2
        Intermediate, // уровень 3–5
        Advanced    // уровень 6+
    }
}
