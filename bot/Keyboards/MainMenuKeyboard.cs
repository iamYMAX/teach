using Telegram.Bot.Types.ReplyMarkups;

namespace OmnieyeBot.Keyboards
{
    public static class MainMenuKeyboard
    {
        public const string LessonsButtonText = "📘 Уроки";
        // Add other main menu button texts as consts if needed

        public static ReplyKeyboardMarkup GetKeyboard()
        {
            return new ReplyKeyboardMarkup(new KeyboardButton[][] // Ensure explicit typing
            {
                new KeyboardButton[] { LessonsButtonText },
                // Future buttons can be added here: new KeyboardButton[] { FlashcardsButtonText }
            })
            {
                ResizeKeyboard = true
            };
        }
    }
}
