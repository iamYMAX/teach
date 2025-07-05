using System.Collections.Generic;
using Telegram.Bot.Types.ReplyMarkups;
using OmnieyeBot.Models; // For CourseModule, Lesson

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
            keyboardButtons.Add(new KeyboardButton[] { "⬅️ Назад" });

            return new ReplyKeyboardMarkup(keyboardButtons)
            {
                ResizeKeyboard = true
            };
        }

        public static ReplyKeyboardMarkup GetLessonContentKeyboard()
        {
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
