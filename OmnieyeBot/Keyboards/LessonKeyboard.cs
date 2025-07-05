using System.Collections.Generic;
using Telegram.Bot.Types.ReplyMarkups;
using OmnieyeBot.Models; // Required for CourseModule, Lesson

namespace OmnieyeBot.Keyboards
{
    public static class LessonKeyboard
    {
        public static ReplyKeyboardMarkup GetModulesKeyboard(List<CourseModule> modules)
        {
            var keyboardButtons = new List<KeyboardButton[]>();

            foreach (var module in modules)
            {
                keyboardButtons.Add(new KeyboardButton[] { $"➡️ Модуль: {module.Title}" });
            }
            // "Back" from module list goes to Main Menu
            keyboardButtons.Add(new KeyboardButton[] { "⬅️ Назад" });

            return new ReplyKeyboardMarkup(keyboardButtons)
            {
                ResizeKeyboard = true
            };
        }

        public static ReplyKeyboardMarkup GetLessonsInModuleKeyboard(CourseModule module)
        {
            var keyboardButtons = new List<KeyboardButton[]>();

            foreach (var lesson in module.Lessons)
            {
                keyboardButtons.Add(new KeyboardButton[] { $"➡️ Урок: {lesson.Title}" });
            }
            // "Back" from lesson list goes to Module List
            keyboardButtons.Add(new KeyboardButton[] { "⬅️ Назад" });

            return new ReplyKeyboardMarkup(keyboardButtons)
            {
                ResizeKeyboard = true
            };
        }

        public static ReplyKeyboardMarkup GetLessonContentKeyboard()
        {
            // "Back" from lesson content goes to Lesson List of the current module
            return new ReplyKeyboardMarkup(new[]
            {
                new KeyboardButton[] { "⬅️ Назад" }
            })
            {
                ResizeKeyboard = true
            };
        }
    }
}
