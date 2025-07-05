using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using OmnieyeBot.Models;
using OmnieyeBot.Keyboards;
using Omnieye.Bot.States; // Required for the existing Omnieye.Bot.States.UserSession class
using OmnieyeBot.Services; // For the new CourseService
using Omnieye.Bot.Services; // For the existing UserSessionService

namespace OmnieyeBot.BotHandlers
{
    public class CommandRouter
    {
        private readonly ITelegramBotClient _botClient;
        private readonly OmnieyeBot.Services.CourseService _newCourseService; // Explicitly new service
        private readonly Omnieye.Bot.Services.UserSessionService _existingUserSessionService; // Explicitly existing service

        public CommandRouter(ITelegramBotClient botClient, OmnieyeBot.Services.CourseService courseService, Omnieye.Bot.Services.UserSessionService userSessionService)
        {
            _botClient = botClient;
            _newCourseService = courseService; // Corrected assignment
            _existingUserSessionService = userSessionService; // Corrected assignment
        }

        public async Task<bool> RouteAsync(Message message, CancellationToken cancellationToken)
        {
            var chatId = message.Chat.Id;
            var messageText = message.Text;
            var userSession = _existingUserSessionService.GetUserSession(chatId); // Use corrected field name

            if (messageText == "/start") // Technically /start is often global, but let new system handle it if it wants
            {
                await HandleStartCommandAsync(chatId, userSession, cancellationToken);
                return true;
            }
            else if (messageText == "📘 Уроки")
            {
                await HandleShowCourseModulesAsync(chatId, userSession, cancellationToken);
                return true;
            }
            else if (messageText == "⬅️ Назад")
            {
                // "Назад" is context-dependent. If CurrentModuleId is set, it's part of course navigation.
                // If CurrentModuleId is null, it might be a general "Back" for the main menu,
                // which the old system might handle or this new one can take to main menu.
                // For now, assume this "Назад" is primarily for the course navigation context.
                await HandleBackCommandAsync(chatId, userSession, cancellationToken);
                return true; // Assume "Назад" is handled by this router if it reaches here.
            }
            else if (messageText.StartsWith("➡️ Модуль:"))
            {
                await HandleShowLessonsInModuleAsync(chatId, messageText, userSession, cancellationToken);
                return true;
            }
            else if (messageText.StartsWith("➡️ Урок:"))
            {
                await HandleShowLessonContentAsync(chatId, messageText, userSession, cancellationToken);
                return true;
            }
            // else
            // {
            //     // Do not call HandleUnknownCommandAsync here.
            //     // If no specific course command matched, return false so original handler can try.
            //     // await HandleUnknownCommandAsync(chatId, cancellationToken);
            // }
            return false; // Command not handled by this router
        }

        private async Task HandleStartCommandAsync(long chatId, UserSession userSession, CancellationToken cancellationToken) // This effectively becomes the main menu for the "course" section
        {
            userSession.CurrentModuleId = null;
            var replyKeyboardMarkup = MainMenuKeyboard.GetKeyboard();

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "Добро пожаловать в Omnieye Bot! Выберите опцию:",
                replyMarkup: replyKeyboardMarkup,
                cancellationToken: cancellationToken);
        }

        private async Task HandleShowCourseModulesAsync(long chatId, UserSession userSession, CancellationToken cancellationToken)
        {
            userSession.CurrentModuleId = null;
            var modules = _newCourseService.GetCourseModules(); // Use corrected field name
            var replyKeyboardMarkup = LessonKeyboard.GetModulesKeyboard(modules);

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "Выберите модуль:",
                replyMarkup: replyKeyboardMarkup,
                cancellationToken: cancellationToken);
        }

        private async Task HandleShowLessonsInModuleAsync(long chatId, string moduleMessage, UserSession userSession, CancellationToken cancellationToken)
        {
            var moduleTitle = moduleMessage.Replace("➡️ Модуль: ", "");
            var module = _newCourseService.GetCourseModules().FirstOrDefault(m => m.Title == moduleTitle); // Use corrected field name

            if (module == null)
            {
                await _botClient.SendTextMessageAsync(chatId: chatId, text: "Модуль не найден.", cancellationToken: cancellationToken);
                await HandleShowCourseModulesAsync(chatId, userSession, cancellationToken);
                return;
            }

            userSession.CurrentModuleId = module.Id;
            var replyKeyboardMarkup = LessonKeyboard.GetLessonsInModuleKeyboard(module);

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: $"Уроки в модуле \"{module.Title}\":",
                replyMarkup: replyKeyboardMarkup,
                cancellationToken: cancellationToken);
        }

        private async Task HandleShowLessonContentAsync(long chatId, string lessonMessage, UserSession userSession, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(userSession.CurrentModuleId))
            {
                await _botClient.SendTextMessageAsync(chatId: chatId, text: "Ошибка: Модуль не выбран. Пожалуйста, вернитесь и выберите модуль.", cancellationToken: cancellationToken);
                await HandleShowCourseModulesAsync(chatId, userSession, cancellationToken);
                return;
            }

            var lessonTitle = lessonMessage.Replace("➡️ Урок: ", "");
            var module = _newCourseService.GetModuleById(userSession.CurrentModuleId); // Use corrected field name
            var lesson = module?.Lessons.FirstOrDefault(l => l.Title == lessonTitle);

            if (lesson == null)
            {
                await _botClient.SendTextMessageAsync(chatId: chatId, text: "Урок не найден.", cancellationToken: cancellationToken);
                if (module != null) {
                     var replyKeyboardMarkupLessons = LessonKeyboard.GetLessonsInModuleKeyboard(module);
                     await _botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: $"Уроки в модуле \"{module.Title}\":",
                        replyMarkup: replyKeyboardMarkupLessons,
                        cancellationToken: cancellationToken);
                } else {
                    await HandleShowCourseModulesAsync(chatId, userSession, cancellationToken);
                }
                return;
            }

            var messageText = $"📖 *{lesson.Title}*\n\n{lesson.Content}";
            var replyKeyboardMarkup = LessonKeyboard.GetLessonContentKeyboard();

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: messageText,
                parseMode: ParseMode.Markdown,
                replyMarkup: replyKeyboardMarkup,
                cancellationToken: cancellationToken);
        }

        private async Task HandleBackCommandAsync(long chatId, UserSession userSession, CancellationToken cancellationToken)
        {
            // Simplified "Back" logic:
            // If CurrentModuleId is set, "Back" goes to the module list (clearing CurrentModuleId).
            // If CurrentModuleId is NOT set, "Back" goes to the main menu.
            // This means "Back" from lesson content will go to module list, not lesson list of the same module.
            // A more granular "CurrentView" property in UserSession would improve this.
            if (!string.IsNullOrEmpty(userSession.CurrentModuleId))
            {
                await HandleShowCourseModulesAsync(chatId, userSession, cancellationToken); // This also clears CurrentModuleId
            }
            else
            {
                await HandleStartCommandAsync(chatId, userSession, cancellationToken);
            }
        }

        private async Task HandleUnknownCommandAsync(long chatId, CancellationToken cancellationToken)
        {
            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "Извините, я не понял эту команду. Пожалуйста, используйте кнопки.",
                cancellationToken: cancellationToken);
        }
    }
}
