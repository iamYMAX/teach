using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Models; // Required for CourseModule, Lesson
// Assuming Services will be in a Services namespace
// using Services; // Required for CourseService, UserSessionService

public class Program
{
    private static ITelegramBotClient _botClient;
    private static CourseService _courseService;
    private static UserSessionService _userSessionService;

    public static async Task Main(string[] args)
    {
        // Replace "YOUR_BOT_TOKEN" with your actual bot token
        _botClient = new TelegramBotClient("YOUR_BOT_TOKEN");
        _courseService = new CourseService();
        _userSessionService = new UserSessionService();

        using var cts = new CancellationTokenSource();

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = Array.Empty<UpdateType>() // receive all update types
        };

        _botClient.StartReceiving(
            updateHandler: HandleUpdateAsync,
            pollingErrorHandler: HandlePollingErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: cts.Token
        );

        var me = await _botClient.GetMeAsync();
        Console.WriteLine($"Start listening for @{me.Username}");
        Console.ReadLine();

        cts.Cancel();
    }

    private static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        if (update.Type != UpdateType.Message)
            return;
        if (update.Message!.Type != MessageType.Text)
            return;

        var chatId = update.Message.Chat.Id;
        var messageText = update.Message.Text;

        Console.WriteLine($"Received a '{messageText}' message in chat {chatId}.");

        var userSession = _userSessionService.GetSession(chatId);

        if (messageText == "/start")
        {
            await ShowMainMenu(chatId, cancellationToken);
            return;
        }
        if (messageText == "⬅️ Назад")
        {
            if (!string.IsNullOrEmpty(userSession.CurrentModuleId))
            {
                // If a module is selected, go back to module list
                userSession.CurrentModuleId = null; // Clear selected module
                await ShowCourseModules(chatId, cancellationToken);
            }
            else
            {
                // If in module list or lesson content, go back to main menu
                await ShowMainMenu(chatId, cancellationToken);
            }
            return;
        }


        switch (messageText)
        {
            case "📘 Уроки":
                userSession.CurrentModuleId = null; // Clear any previously selected module
                await ShowCourseModules(chatId, cancellationToken);
                break;
            case string s when s.StartsWith("➡️ Модуль"):
                await ShowLessonsInModule(chatId, s, cancellationToken);
                break;
            case string s when s.StartsWith("➡️ Урок"):
                await ShowLessonContent(chatId, s, userSession.CurrentModuleId, cancellationToken);
                break;
            default:
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Извините, я не понял эту команду. Пожалуйста, используйте кнопки.",
                    cancellationToken: cancellationToken);
                break;
        }
    }

    private static Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        var errorMessage = exception switch
        {
            ApiRequestException apiRequestException
                => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
            _ => exception.ToString()
        };

        Console.WriteLine(errorMessage);
        return Task.CompletedTask;
    }

    private static async Task ShowMainMenu(long chatId, CancellationToken cancellationToken)
    {
        ReplyKeyboardMarkup replyKeyboardMarkup = new(new[]
        {
            new KeyboardButton[] { "📘 Уроки" },
            // Add other main menu buttons here if any
        })
        {
            ResizeKeyboard = true
        };

        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: "Добро пожаловать в Omnieye Bot! Выберите опцию:",
            replyMarkup: replyKeyboardMarkup,
            cancellationToken: cancellationToken);
    }

    private static async Task ShowCourseModules(long chatId, CancellationToken cancellationToken)
    {
        var modules = _courseService.GetCourseModules();
        var keyboardButtons = new List<KeyboardButton[]>();

        foreach (var module in modules)
        {
            keyboardButtons.Add(new KeyboardButton[] { $"➡️ Модуль: {module.Title}" });
        }
        keyboardButtons.Add(new KeyboardButton[] { "⬅️ Назад" }); // Back to Main Menu

        var replyKeyboardMarkup = new ReplyKeyboardMarkup(keyboardButtons)
        {
            ResizeKeyboard = true
        };

        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: "Выберите модуль:",
            replyMarkup: replyKeyboardMarkup,
            cancellationToken: cancellationToken);
    }

    private static async Task ShowLessonsInModule(long chatId, string moduleMessage, CancellationToken cancellationToken)
    {
        // Extract module title from message e.g., "➡️ Модуль: Введение в системное администрирование"
        var moduleTitle = moduleMessage.Replace("➡️ Модуль: ", "");
        var module = _courseService.GetCourseModules().FirstOrDefault(m => m.Title == moduleTitle);

        if (module == null)
        {
            await _botClient.SendTextMessageAsync(chatId: chatId, text: "Модуль не найден.", cancellationToken: cancellationToken);
            return;
        }

        var userSession = _userSessionService.GetSession(chatId);
        userSession.CurrentModuleId = module.Id; // Store selected module

        var keyboardButtons = new List<KeyboardButton[]>();
        foreach (var lesson in module.Lessons)
        {
            keyboardButtons.Add(new KeyboardButton[] { $"➡️ Урок: {lesson.Title}" });
        }
        keyboardButtons.Add(new KeyboardButton[] { "⬅️ Назад" }); // Back to Modules List

        var replyKeyboardMarkup = new ReplyKeyboardMarkup(keyboardButtons)
        {
            ResizeKeyboard = true
        };

        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: $"Уроки в модуле \"{module.Title}\":",
            replyMarkup: replyKeyboardMarkup,
            cancellationToken: cancellationToken);
    }

    private static async Task ShowLessonContent(long chatId, string lessonMessage, string currentModuleId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(currentModuleId))
        {
            await _botClient.SendTextMessageAsync(chatId: chatId, text: "Ошибка: Модуль не выбран. Пожалуйста, вернитесь и выберите модуль.", cancellationToken: cancellationToken);
            // Optionally, redirect to module selection
            await ShowCourseModules(chatId, cancellationToken);
            return;
        }

        // Extract lesson title from message e.g., "➡️ Урок: Кто такой системный администратор?"
        var lessonTitle = lessonMessage.Replace("➡️ Урок: ", "");
        var module = _courseService.GetModuleById(currentModuleId);
        var lesson = module?.Lessons.FirstOrDefault(l => l.Title == lessonTitle);

        if (lesson == null)
        {
            await _botClient.SendTextMessageAsync(chatId: chatId, text: "Урок не найден.", cancellationToken: cancellationToken);
            return;
        }

        var message = $"📖 *{lesson.Title}*\n\n{lesson.Content}";

        // Since we are showing content, we can offer a "Back" button to go back to the lessons list of the current module
        var keyboardButtons = new List<KeyboardButton[]>
        {
            new KeyboardButton[] { "⬅️ Назад" } // This will take user to lesson list of current module
        };
        var replyKeyboardMarkup = new ReplyKeyboardMarkup(keyboardButtons)
        {
            ResizeKeyboard = true
        };


        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: message,
            parseMode: ParseMode.Markdown,
            replyMarkup: replyKeyboardMarkup,
            cancellationToken: cancellationToken);
    }
}
