using System.Collections.Generic;
using Telegram.Bot.Types.ReplyMarkups;
using OmnieyeBot.Models; // For CourseModule, Lesson

namespace OmnieyeBot.Keyboards
{
    public static class LessonKeyboard
    {
        public const string ModulePrefix = "Модуль: "; // Emoji removed
        public const string LessonPrefix = "Урок: ";   // Emoji removed
        public const string BackButtonText = "Назад";   // Emoji removed

        // Method to get keyboard for displaying modules loaded from JSON
        public static ReplyKeyboardMarkup GetModulesKeyboard(List<ModuleContent> modules)
        {
            var keyboardButtons = new List<KeyboardButton[]>();

            foreach (var module in modules)
            {
                // Text now includes ID for easier parsing in CommandRouter if needed,
                // or CommandRouter can rely on exact title match to get ID from service.
                // For ReplyKeyboardMarkup, parsing text is common.
                // Example: "➡️ Основы системного администрирования (id:module1)"
                // For simplicity now, just title. CommandRouter will need to map title back to ID or load all to find.
                keyboardButtons.Add(new KeyboardButton[] { $"{ModulePrefix}{module.Title}" });
            }
            keyboardButtons.Add(new KeyboardButton[] { BackButtonText });

            return new ReplyKeyboardMarkup(keyboardButtons.ToArray()) // Ensure it's an array
            {
                ResizeKeyboard = true
            };
        }

        // Method to get keyboard for lessons within a specific module (loaded from JSON)
        public static ReplyKeyboardMarkup GetLessonsInModuleKeyboard(ModuleContent module)
        {
            var keyboardButtons = new List<KeyboardButton[]>();

            foreach (var lesson in module.Lessons)
            {
                // Example: "➡️ Урок: Кто такой системный администратор (id:1)"
                keyboardButtons.Add(new KeyboardButton[] { $"{LessonPrefix}{lesson.Title} (id:{lesson.LessonId})" });
            }
            keyboardButtons.Add(new KeyboardButton[] { BackButtonText });

            return new ReplyKeyboardMarkup(keyboardButtons.ToArray()) // Ensure it's an array
            {
                ResizeKeyboard = true
            };
        }

        // Keyboard for when viewing lesson content - this one will now use InlineKeyboardMarkup
        // public static ReplyKeyboardMarkup GetLessonContentReplyKeyboard() // Old Reply version
        // {
        //     return new ReplyKeyboardMarkup(new KeyboardButton[][]
        //     {
        //         new KeyboardButton[] { BackButtonText }
        //     })
        //     {
        //         ResizeKeyboard = true
        //     };
        // }

        // NEW: Inline keyboard after showing lesson content
        public static InlineKeyboardMarkup GetLessonContentInlineKeyboard(int lessonId, string moduleId)
        {
            return new InlineKeyboardMarkup(new[]
            {
                InlineKeyboardButton.WithCallbackData("🔹 Флеш-карточки", $"flashcards_{moduleId}_{lessonId}"),
                InlineKeyboardButton.WithCallbackData("🔹 Пройти тест", $"quiz_{moduleId}_{lessonId}")
            });
            // "Назад к списку уроков" might be better as a ReplyKeyboard button or a separate inline button row.
            // For now, focusing on new options. The existing "⬅️ Назад" ReplyKeyboard button will handle going back.
        }
    }
}
