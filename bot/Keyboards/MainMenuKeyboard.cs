using Telegram.Bot.Types.ReplyMarkups;

namespace OmnieyeBot.Keyboards
{
    public static class MainMenuKeyboard
    {
        public static ReplyKeyboardMarkup GetKeyboard()
        {
            return new ReplyKeyboardMarkup(new[]
            {
                new KeyboardButton[] { "📘 Уроки" },
                // Future buttons can be added here: "🧠 Карточки", "🏆 Тесты"
            })
            {
                ResizeKeyboard = true
            };
        }
    }
}
