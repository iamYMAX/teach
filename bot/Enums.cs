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
        WaitingForNameInput, // New state for setting name
        ReviewingFlashcards, // New state for flashcard mode

        // Admin states
        AdminRoot,
        AdminAddingLessonTitle,
        AdminAddingLessonDescription,
        AdminAddingLessonContent,
        AdminAddingLessonLevel,
        AdminEditingLessonSelect,
        AdminEditingLessonSelectField,
        AdminEditingLessonEnterNewValue,
        AdminDeletingLessonSelect,
        AdminAddingTestTitle,
        AdminAddingTestDescription,
        AdminAddingTestLevel,
        AdminAddingTestQuestionText,
        AdminAddingTestQuestionOptions,
        AdminAddingTestQuestionCorrectOption,
        AdminAddingTestQuestionAskMore,
        AdminEditingTestSelect,
        AdminEditingTestSelectField,
        AdminEditingTestEnterNewValue, // For test metadata
        AdminEditingTestQuestionSelect,
        AdminEditingTestQuestionEditField, // Field of a question (text, options, correct answer)
        AdminEditingTestQuestionEnterNewValue, // For question fields
        AdminDeletingTestSelect
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
