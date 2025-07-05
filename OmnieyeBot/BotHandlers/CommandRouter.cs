using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
// Removed: using Telegram.Bot.Types.ReplyMarkups; // No longer needed here
using OmnieyeBot.Services;
using OmnieyeBot.Models;
using OmnieyeBot.Keyboards; // For using MainMenuKeyboard, LessonKeyboard

namespace OmnieyeBot.BotHandlers
{
    public class CommandRouter
    {
        private readonly ITelegramBotClient _botClient;
        private readonly CourseService _courseService;
        private readonly UserSessionService _userSessionService;

        public CommandRouter(ITelegramBotClient botClient, CourseService courseService, UserSessionService userSessionService)
        {
            _botClient = botClient;
            _courseService = courseService;
            _userSessionService = userSessionService;
        }

        public async Task RouteAsync(Message message, CancellationToken cancellationToken)
        {
            var chatId = message.Chat.Id;
            var messageText = message.Text;
            var userSession = _userSessionService.GetSession(chatId);

            // Ensure CurrentModuleId is handled consistently, especially for "Назад"
            // Consider adding a "CurrentView" to UserSession for more robust back navigation

            if (messageText == "/start")
            {
                await HandleStartCommandAsync(chatId, userSession, cancellationToken);
            }
            else if (messageText == "📘 Уроки")
            {
                await HandleShowCourseModulesAsync(chatId, userSession, cancellationToken);
            }
            else if (messageText == "⬅️ Назад")
            {
                await HandleBackCommandAsync(chatId, userSession, cancellationToken);
            }
            else if (messageText.StartsWith("➡️ Модуль:"))
            {
                await HandleShowLessonsInModuleAsync(chatId, messageText, userSession, cancellationToken);
            }
            else if (messageText.StartsWith("➡️ Урок:"))
            {
                await HandleShowLessonContentAsync(chatId, messageText, userSession, cancellationToken);
            }
            else
            {
                await HandleUnknownCommandAsync(chatId, cancellationToken);
            }
        }

        private async Task HandleStartCommandAsync(long chatId, UserSession userSession, CancellationToken cancellationToken)
        {
            userSession.CurrentModuleId = null; // Reset context
            // userSession.CurrentView = ViewState.MainMenu; // If using ViewState

            var replyKeyboardMarkup = MainMenuKeyboard.GetKeyboard();

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "Добро пожаловать в Omnieye Bot! Выберите опцию:",
                replyMarkup: replyKeyboardMarkup,
                cancellationToken: cancellationToken);
        }

        private async Task HandleShowCourseModulesAsync(long chatId, UserSession userSession, CancellationToken cancellationToken)
        {
            userSession.CurrentModuleId = null; // Reset context when viewing modules
            // userSession.CurrentView = ViewState.ModuleList; // If using ViewState

            var modules = _courseService.GetCourseModules();
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
            var module = _courseService.GetCourseModules().FirstOrDefault(m => m.Title == moduleTitle);

            if (module == null)
            {
                await _botClient.SendTextMessageAsync(chatId: chatId, text: "Модуль не найден.", cancellationToken: cancellationToken);
                await HandleShowCourseModulesAsync(chatId, userSession, cancellationToken); // Go back to module list
                return;
            }

            userSession.CurrentModuleId = module.Id; // Set context
            // userSession.CurrentView = ViewState.LessonList; // If using ViewState

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
            var module = _courseService.GetModuleById(userSession.CurrentModuleId); // Use current module from session
            var lesson = module?.Lessons.FirstOrDefault(l => l.Title == lessonTitle);

            if (lesson == null)
            {
                await _botClient.SendTextMessageAsync(chatId: chatId, text: "Урок не найден.", cancellationToken: cancellationToken);
                // Attempt to show lessons for the current module again
                if (module != null) {
                     // userSession.CurrentView = ViewState.LessonList; // If using ViewState
                     var replyKeyboardMarkupLessons = LessonKeyboard.GetLessonsInModuleKeyboard(module);
                     await _botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: $"Уроки в модуле \"{module.Title}\":", // Resend lesson list
                        replyMarkup: replyKeyboardMarkupLessons,
                        cancellationToken: cancellationToken);
                } else {
                    // Fallback if module somehow became null
                    await HandleShowCourseModulesAsync(chatId, userSession, cancellationToken);
                }
                return;
            }

