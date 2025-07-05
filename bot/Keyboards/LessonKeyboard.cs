using System.Collections.Generic;
using Telegram.Bot.Types.ReplyMarkups;
using OmnieyeBot.Models; // For ModuleContent, LessonContent, LevelEntry

namespace OmnieyeBot.Keyboards
{
    public static class LessonKeyboard
    {
        public const string LevelPrefix = "Уровень: "; // New prefix for levels
        public const string ModulePrefix = "Модуль: ";
        public const string LessonPrefix = "Урок: ";
        public const string BackButtonText = "Назад";

        public static ReplyKeyboardMarkup GetLevelsKeyboard(List<LevelEntry> levels)
        {
            var keyboardButtons = new List<KeyboardButton[]>();
            foreach (var level in levels)
            {
                // Button text: "Уровень: Junior Admin"
                // CommandRouter will parse "Junior Admin" and find matching LevelEntry by title
                keyboardButtons.Add(new KeyboardButton[] { $"{LevelPrefix}{level.Title}" });
            }
            keyboardButtons.Add(new KeyboardButton[] { BackButtonText }); // Back from level list goes to Main Menu (handled by CommandRouter logic)
            return new ReplyKeyboardMarkup(keyboardButtons.ToArray()) { ResizeKeyboard = true };
        }

        // Renamed from GetModulesKeyboard and updated to take LevelEntry
        public static ReplyKeyboardMarkup GetModulesInLevelKeyboard(LevelEntry level)
        {
            var keyboardButtons = new List<KeyboardButton[]>();
            foreach (var module in level.Modules)
            {
                // Button text: "Модуль: Основы системного администрирования"
                // CommandRouter will use CurrentLevelId from session and this title to find the module
                keyboardButtons.Add(new KeyboardButton[] { $"{ModulePrefix}{module.Title}" });
            }
            keyboardButtons.Add(new KeyboardButton[] { BackButtonText }); // Back from module list (within a level) goes to Level List
            return new ReplyKeyboardMarkup(keyboardButtons.ToArray()) { ResizeKeyboard = true };
        }

        // Parameter type changed to ModuleContent (which is what we cache and use)
        public static ReplyKeyboardMarkup GetLessonsInModuleKeyboard(ModuleContent module)
        {
            var keyboardButtons = new List<KeyboardButton[]>();
            foreach (var lesson in module.Lessons)
            {
                // Button text: "Урок: Кто такой системный администратор (id:1)"
                keyboardButtons.Add(new KeyboardButton[] { $"{LessonPrefix}{lesson.Title} (id:{lesson.LessonId})" });
            }
            keyboardButtons.Add(new KeyboardButton[] { BackButtonText }); // Back from lesson list goes to Module List (for current level)
            return new ReplyKeyboardMarkup(keyboardButtons.ToArray()) { ResizeKeyboard = true };
        }

        // Updated to include levelId, moduleId, lessonId in callback_data
        public static InlineKeyboardMarkup GetLessonContentInlineKeyboard(int levelId, int moduleId, int lessonId)
        {
            return new InlineKeyboardMarkup(new[]
            {
                // Example callback_data: "flashcards_1_1_1" (levelId_moduleId_lessonId)
                InlineKeyboardButton.WithCallbackData("🔹 Флеш-карточки", $"flashcards_{levelId}_{moduleId}_{lessonId}"),
                InlineKeyboardButton.WithCallbackData("🔹 Пройти тест", $"quiz_{levelId}_{moduleId}_{lessonId}")
                // "Назад к списку уроков" can be an inline button too, or rely on the ReplyKeyboard "Назад"
                // InlineKeyboardButton.WithCallbackData("⬅️ К урокам", $"backtolessons_{levelId}_{moduleId}")
            });
        }
    }
}