            // userSession.CurrentView = ViewState.LessonContent; // If using ViewState
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
            // This "Back" logic is still simplified.
            // A robust solution would use a "ViewState" in UserSession
            // or a stack of previous states to determine where to go back to.

            // Current logic:
            // If CurrentModuleId is set, it means we were in a lesson list or lesson content.
            // "Back" from lesson content should go to the lesson list of the *same module*.
            // "Back" from a lesson list should go to the module list (and clear CurrentModuleId).
            // "Back" from module list should go to main menu.

            // For this iteration, the "Назад" button is generic.
            // If CurrentModuleId IS SET, it means we are inside a module (either lesson list or content).
            // The "Back" button from lesson content (via GetLessonContentKeyboard) takes to lesson list.
            // The "Back" button from lesson list (via GetLessonsInModuleKeyboard) takes to module list.
            // The "Back" button from module list (via GetModulesKeyboard) takes to main menu.

            // The challenge is that this HandleBackCommandAsync is called for *any* "⬅️ Назад" press.
            // It needs to infer the context.

            // Let's assume:
            // If userSession.CurrentModuleId is set, they could be:
            //   1. Viewing lesson content (want to go to lesson list of CurrentModuleId)
            //   2. Viewing lesson list (want to go to module list, and clear CurrentModuleId)
            // If userSession.CurrentModuleId is NOT set, they are:
            //   3. Viewing module list (want to go to main menu)

            // The current keyboard setup:
            // - Lesson Content Keyboard has "Назад". If clicked, we want to show Lesson List for CurrentModuleId.
            // - Lesson List Keyboard has "Назад". If clicked, we want to show Module List (and clear CurrentModuleId).
            // - Module List Keyboard has "Назад". If clicked, we want to show Main Menu.

            // This implies the "Back" button functionality is tied to the *previous* view.
            // Without storing CurrentView, we make a best guess:
            if (!string.IsNullOrEmpty(userSession.CurrentModuleId))
            {
                // User is inside a module.
                // The most common "Back" from here would be from lesson content to lesson list,
                // or from lesson list to module list.
                // Let's check if the module still exists.
                var module = _courseService.GetModuleById(userSession.CurrentModuleId);
                if (module != null)
                {
                    // If they were viewing content, they'd go back to this module's lesson list.
                    // If they were viewing this module's lesson list, they'd go back to the main module list.
                    // This is where ViewState would distinguish.
                    // For now, if CurrentModuleId is set, pressing "Back" will take them to the list of lessons for that module.
                    // If they press "Back" *again* from that lesson list, CurrentModuleId is still set,
                    // so they would be shown the lesson list again. This is the flaw.

                    // Revised "Back" logic:
                    // To break the loop, we need a way to differentiate.
                    // A simple heuristic: if the "Back" is pressed and CurrentModuleId is set,
                    // we assume they want to go "up one level".
                    // "Up one level" from Lesson Content is Lesson List (CurrentModuleId remains).
                    // "Up one level" from Lesson List is Module List (CurrentModuleId gets cleared).

                    // Let's try this: if CurrentModuleId is set, the "Back" button means "go to module list".
                    // This simplifies the "Back" from lesson content to be a two-step:
                    // 1. Back from content -> module list (CurrentModuleId cleared)
                    // 2. Back from module list -> main menu
                    // This isn't ideal as "Back" from content should go to lesson list.

                    // The simplest, most consistent (though not perfectly granular) "Back" for now:
                    // If CurrentModuleId is set, "Back" always goes to the list of modules (and CurrentModuleId is cleared).
                    // If CurrentModuleId is NOT set, "Back" goes to the main menu.
                    await HandleShowCourseModulesAsync(chatId, userSession, cancellationToken); // This clears CurrentModuleId
                }
                else
                {
                    // Fallback if module ID in session is invalid
                    userSession.CurrentModuleId = null;
                    await HandleStartCommandAsync(chatId, userSession, cancellationToken);
                }
            }
            else
            {
                // CurrentModuleId is null, so we were in the module list (or main menu). "Back" goes to main menu.
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
