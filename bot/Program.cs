using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Omnieye.Bot.CoreModels; // For TestData, QuestionData, Lesson, Flashcard
using Omnieye.Bot.Services;
using Omnieye.Bot.States;     // For UserProfile, UserSession, TestHistoryEntry, UserCurrentState, TestDifficulty, LessonLevel
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using System.Text.Json; // Required for JsonSerializer
using Omnieye.Bot.Models; // Required for Test model if not already there for other reasons
using System.Timers; // Required for System.Timers.Timer
using IOFile = System.IO.File;

namespace Omnieye.Bot
{
    class Program
    {
        private static UserSessionService _userSessionService = new UserSessionService();
        private static AdminDataService _adminDataService = new AdminDataService("bot_data"); // Added AdminDataService
        private static MaterialLoader _materialLoader = new MaterialLoader();
        private static TestLoaderService _testLoaderService = new TestLoaderService(); // Added for accessing tests

        private static ITelegramBotClient? _botClient;
        private static CancellationTokenSource? _cts;

        // Old simple lists - effectively deprecated
        // private static readonly List<string> availableLessons_OLD_FORMAT = new List<string>
        // {
        //     "Урок 1: Введение в систему", "Урок 2: Основы работы", "Урок 3: Продвинутые возможности"
        // };
        // private static readonly Dictionary<int, string> lessonDetails_OLD_FORMAT = new Dictionary<int, string>
        // {
        //     { 1, "Урок 1: Введение в систему\n\nЗдесь рассказывается об основах работы с ботом и системой." },
        //     { 2, "Урок 2: Основы работы\n\nОписание основных функций и интерфейса." },
        //     { 3, "Урок 3: Продвинутые возможности\n\nДополнительные настройки и советы." }
        // };
        //  private static readonly List<string> availableTests_OLD_FORMAT = new List<string>
        // {
        //     "Тест 1: Проверка знаний по основам", "Тест 2: Продвинутый тест"
        // };

        // testDetails is used by HandleTestDetailAsync. It should be reviewed if it's still needed
        // or if TestData.Description should be used instead.
        // For now, keeping it to minimize breaking changes, but it might need refactoring.
        private static readonly Dictionary<string, string> testDetails = new Dictionary<string, string>
        {
            { "1", "Тест 1: Проверка знаний по основам\n\nВключает вопросы по базовым темам." },
            { "2", "Тест 2: Продвинутый тест\n\nСложные вопросы для опытных пользователей." }
        };

        private static readonly ReplyKeyboardMarkup MainCommandKeyboard = new ReplyKeyboardMarkup(new[]
        {
            new KeyboardButton[] { new KeyboardButton("📘 Уроки"), new KeyboardButton("🧪 Тесты") },
            new KeyboardButton[] { new KeyboardButton("🧠 Флеш-карточки"), new KeyboardButton("История") },
            new KeyboardButton[] { new KeyboardButton("🏆 Топ"), new KeyboardButton("👤 Профиль") },
            new KeyboardButton[] { new KeyboardButton("🔐 Выйти") }
        })
        {
            ResizeKeyboard = true
        };

        private static readonly ReplyKeyboardMarkup LessonDetailKeyboard = new ReplyKeyboardMarkup(new[]
        {
            new KeyboardButton[] { "Назад к списку уроков" }
        })
        {
            ResizeKeyboard = true
        };

        private static readonly ReplyKeyboardMarkup TestDetailKeyboard = new ReplyKeyboardMarkup(new[]
        {
            new KeyboardButton[] { "Начать тест", "Назад" }
        })
        {
            ResizeKeyboard = true
        };

        private static readonly ReplyKeyboardMarkup AfterTestMenuKeyboard = new ReplyKeyboardMarkup(new[]
        {
            new KeyboardButton[] { "Вернуться в меню" }
        })
        {
            ResizeKeyboard = true,
            OneTimeKeyboard = true
        };

        private static readonly ReplyKeyboardMarkup FlashcardQuestionKeyboard = new ReplyKeyboardMarkup(new[]
        {
            new KeyboardButton[] { new KeyboardButton("Показать ответ") },
            new KeyboardButton[] { new KeyboardButton("Следующая карточка") },
            new KeyboardButton[] { new KeyboardButton("↩ Меню") }
        })
        {
            ResizeKeyboard = true
        };

        // activeTestsData is now managed by AdminDataService. This static variable is removed.
        // Methods needing test data will call _adminDataService.GetAllTests() or _adminDataService.GetTestById(string id).
        // private static readonly Dictionary<int, TestData> activeTestsData = new Dictionary<int, TestData> { ... };

        // allLessonsData is now managed by AdminDataService. This static variable is removed.
        // Methods needing lesson data will call _adminDataService.GetAllLessons() or _adminDataService.GetLessonById(string id).
        // private static readonly List<Lesson> allLessonsData = new List<Lesson> { ... };

        private static readonly List<Flashcard> allFlashcardsData = new List<Flashcard> // Assuming flashcards remain static for now
        {
            new Flashcard("Что такое DHCP?", "Протокол динамической настройки узла (Dynamic Host Configuration Protocol), позволяющий устройствам автоматически получать IP-адреса и другие сетевые параметры.", LessonLevel.Beginner),
            new Flashcard("Назовите 3 уровня модели OSI.", "Физический, Канальный, Сетевой (или любые три из семи: Физический, Канальный, Сетевой, Транспортный, Сеансовый, Представления, Прикладной).", LessonLevel.Beginner),
            new Flashcard("Что означает VPN?", "Virtual Private Network (Виртуальная Частная Сеть) - технология, позволяющая создавать безопасное зашифрованное соединение поверх другой сети (обычно Интернет).", LessonLevel.Intermediate),
            new Flashcard("Для чего используется порт 22/TCP?", "Для протокола SSH (Secure Shell), обеспечивающего безопасное удаленное управление.", LessonLevel.Intermediate),
            new Flashcard("Что такое контейнеризация?", "Метод виртуализации на уровне операционной системы, позволяющий упаковывать приложение и его зависимости в изолированные окружения (контейнеры).", LessonLevel.Advanced),
            new Flashcard("Кратко опишите CI/CD.", "Continuous Integration / Continuous Delivery (or Deployment) - практики автоматизации сборки, тестирования и развертывания программного обеспечения.", LessonLevel.Advanced)
        };

        static async Task Main(string[] args)
        {
            Console.WriteLine("Omnieye Telegram Bot starting...");
            var botToken = Environment.GetEnvironmentVariable("OMNIEYE_BOT_TOKEN") ?? "YOUR_BOT_TOKEN_HERE";
            if (botToken == "YOUR_BOT_TOKEN_HERE")
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("CRITICAL: Bot token is not set. Please set the OMNIEYE_BOT_TOKEN environment variable or replace the placeholder in code.");
                Console.ResetColor();
                Console.WriteLine("Press any key to exit.");
                Console.ReadKey();
                return;
            }

            _botClient = new TelegramBotClient(botToken);
            _cts = new CancellationTokenSource();

            // Start services like auto-backup
            StartAutoBackup(); // Initialize and start the auto-backup timer

            var receiverOptions = new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() };
            _botClient.StartReceiving(
                updateHandler: HandleUpdateAsync,
                pollingErrorHandler: HandlePollingErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: _cts.Token);

            var me = await _botClient.GetMeAsync();
            Console.WriteLine($"Bot @{me.Username} started and listening for messages. Press Ctrl+C to exit.");

            var tcs = new TaskCompletionSource<bool>();
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                eventArgs.Cancel = true;
                _cts.Cancel();
                tcs.SetResult(true);
                Console.WriteLine("Ctrl+C pressed. Shutting down...");
            };
            await tcs.Task;

            Console.WriteLine("Bot shutting down gracefully...");
            await _userSessionService.SaveSessionsToFileAsync();
            Console.WriteLine("Sessions saved. Exiting.");
        }

        static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            // Handle CallbackQueries first
            if (update.Type == UpdateType.CallbackQuery)
            {
                if (update.CallbackQuery is not { } callbackQuery) return;
                if (callbackQuery.From is not { } callbackUser) return;
                if (callbackQuery.Message is not { } callbackMessage) return; // Ensure message context exists

                long userId = callbackUser.Id;
                long chatId = callbackMessage.Chat.Id; // Use chat from message for sending replies
                var session = _userSessionService.GetUserSession(userId);
                string? callbackData = callbackQuery.Data;

                Console.WriteLine($"Received CallbackQuery '{callbackData}' from User {userId} in Chat {chatId}. State: {session.CurrentState}");

                if (IsAdmin(userId))
                {
                    if (callbackData != null && callbackData.StartsWith("admin_edit_lesson_"))
                    {
                        string lessonId = callbackData.Substring("admin_edit_lesson_".Length);
                        session.EditingItemId = lessonId;
                        session.CurrentState = UserCurrentState.AdminEditingLessonSelectField;

                        var fieldKeyboard = new InlineKeyboardMarkup(new[]
                        {
                            new[] { InlineKeyboardButton.WithCallbackData("Название", $"admin_edit_field_title") },
                            new[] { InlineKeyboardButton.WithCallbackData("Описание", $"admin_edit_field_description") },
                            new[] { InlineKeyboardButton.WithCallbackData("Содержание", $"admin_edit_field_content") },
                            new[] { InlineKeyboardButton.WithCallbackData("Уровень", $"admin_edit_field_level") }
                        });

                        await botClient.EditMessageTextAsync(chatId, callbackMessage.MessageId, "Выбран урок. Какое поле редактировать?", replyMarkup: fieldKeyboard, cancellationToken: cancellationToken);
                        await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken); // Acknowledge callback
                        return; // Callback processed
                    }
                    else if (callbackData != null && callbackData.StartsWith("admin_edit_field_") && session.CurrentState == UserCurrentState.AdminEditingLessonSelectField)
                    {
                        string fieldToEdit = callbackData.Substring("admin_edit_field_".Length);
                        session.EditingField = fieldToEdit;
                        session.CurrentState = UserCurrentState.AdminEditingLessonEnterNewValue;

                        if (fieldToEdit == "level")
                        {
                            var levelKeyboard = new ReplyKeyboardMarkup(new[] // Using ReplyKeyboard for this input
                            {
                                new[] { new KeyboardButton(LessonLevel.Beginner.ToString()), new KeyboardButton(LessonLevel.Intermediate.ToString()) },
                                new[] { new KeyboardButton(LessonLevel.Advanced.ToString()) }
                            }) { ResizeKeyboard = true, OneTimeKeyboard = true };
                            await botClient.EditMessageReplyMarkupAsync(chatId, callbackMessage.MessageId, replyMarkup: null, cancellationToken: cancellationToken); // Remove inline keyboard
                            await botClient.SendTextMessageAsync(chatId, $"Выбран урок (ID: {session.EditingItemId?.Substring(0,8)}...). Введите новый уровень:", replyMarkup: levelKeyboard, cancellationToken: cancellationToken);
                        }
                        else
                        {
                            await botClient.EditMessageReplyMarkupAsync(chatId, callbackMessage.MessageId, replyMarkup: null, cancellationToken: cancellationToken); // Remove inline keyboard
                            await botClient.SendTextMessageAsync(chatId, $"Выбран урок (ID: {session.EditingItemId?.Substring(0,8)}...). Введите новое значение для поля '{fieldToEdit}':", cancellationToken: cancellationToken);
                        }
                        await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken);
                        return; // Callback processed
                    }
                    else if (callbackData != null && callbackData.StartsWith("admin_delete_lesson_") && session.CurrentState == UserCurrentState.AdminDeletingLessonSelect)
                    {
                        string lessonId = callbackData.Substring("admin_delete_lesson_".Length);
                        var lessonToDelete = _adminDataService.GetLessonById(lessonId);
                        if (lessonToDelete == null)
                        {
                            await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Урок не найден.", cancellationToken: cancellationToken);
                            await GoToAdminRoot(botClient, session, chatId, "Ошибка: урок для удаления не найден.", cancellationToken);
                            return;
                        }

                        session.EditingItemId = lessonId; // Using EditingItemId to store ID of item to be deleted
                        var confirmationKeyboard = new InlineKeyboardMarkup(new[]
                        {
                            InlineKeyboardButton.WithCallbackData($"Да, удалить \"{lessonToDelete.Title.Substring(0, Math.Min(lessonToDelete.Title.Length, 20))}...\"", $"admin_confirm_delete_lesson_{lessonId}"),
                            InlineKeyboardButton.WithCallbackData("Нет, отмена", "admin_cancel_delete")
                        });
                        await botClient.EditMessageTextAsync(chatId, callbackMessage.MessageId, $"Вы уверены, что хотите удалить урок \"{lessonToDelete.Title}\"?", replyMarkup: confirmationKeyboard, cancellationToken: cancellationToken);
                        await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken);
                        return;
                    }
                    else if (callbackData != null && callbackData.StartsWith("admin_confirm_delete_lesson_") && session.CurrentState == UserCurrentState.AdminDeletingLessonSelect)
                    {
                        if (string.IsNullOrEmpty(session.EditingItemId) || !callbackData.EndsWith(session.EditingItemId)) // Basic check
                        {
                             await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Ошибка подтверждения.", cancellationToken: cancellationToken);
                             await GoToAdminRoot(botClient, session, chatId, "Ошибка при подтверждении удаления.", cancellationToken);
                             return;
                        }
                        try
                        {
                            await _adminDataService.DeleteLesson(session.EditingItemId);
                            await botClient.EditMessageTextAsync(chatId, callbackMessage.MessageId, $"Урок (ID: {session.EditingItemId.Substring(0,8)}...) успешно удален.", cancellationToken: cancellationToken);
                            await GoToAdminRoot(botClient, session, chatId, "Выберите следующее действие:", cancellationToken);
                        }
                        catch (KeyNotFoundException)
                        {
                            await botClient.EditMessageTextAsync(chatId, callbackMessage.MessageId, "Ошибка: Урок не найден для удаления (возможно, уже удален).", cancellationToken: cancellationToken);
                            await GoToAdminRoot(botClient, session, chatId, "Выберите следующее действие:", cancellationToken);
                        }
                        session.EditingItemId = null;
                        await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken);
                        return;
                    }
                    else if (callbackData == "admin_cancel_delete" && session.CurrentState == UserCurrentState.AdminDeletingLessonSelect)
                    {
                        await botClient.EditMessageTextAsync(chatId, callbackMessage.MessageId, "Удаление отменено.", cancellationToken: cancellationToken);
                        await GoToAdminRoot(botClient, session, chatId, "Выберите следующее действие:", cancellationToken);
                        await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken);
                        return;
                    }
                    else if (callbackData != null && callbackData.StartsWith("admin_edit_test_") && session.CurrentState == UserCurrentState.AdminEditingTestSelect)
                    {
                        string testId = callbackData.Substring("admin_edit_test_".Length);
                        var testToEdit = _adminDataService.GetTestById(testId);
                        if (testToEdit == null)
                        {
                            await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Тест не найден.", cancellationToken: cancellationToken);
                            await GoToAdminRoot(botClient, session, chatId, "Ошибка: тест для редактирования не найден.", cancellationToken);
                            return;
                        }
                        session.EditingItemId = testId; // Store ID of test being edited
                        session.CurrentState = UserCurrentState.AdminEditingTestSelectField; // Or a new state like AdminEditingTestMainMenu

                        var editTestOptionsKeyboard = new InlineKeyboardMarkup(new[]
                        {
                            new[] { InlineKeyboardButton.WithCallbackData("Изменить Название", $"admin_edit_testmeta_title") },
                            new[] { InlineKeyboardButton.WithCallbackData("Изменить Описание", $"admin_edit_testmeta_description") },
                            new[] { InlineKeyboardButton.WithCallbackData("Изменить Сложность", $"admin_edit_testmeta_difficulty") },
                            new[] { InlineKeyboardButton.WithCallbackData("Редактировать Вопросы", $"admin_edit_testquestions_{testId}") }, // Navigate to question editing
                            new[] { InlineKeyboardButton.WithCallbackData("<< Назад к выбору теста", $"admin_back_to_test_select_for_edit") }
                        });
                        await botClient.EditMessageTextAsync(chatId, callbackMessage.MessageId, $"Выбран тест: \"{testToEdit.TestName}\". Что вы хотите сделать?", replyMarkup: editTestOptionsKeyboard, cancellationToken: cancellationToken);
                        await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken);
                        return;
                    }
                    else if (callbackData != null && callbackData.StartsWith("admin_edit_testmeta_") && session.CurrentState == UserCurrentState.AdminEditingTestSelectField)
                    {
                        string fieldToEdit = callbackData.Substring("admin_edit_testmeta_".Length);
                        session.EditingField = fieldToEdit; // Store the metadata field to edit (title, description, difficulty)
                        session.CurrentState = UserCurrentState.AdminEditingTestEnterNewValue; // Re-use state from lesson editing for entering new value

                        if (fieldToEdit == "difficulty")
                        {
                            var difficultyKeyboard = new ReplyKeyboardMarkup(new[]
                            {
                                new[] { new KeyboardButton(TestDifficulty.Easy.ToString()), new KeyboardButton(TestDifficulty.Medium.ToString()) },
                                new[] { new KeyboardButton(TestDifficulty.Hard.ToString()) }
                            }) { ResizeKeyboard = true, OneTimeKeyboard = true };
                            await botClient.EditMessageReplyMarkupAsync(chatId, callbackMessage.MessageId, replyMarkup: null, cancellationToken: cancellationToken); // Remove inline
                            await botClient.SendTextMessageAsync(chatId, $"Текущий тест: {session.EditingItemId?.Substring(0,8)}...\nВведите новую сложность:", replyMarkup: difficultyKeyboard, cancellationToken: cancellationToken);
                        }
                        else
                        {
                             await botClient.EditMessageReplyMarkupAsync(chatId, callbackMessage.MessageId, replyMarkup: null, cancellationToken: cancellationToken); // Remove inline
                            await botClient.SendTextMessageAsync(chatId, $"Текущий тест: {session.EditingItemId?.Substring(0,8)}...\nВведите новое значение для поля '{fieldToEdit}':", cancellationToken: cancellationToken);
                        }
                        await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken);
                        return;
                    }
                     else if (callbackData == "admin_back_to_test_select_for_edit" && session.CurrentState == UserCurrentState.AdminEditingTestSelectField)
                    {
                        // This will effectively re-trigger the "✏️ Редактировать тест" flow's display part
                        session.CurrentState = UserCurrentState.AdminRoot; // Go to root to allow re-selection of "Edit Test"
                        await botClient.DeleteMessageAsync(chatId, callbackMessage.MessageId, cancellationToken); // Clean up the message
                        // Simulate pressing "Edit Test" again by sending the selection message
                        // This is a bit of a hack; ideally, this would be a direct state transition + message send.
                        // For simplicity here, we'll just guide the user back.
                        // A cleaner way is to have a dedicated method to show the test list for editing.
                        // For now, let's just send them to admin root and they can click "Edit Test" again.
                        await GoToAdminRoot(botClient, session, chatId, "Выберите тест для редактирования из списка (нажмите '✏️ Редактировать тест' снова).", cancellationToken);
                        await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken);
                        return;
                    }
                     else if (callbackData != null && callbackData.StartsWith("admin_delete_test_") && session.CurrentState == UserCurrentState.AdminDeletingTestSelect)
                    {
                        string testId = callbackData.Substring("admin_delete_test_".Length);
                        var testToDelete = _adminDataService.GetTestById(testId);
                        if (testToDelete == null)
                        {
                            await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Тест не найден.", cancellationToken: cancellationToken);
                            await GoToAdminRoot(botClient, session, chatId, "Ошибка: тест для удаления не найден.", cancellationToken);
                            return;
                        }

                        session.EditingItemId = testId; // Re-use for item to be deleted
                        var confirmationKeyboard = new InlineKeyboardMarkup(new[]
                        {
                            InlineKeyboardButton.WithCallbackData($"Да, удалить \"{testToDelete.TestName.Substring(0, Math.Min(testToDelete.TestName.Length, 20))}...\"", $"admin_confirm_delete_test_{testId}"),
                            InlineKeyboardButton.WithCallbackData("Нет, отмена", "admin_cancel_delete_test") // Specific cancel for test
                        });
                        await botClient.EditMessageTextAsync(chatId, callbackMessage.MessageId, $"Вы уверены, что хотите удалить тест \"{testToDelete.TestName}\"?", replyMarkup: confirmationKeyboard, cancellationToken: cancellationToken);
                        await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken);
                        return;
                    }
                    else if (callbackData != null && callbackData.StartsWith("admin_confirm_delete_test_") && session.CurrentState == UserCurrentState.AdminDeletingTestSelect)
                    {
                        if (string.IsNullOrEmpty(session.EditingItemId) || !callbackData.EndsWith(session.EditingItemId))
                        {
                             await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Ошибка подтверждения.", cancellationToken: cancellationToken);
                             await GoToAdminRoot(botClient, session, chatId, "Ошибка при подтверждении удаления теста.", cancellationToken);
                             return;
                        }
                        try
                        {
                            await _adminDataService.DeleteTest(session.EditingItemId);
                            await botClient.EditMessageTextAsync(chatId, callbackMessage.MessageId, $"Тест (ID: {session.EditingItemId.Substring(0,8)}...) успешно удален.", cancellationToken: cancellationToken);
                            await GoToAdminRoot(botClient, session, chatId, "Выберите следующее действие:", cancellationToken);
                        }
                        catch (KeyNotFoundException)
                        {
                            await botClient.EditMessageTextAsync(chatId, callbackMessage.MessageId, "Ошибка: Тест не найден для удаления (возможно, уже удален).", cancellationToken: cancellationToken);
                            await GoToAdminRoot(botClient, session, chatId, "Выберите следующее действие:", cancellationToken);
                        }
                        session.EditingItemId = null;
                        await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken);
                        return;
                    }
                    else if (callbackData == "admin_cancel_delete_test" && session.CurrentState == UserCurrentState.AdminDeletingTestSelect)
                    {
                        await botClient.EditMessageTextAsync(chatId, callbackMessage.MessageId, "Удаление теста отменено.", cancellationToken: cancellationToken);
                        await GoToAdminRoot(botClient, session, chatId, "Выберите следующее действие:", cancellationToken);
                        await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken);
                        return;
                    }
                    else if (callbackData != null && callbackData.StartsWith("admin_edit_testquestions_") && session.CurrentState == UserCurrentState.AdminEditingTestSelectField)
                    {
                        string testId = callbackData.Substring("admin_edit_testquestions_".Length);
                        // session.EditingItemId should already be set to this testId from the previous step
                        if (session.EditingItemId != testId)
                        {
                            // Mismatch, something went wrong or state is inconsistent
                            await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Ошибка: ID теста не совпадает.", cancellationToken: cancellationToken);
                            await GoToAdminRoot(botClient, session, chatId, "Произошла ошибка при выборе теста для редактирования вопросов.", cancellationToken);
                            return;
                        }

                        var testToEdit = _adminDataService.GetTestById(testId);
                        if (testToEdit == null)
                        {
                            await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Тест не найден.", cancellationToken: cancellationToken);
                            await GoToAdminRoot(botClient, session, chatId, "Ошибка: тест для редактирования вопросов не найден.", cancellationToken);
                            return;
                        }

                        session.CurrentState = UserCurrentState.AdminEditingTestQuestionSelect; // New state for managing questions of a test

                        var qKeyboard = new List<IEnumerable<InlineKeyboardButton>>();
                        qKeyboard.Add(new[] { InlineKeyboardButton.WithCallbackData("Добавить новый вопрос к этому тесту", $"admin_add_qst_to_test_{testId}") });

                        if (testToEdit.Questions.Any())
                        {
                            for(int i=0; i < testToEdit.Questions.Count; i++)
                            {
                                var q = testToEdit.Questions[i];
                                string qTextShort = q.Text.Length > 30 ? q.Text.Substring(0, 27) + "..." : q.Text;
                                qKeyboard.Add(new[] {
                                    InlineKeyboardButton.WithCallbackData($"✏️ {i+1}. {qTextShort}", $"admin_edit_existing_qst_{testId}_{i}"), // Pass testId and questionIndex
                                    InlineKeyboardButton.WithCallbackData($"🗑️ Удалить", $"admin_delete_existing_qst_{testId}_{i}")
                                });
                            }
                        }
                        else
                        {
                             qKeyboard.Add(new[] { InlineKeyboardButton.WithCallbackData("Вопросов пока нет.", "admin_no_op") });
                        }
                        qKeyboard.Add(new[] { InlineKeyboardButton.WithCallbackData("<< Назад к редактированию теста", $"admin_edit_test_{testId}") }); // Go back to test metadata edit

                        await botClient.EditMessageTextAsync(chatId, callbackMessage.MessageId,
                            $"Редактирование вопросов для теста: \"{testToEdit.TestName}\"\nВсего вопросов: {testToEdit.Questions.Count}",
                            replyMarkup: new InlineKeyboardMarkup(qKeyboard), cancellationToken: cancellationToken);
                        await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken);
                        return;
                    }
                    else if (callbackData != null && callbackData.StartsWith("admin_add_qst_to_test_") && session.CurrentState == UserCurrentState.AdminEditingTestQuestionSelect)
                    {
                        string testId = callbackData.Substring("admin_add_qst_to_test_".Length);
                        if (session.EditingItemId != testId) { /* Error handling */ return; }

                        session.PendingQuestion = new QuestionData();
                        session.CurrentState = UserCurrentState.AdminAddingTestQuestionText; // Re-use this state
                        // Important: We need a way to know that after this question is added, we return to question list, not "ask more"
                        // For now, the AdminAddingTestQuestionCorrectOption handler will need to check if EditingItemId is set.
                        // If EditingItemId is set, it means we are adding a question to an existing test.
                        await botClient.EditMessageReplyMarkupAsync(chatId, callbackMessage.MessageId, replyMarkup: null, cancellationToken: cancellationToken); // Clear inline keyboard
                        await botClient.SendTextMessageAsync(chatId, "Введите текст нового вопроса для этого теста:", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
                        await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken);
                        return;
                    }
                    else if (callbackData != null && callbackData.StartsWith("admin_edit_existing_qst_") && session.CurrentState == UserCurrentState.AdminEditingTestQuestionSelect)
                    {
                        // Format: admin_edit_existing_qst_{testId}_{questionIndex}
                        var parts = callbackData.Split('_');
                        if (parts.Length < 5) { /* Error */ return; } // Should be 5 parts: admin, edit, existing, qst, testIdAndIndex

                        string testId = parts[4]; // Assuming testId does not contain underscores
                        // If testId can contain underscores, parsing needs to be more robust.
                        // For now, assuming testId is the 4th part and questionIndex is the 5th if it were testId_questionIndex
                        // Let's refine: admin_edit_existing_qst_TESTID_INDEX
                        // So parts[0]=admin, parts[1]=edit, parts[2]=existing, parts[3]=qst, parts[4]=TESTID, parts[5]=INDEX

                        // A safer way: find last underscore for index, and the one before that for testId start
                        int lastUnderscore = callbackData.LastIndexOf('_');
                        if (lastUnderscore == -1 || lastUnderscore == callbackData.Length -1) { /* Error */ return;}
                        if (!int.TryParse(callbackData.Substring(lastUnderscore + 1), out int qIndex)) { /* Error */ return; }

                        string prefix = "admin_edit_existing_qst_";
                        string testIdFromCallback = callbackData.Substring(prefix.Length, lastUnderscore - prefix.Length);

                        if (session.EditingItemId != testIdFromCallback) { /* Error: Test ID mismatch */ return; }

                        var test = _adminDataService.GetTestById(testIdFromCallback);
                        if (test == null || qIndex < 0 || qIndex >= test.Questions.Count) { /* Error: Test or question not found */ return; }

                        session.EditingQuestionIndex = qIndex;
                        var questionToEdit = test.Questions[qIndex];
                        session.CurrentState = UserCurrentState.AdminEditingTestQuestionEditField; // New state for selecting WHICH part of question to edit

                        var editQuestionPartKeyboard = new InlineKeyboardMarkup(new[]
                        {
                            new[] { InlineKeyboardButton.WithCallbackData("Текст вопроса", $"admin_edit_qstpart_text_{testIdFromCallback}_{qIndex}") },
                            new[] { InlineKeyboardButton.WithCallbackData("Варианты ответов", $"admin_edit_qstpart_options_{testIdFromCallback}_{qIndex}") },
                            new[] { InlineKeyboardButton.WithCallbackData("Правильный ответ", $"admin_edit_qstpart_correct_{testIdFromCallback}_{qIndex}") },
                            new[] { InlineKeyboardButton.WithCallbackData("<< Назад к списку вопросов", $"admin_edit_testquestions_{testIdFromCallback}")}
                        });

                        await botClient.EditMessageTextAsync(chatId, callbackMessage.MessageId,
                            $"Редактирование вопроса {qIndex+1}: \"{questionToEdit.Text.Substring(0, Math.Min(questionToEdit.Text.Length,30))}...\"\nЧто изменить?",
                            replyMarkup: editQuestionPartKeyboard, cancellationToken: cancellationToken);
                        await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken);
                        return;
                    }
                    else if (callbackData != null && callbackData.StartsWith("admin_edit_qstpart_") && session.CurrentState == UserCurrentState.AdminEditingTestQuestionEditField)
                    {
                        // Format: admin_edit_qstpart_{type}_{testId}_{questionIndex}
                        var parts = callbackData.Split('_'); // admin, edit, qstpart, type, testId, qIndex
                        if (parts.Length < 6) { /* Error */ return; }
                        string type = parts[3];
                        string testId = parts[4];
                        if (!int.TryParse(parts[5], out int qIndex)) { /* Error */ return;}

                        if (session.EditingItemId != testId || session.EditingQuestionIndex != qIndex) { /* Error: Mismatch */ return;}

                        session.EditingField = $"question_{type}"; // e.g., question_text, question_options
                        session.CurrentState = UserCurrentState.AdminEditingTestQuestionEnterNewValue;

                        string prompt = "";
                        IReplyMarkup? replyMarkup = new ReplyKeyboardRemove();

                        switch(type)
                        {
                            case "text":
                                prompt = "Введите новый текст вопроса:";
                                break;
                            case "options":
                                prompt = "Введите новые варианты ответа через запятую (например: ОпцияА,ОпцияБ,ОпцияВ):";
                                break;
                            case "correct":
                                var test = _adminDataService.GetTestById(testId);
                                if (test != null && qIndex < test.Questions.Count)
                                {
                                    var q = test.Questions[qIndex];
                                    var optionsText = string.Join("\n", q.Options.Select((opt, idx) => $"{idx + 1}. {opt}"));
                                    prompt = $"Текущие варианты:\n{optionsText}\n\nВведите НОВЫЙ номер правильного варианта (начиная с 1):";
                                } else { prompt = "Введите НОВЫЙ номер правильного варианта (начиная с 1):"; }
                                break;
                            default: // Should not happen
                                await GoToAdminRoot(botClient, session, chatId, "Неизвестное поле для редактирования вопроса.",cancellationToken);
                                return;
                        }
                        await botClient.EditMessageReplyMarkupAsync(chatId, callbackMessage.MessageId, replyMarkup: null, cancellationToken: cancellationToken); // Remove inline keyboard
                        await botClient.SendTextMessageAsync(chatId, prompt, replyMarkup: replyMarkup, cancellationToken: cancellationToken);
                        await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken);
                        return;
                    }
                    else if (callbackData != null && callbackData.StartsWith("admin_delete_existing_qst_") && session.CurrentState == UserCurrentState.AdminEditingTestQuestionSelect)
                    {
                        // Format: admin_delete_existing_qst_{testId}_{questionIndex}
                        int lastUnderscore = callbackData.LastIndexOf('_');
                        if (lastUnderscore == -1 || lastUnderscore == callbackData.Length -1) { /* Error */ return;}
                        if (!int.TryParse(callbackData.Substring(lastUnderscore + 1), out int qIndex)) { /* Error */ return; }

                        string prefix = "admin_delete_existing_qst_";
                        string testId = callbackData.Substring(prefix.Length, lastUnderscore - prefix.Length);

                        if (session.EditingItemId != testId) { /* Error: Test ID mismatch */ return; }

                        var test = _adminDataService.GetTestById(testId);
                        if (test == null || qIndex < 0 || qIndex >= test.Questions.Count) { /* Error: Test or question not found */ return; }

                        var questionToDelete = test.Questions[qIndex];
                        // Confirmation for deleting a question could be added here with another callback step,
                        // but for simplicity now, let's delete directly. If UX requires confirmation, this needs expansion.

                        test.Questions.RemoveAt(qIndex);
                        await _adminDataService.UpdateTest(test);

                        await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, $"Вопрос \"{questionToDelete.Text.Substring(0, Math.Min(questionToDelete.Text.Length, 20))}...\" удален.", cancellationToken: cancellationToken);

                        // Refresh question list
                        var qKeyboardRefresh = new List<IEnumerable<InlineKeyboardButton>>();
                        qKeyboardRefresh.Add(new[] { InlineKeyboardButton.WithCallbackData("Добавить новый вопрос к этому тесту", $"admin_add_qst_to_test_{test.Id}") });
                        if (test.Questions.Any())
                        {
                            for(int i=0; i < test.Questions.Count; i++)
                            {
                                var q = test.Questions[i];
                                string qTextShort = q.Text.Length > 30 ? q.Text.Substring(0, 27) + "..." : q.Text;
                                qKeyboardRefresh.Add(new[] {
                                    InlineKeyboardButton.WithCallbackData($"✏️ {i+1}. {qTextShort}", $"admin_edit_existing_qst_{test.Id}_{i}"),
                                    InlineKeyboardButton.WithCallbackData($"🗑️ Удалить", $"admin_delete_existing_qst_{test.Id}_{i}")
                                });
                            }
                        } else { qKeyboardRefresh.Add(new[] { InlineKeyboardButton.WithCallbackData("Вопросов пока нет.", "admin_no_op") }); }
                        qKeyboardRefresh.Add(new[] { InlineKeyboardButton.WithCallbackData("<< Назад к редактированию теста", $"admin_edit_test_{test.Id}") });

                        await botClient.EditMessageTextAsync(chatId, callbackMessage.MessageId,
                            $"Редактирование вопросов для теста: \"{test.TestName}\"\nВсего вопросов: {test.Questions.Count}",
                            replyMarkup: new InlineKeyboardMarkup(qKeyboardRefresh), cancellationToken: cancellationToken);
                        // No return here, let it fall through to AnswerCallbackQueryAsync if not handled.
                    }
                }
                // Fallback or other callback query handling if necessary
                // Ensure AnswerCallbackQueryAsync is always called for any callback.
                if(callbackQuery!=null && !string.IsNullOrEmpty(callbackQuery.Id) && !callbackQuery.IsAcknowledged()) await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Действие не обработано.", cancellationToken: cancellationToken);
                return;
            }

            if (update.Message is not { } message) return;
            if (message.From is not { } user) return;
            // messageText can be null for non-text messages, handle accordingly or check type.
            // For this bot, we primarily expect text messages after this point.
            if (message.Text is not { } messageText)
            {
                // If it's not a text message and not a callback, ignore or handle specific non-text types if needed.
                // For example, if the bot expects photos, documents, etc.
                // For now, if not text and not callback, we assume it's not meant for standard processing.
                Console.WriteLine($"Received non-text message type {message.Type} from User {user.Id}. Ignoring.");
                return;
            }


            long userId_msg = user.Id; // Renamed to avoid conflict with callback's userId if scopes were nested differently
            long chatId_msg = message.Chat.Id; // Renamed
            var session_msg = _userSessionService.GetUserSession(userId_msg); // Renamed

            Console.WriteLine($"Received '{messageText}' from User {userId_msg} in Chat {chatId_msg}. State: {session_msg.CurrentState}, WaitingForName: {session_msg.WaitingForNameInput}");

            if (session_msg.WaitingForNameInput)
            {
                if (messageText.StartsWith("/"))
                {
                    session.WaitingForNameInput = false;
                    session.CurrentState = UserCurrentState.MainMenu;
                    await botClient.SendTextMessageAsync(chatId, "Ввод имени отменен.", replyMarkup: MainCommandKeyboard, cancellationToken: cancellationToken);
                    if (messageText.ToLower() == "/setname") return; // Exit if it was /setname itself
                    // If it was another command, it will be processed below after this block
                }
                else
                {
                    session.Profile.Name = messageText.Trim();
                    session.WaitingForNameInput = false;
                    _userSessionService.PersistUpdatedProfile(userId);

                    await botClient.SendTextMessageAsync(
                        chatId,
                        $"Имя сохранено как *{session.Profile.Name}*.",
                        parseMode: ParseMode.Markdown,
                        replyMarkup: MainCommandKeyboard,
                        cancellationToken: cancellationToken);

                    session.CurrentState = UserCurrentState.MainMenu;
                    return;
                }
            }

            // Admin states for adding a lesson
            if (IsAdmin(userId))
            {
                switch (session.CurrentState)
                {
                    case UserCurrentState.AdminAddingLessonTitle:
                        session.PendingLesson = new Lesson { CreatedBy = userId, CreatedAt = DateTime.UtcNow };
                        session.PendingLesson.Title = messageText.Trim();
                        session.CurrentState = UserCurrentState.AdminAddingLessonDescription;
                        await botClient.SendTextMessageAsync(chatId, "Введите описание урока:", cancellationToken: cancellationToken);
                        return; // Input processed for this state

                    case UserCurrentState.AdminAddingLessonDescription:
                        if (session.PendingLesson == null) { /* Error: PendingLesson not initialized */ await GoToAdminRoot(botClient, session, chatId, "Ошибка: урок не инициализирован.", cancellationToken); return; }
                        session.PendingLesson.Description = messageText.Trim();
                        session.CurrentState = UserCurrentState.AdminAddingLessonContent;
                        await botClient.SendTextMessageAsync(chatId, "Введите содержание урока:", cancellationToken: cancellationToken);
                        return;

                    case UserCurrentState.AdminAddingLessonContent:
                        if (session.PendingLesson == null) { /* Error */ await GoToAdminRoot(botClient, session, chatId, "Ошибка: урок не инициализирован.", cancellationToken); return; }
                        session.PendingLesson.Content = messageText.Trim();
                        session.CurrentState = UserCurrentState.AdminAddingLessonLevel;
                        var levelKeyboard = new ReplyKeyboardMarkup(new[]
                        {
                            new[] { new KeyboardButton(LessonLevel.Beginner.ToString()), new KeyboardButton(LessonLevel.Intermediate.ToString()) },
                            new[] { new KeyboardButton(LessonLevel.Advanced.ToString()) }
                        }) { ResizeKeyboard = true, OneTimeKeyboard = true };
                        await botClient.SendTextMessageAsync(chatId, "Выберите уровень урока:", replyMarkup: levelKeyboard, cancellationToken: cancellationToken);
                        return;

                    case UserCurrentState.AdminAddingLessonLevel:
                        if (session.PendingLesson == null) { /* Error */ await GoToAdminRoot(botClient, session, chatId, "Ошибка: урок не инициализирован.", cancellationToken); return; }
                        if (Enum.TryParse<LessonLevel>(messageText, true, out var level))
                        {
                            session.PendingLesson.Level = level;
                            await _adminDataService.AddLesson(session.PendingLesson);
                            await botClient.SendTextMessageAsync(chatId, $"Урок \"{session.PendingLesson.Title}\" успешно добавлен.", cancellationToken: cancellationToken);
                            session.PendingLesson = null; // Clear pending lesson
                            await GoToAdminRoot(botClient, session, chatId, "Выберите следующее действие:", cancellationToken); // Go back to admin root
                        }
                        else
                        {
                            await botClient.SendTextMessageAsync(chatId, "Неверный уровень. Пожалуйста, выберите из предложенных вариантов.", cancellationToken: cancellationToken);
                            // Optionally resend the keyboard
                             var resendLevelKeyboard = new ReplyKeyboardMarkup(new[]
                            {
                                new[] { new KeyboardButton(LessonLevel.Beginner.ToString()), new KeyboardButton(LessonLevel.Intermediate.ToString()) },
                                new[] { new KeyboardButton(LessonLevel.Advanced.ToString()) }
                            }) { ResizeKeyboard = true, OneTimeKeyboard = true };
                            await botClient.SendTextMessageAsync(chatId, "Выберите уровень урока:", replyMarkup: resendLevelKeyboard, cancellationToken: cancellationToken);
                        }
                        return;
                    case UserCurrentState.AdminEditingLessonEnterNewValue:
                        if (string.IsNullOrEmpty(session_msg.EditingItemId) || string.IsNullOrEmpty(session_msg.EditingField))
                        {
                            await GoToAdminRoot(botClient, session_msg, chatId_msg, "Ошибка: не указан урок или поле для редактирования.", cancellationToken);
                            return;
                        }
                        var lessonToEdit = _adminDataService.GetLessonById(session_msg.EditingItemId);
                        if (lessonToEdit == null)
                        {
                            await GoToAdminRoot(botClient, session_msg, chatId_msg, "Ошибка: урок для редактирования не найден.", cancellationToken);
                            return;
                        }

                        bool edited = false;
                        switch (session_msg.EditingField.ToLower())
                        {
                            case "title":
                                lessonToEdit.Title = messageText.Trim();
                                edited = true;
                                break;
                            case "description":
                                lessonToEdit.Description = messageText.Trim();
                                edited = true;
                                break;
                            case "content":
                                lessonToEdit.Content = messageText.Trim();
                                edited = true;
                                break;
                            case "level":
                                if (Enum.TryParse<LessonLevel>(messageText, true, out var newLevel))
                                {
                                    lessonToEdit.Level = newLevel;
                                    edited = true;
                                }
                                else
                                {
                                    await botClient.SendTextMessageAsync(chatId_msg, "Неверный уровень. Пожалуйста, выберите из предложенных вариантов или введите корректное значение (Beginner, Intermediate, Advanced).", cancellationToken: cancellationToken);
                                    // Resend level keyboard
                                    var resendLevelKeyboard = new ReplyKeyboardMarkup(new[]
                                    {
                                        new[] { new KeyboardButton(LessonLevel.Beginner.ToString()), new KeyboardButton(LessonLevel.Intermediate.ToString()) },
                                        new[] { new KeyboardButton(LessonLevel.Advanced.ToString()) }
                                    }) { ResizeKeyboard = true, OneTimeKeyboard = true };
                                    await botClient.SendTextMessageAsync(chatId_msg, "Выберите или введите новый уровень:", replyMarkup: resendLevelKeyboard, cancellationToken: cancellationToken);
                                    return; // Keep state, wait for valid input
                                }
                                break;
                        }

                        if (edited)
                        {
                            await _adminDataService.UpdateLesson(lessonToEdit);
                            await botClient.SendTextMessageAsync(chatId_msg, $"Поле '{session_msg.EditingField}' урока \"{lessonToEdit.Title}\" успешно обновлено.", cancellationToken: cancellationToken);
                            await GoToAdminRoot(botClient, session_msg, chatId_msg, "Выберите следующее действие:", cancellationToken);
                        }
                        else if(session_msg.EditingField.ToLower() != "level") // Avoid double message if level was invalid
                        {
                            await botClient.SendTextMessageAsync(chatId_msg, "Не удалось распознать поле для редактирования.", cancellationToken: cancellationToken);
                            await GoToAdminRoot(botClient, session_msg, chatId_msg, "Выберите следующее действие:", cancellationToken);
                        }
                        return;

                    // States for adding a Test
                    case UserCurrentState.AdminAddingTestTitle:
                        if (session_msg.PendingTest == null) { /* Should have been initialized */ await GoToAdminRoot(botClient, session_msg, chatId_msg, "Ошибка: тест не инициализирован.", cancellationToken); return; }
                        session_msg.PendingTest.TestName = messageText.Trim();
                        session_msg.CurrentState = UserCurrentState.AdminAddingTestDescription;
                        await botClient.SendTextMessageAsync(chatId_msg, "Введите описание теста:", cancellationToken: cancellationToken);
                        return;

                    case UserCurrentState.AdminAddingTestDescription:
                        if (session_msg.PendingTest == null) { /* Error */ await GoToAdminRoot(botClient, session_msg, chatId_msg, "Ошибка: тест не инициализирован.", cancellationToken); return; }
                        session_msg.PendingTest.Description = messageText.Trim();
                        session_msg.CurrentState = UserCurrentState.AdminAddingTestLevel;
                        var testLevelKeyboard = new ReplyKeyboardMarkup(new[]
                        {
                            new[] { new KeyboardButton(TestDifficulty.Easy.ToString()), new KeyboardButton(TestDifficulty.Medium.ToString()) },
                            new[] { new KeyboardButton(TestDifficulty.Hard.ToString()) }
                        }) { ResizeKeyboard = true, OneTimeKeyboard = true };
                        await botClient.SendTextMessageAsync(chatId_msg, "Выберите сложность теста:", replyMarkup: testLevelKeyboard, cancellationToken: cancellationToken);
                        return;

                    case UserCurrentState.AdminAddingTestLevel:
                        if (session_msg.PendingTest == null) { /* Error */ await GoToAdminRoot(botClient, session_msg, chatId_msg, "Ошибка: тест не инициализирован.", cancellationToken); return; }
                        if (Enum.TryParse<TestDifficulty>(messageText, true, out var testDifficulty))
                        {
                            session_msg.PendingTest.Difficulty = testDifficulty;
                            session_msg.CurrentState = UserCurrentState.AdminAddingTestQuestionText;
                            session_msg.PendingQuestion = new QuestionData(); // Initialize for the first question
                            await botClient.SendTextMessageAsync(chatId_msg, "Тест создан. Теперь добавьте вопросы.\nВведите текст первого вопроса:", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
                        }
                        else
                        {
                            await botClient.SendTextMessageAsync(chatId_msg, "Неверная сложность. Пожалуйста, выберите из предложенных вариантов.", cancellationToken: cancellationToken);
                             var resendTestLevelKeyboard = new ReplyKeyboardMarkup(new[]
                            {
                                new[] { new KeyboardButton(TestDifficulty.Easy.ToString()), new KeyboardButton(TestDifficulty.Medium.ToString()) },
                                new[] { new KeyboardButton(TestDifficulty.Hard.ToString()) }
                            }) { ResizeKeyboard = true, OneTimeKeyboard = true };
                            await botClient.SendTextMessageAsync(chatId_msg, "Выберите сложность теста:", replyMarkup: resendTestLevelKeyboard, cancellationToken: cancellationToken);
                        }
                        return;

                    case UserCurrentState.AdminAddingTestQuestionText:
                        if (session_msg.PendingTest == null || session_msg.PendingQuestion == null) { /* Error */ await GoToAdminRoot(botClient, session_msg, chatId_msg, "Ошибка: не удалось добавить вопрос к тесту.", cancellationToken); return; }
                        session_msg.PendingQuestion.Text = messageText.Trim();
                        session_msg.CurrentState = UserCurrentState.AdminAddingTestQuestionOptions;
                        await botClient.SendTextMessageAsync(chatId_msg, "Введите варианты ответа через запятую (например: Ответ1,Ответ2,Ответ3):", cancellationToken: cancellationToken);
                        return;

                    case UserCurrentState.AdminAddingTestQuestionOptions:
                        if (session_msg.PendingTest == null || session_msg.PendingQuestion == null) { /* Error */ await GoToAdminRoot(botClient, session_msg, chatId_msg, "Ошибка: не удалось добавить варианты к вопросу.", cancellationToken); return; }
                        var options = messageText.Split(',').Select(o => o.Trim()).Where(o => !string.IsNullOrEmpty(o)).ToList();
                        if (options.Count < 2)
                        {
                            await botClient.SendTextMessageAsync(chatId_msg, "Нужно как минимум 2 варианта ответа. Попробуйте снова, через запятую:", cancellationToken: cancellationToken);
                            return; // Keep state, wait for valid input
                        }
                        session_msg.PendingQuestion.Options = options;
                        session_msg.CurrentState = UserCurrentState.AdminAddingTestQuestionCorrectOption;
                        // Send options as numbered list for easy selection of correct answer
                        var optionsText = string.Join("\n", options.Select((opt, idx) => $"{idx + 1}. {opt}"));
                        await botClient.SendTextMessageAsync(chatId_msg, $"Варианты:\n{optionsText}\n\nВведите номер правильного варианта (начиная с 1):", cancellationToken: cancellationToken);
                        return;

                    case UserCurrentState.AdminAddingTestQuestionCorrectOption:
                        if (session_msg.PendingTest == null || session_msg.PendingQuestion == null || session_msg.PendingQuestion.Options == null) { /* Error */ await GoToAdminRoot(botClient, session_msg, chatId_msg, "Ошибка: не удалось установить правильный ответ.", cancellationToken); return; }
                        if (int.TryParse(messageText, out int correctOptionNumber) && correctOptionNumber > 0 && correctOptionNumber <= session_msg.PendingQuestion.Options.Count)
                        {
                            session_msg.PendingQuestion.CorrectOptionIndex = correctOptionNumber - 1; // 0-indexed

                            if (!string.IsNullOrEmpty(session_msg.EditingItemId) && session_msg.PendingTest == null)
                            {
                                // This means we are adding a question to an existing test (session_msg.EditingItemId is the testId)
                                var testToUpdate = _adminDataService.GetTestById(session_msg.EditingItemId);
                                if (testToUpdate != null)
                                {
                                    testToUpdate.Questions.Add(session_msg.PendingQuestion);
                                    await _adminDataService.UpdateTest(testToUpdate);
                                    session_msg.PendingQuestion = null;
                                    // Refresh question list for this test
                                    await botClient.SendTextMessageAsync(chatId_msg, "Вопрос добавлен к текущему тесту.", cancellationToken: cancellationToken);
                                    // Simulate callback to refresh question list - this is a bit indirect.
                                    // A direct method call would be cleaner.
                                    // For now, this means the user will see the updated list next time they enter question editing for this test.
                                    // To immediately refresh, we'd need to reconstruct and send the qKeyboard.
                                    // Let's try to refresh directly:
                                    var qKeyboardRefresh = new List<IEnumerable<InlineKeyboardButton>>();
                                    qKeyboardRefresh.Add(new[] { InlineKeyboardButton.WithCallbackData("Добавить новый вопрос к этому тесту", $"admin_add_qst_to_test_{testToUpdate.Id}") });
                                    if (testToUpdate.Questions.Any())
                                    {
                                        for(int i=0; i < testToUpdate.Questions.Count; i++)
                                        {
                                            var q = testToUpdate.Questions[i];
                                            string qTextShort = q.Text.Length > 30 ? q.Text.Substring(0, 27) + "..." : q.Text;
                                            qKeyboardRefresh.Add(new[] {
                                                InlineKeyboardButton.WithCallbackData($"✏️ {i+1}. {qTextShort}", $"admin_edit_existing_qst_{testToUpdate.Id}_{i}"),
                                                InlineKeyboardButton.WithCallbackData($"🗑️ Удалить", $"admin_delete_existing_qst_{testToUpdate.Id}_{i}")
                                            });
                                        }
                                    }
                                    qKeyboardRefresh.Add(new[] { InlineKeyboardButton.WithCallbackData("<< Назад к редактированию теста", $"admin_edit_test_{testToUpdate.Id}") });
                                    session_msg.CurrentState = UserCurrentState.AdminEditingTestQuestionSelect; // Back to question selection state
                                    await botClient.SendTextMessageAsync(chatId_msg, $"Редактирование вопросов для теста: \"{testToUpdate.TestName}\"\nВсего вопросов: {testToUpdate.Questions.Count}",
                                        replyMarkup: new InlineKeyboardMarkup(qKeyboardRefresh), cancellationToken: cancellationToken);
                                }
                                else { /* Error, test not found */ }
                            }
                            else if (session_msg.PendingTest != null)
                            {
                                // This is the original "Add Test" flow
                                session_msg.PendingTest.Questions.Add(session_msg.PendingQuestion);
                                session_msg.PendingQuestion = null; // Clear for next question or finish
                                session_msg.CurrentState = UserCurrentState.AdminAddingTestQuestionAskMore;
                                var askMoreKeyboard = new ReplyKeyboardMarkup(new[]
                                {
                                    new KeyboardButton[] { "Да, добавить еще вопрос" },
                                    new KeyboardButton[] { "Нет, завершить добавление теста" }
                                }) { ResizeKeyboard = true, OneTimeKeyboard = true };
                                await botClient.SendTextMessageAsync(chatId_msg, "Вопрос добавлен. Хотите добавить еще один вопрос?", replyMarkup: askMoreKeyboard, cancellationToken: cancellationToken);
                            }
                            else
                            {
                                // Should not happen: PendingTest is null and not EditingItemId
                                await GoToAdminRoot(botClient, session_msg, chatId_msg, "Произошла ошибка при добавлении вопроса.", cancellationToken);
                            }
                        }
                        else
                        {
                            await botClient.SendTextMessageAsync(chatId_msg, $"Неверный номер. Введите число от 1 до {session_msg.PendingQuestion.Options.Count}.", cancellationToken: cancellationToken);
                        }
                        return;

                    case UserCurrentState.AdminAddingTestQuestionAskMore:
                        if (session_msg.PendingTest == null && string.IsNullOrEmpty(session_msg.EditingItemId))
                        {
                            // If PendingTest is null AND we are not editing an existing test's questions, then error.
                            // This state should only be reached if session_msg.PendingTest is not null (original add flow).
                            await GoToAdminRoot(botClient, session_msg, chatId_msg, "Ошибка: не удалось завершить тест.", cancellationToken); return;
                        }
                        // This state is primarily for the initial "Add Test" flow.
                        // If we were adding a question to an existing test, we would have already returned to question list.
                        if (session_msg.PendingTest != null)
                        {
                            if (messageText.ToLower() == "да, добавить еще вопрос")
                            {
                                session_msg.CurrentState = UserCurrentState.AdminAddingTestQuestionText;
                                session_msg.PendingQuestion = new QuestionData(); // Initialize for next question
                                await botClient.SendTextMessageAsync(chatId_msg, "Введите текст следующего вопроса:", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
                            }
                            else if (messageText.ToLower() == "нет, завершить добавление теста")
                            {
                                if (!session_msg.PendingTest.Questions.Any())
                                {
                                    await botClient.SendTextMessageAsync(chatId_msg, "Тест должен содержать хотя бы один вопрос. Пожалуйста, добавьте вопрос.", cancellationToken: cancellationToken);
                                    session_msg.CurrentState = UserCurrentState.AdminAddingTestQuestionText;
                                    session_msg.PendingQuestion = new QuestionData();
                                    await botClient.SendTextMessageAsync(chatId_msg, "Введите текст вопроса:", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
                                    return;
                                }
                                await _adminDataService.AddTest(session_msg.PendingTest);
                                await botClient.SendTextMessageAsync(chatId_msg, $"Тест \"{session_msg.PendingTest.TestName}\" успешно добавлен со всеми вопросами.", cancellationToken: cancellationToken);
                                session_msg.PendingTest = null;
                                await GoToAdminRoot(botClient, session_msg, chatId_msg, "Выберите следующее действие:", cancellationToken);
                            }
                            else
                            {
                                await botClient.SendTextMessageAsync(chatId_msg, "Пожалуйста, выберите 'Да' или 'Нет'.", cancellationToken: cancellationToken);
                                var resendAskMoreKeyboard = new ReplyKeyboardMarkup(new[]
                                {
                                    new KeyboardButton[] { "Да, добавить еще вопрос" },
                                    new KeyboardButton[] { "Нет, завершить добавление теста" }
                                }) { ResizeKeyboard = true, OneTimeKeyboard = true };
                                await botClient.SendTextMessageAsync(chatId_msg, "Хотите добавить еще один вопрос?", replyMarkup: resendAskMoreKeyboard, cancellationToken: cancellationToken);
                            }
                        }
                        else
                        {
                            // This case (EditingItemId is set, but we reached AskMore) should ideally not happen with the new logic.
                            // If it does, it implies a logic flaw. For safety, go to admin root.
                             await GoToAdminRoot(botClient, session_msg, chatId_msg, "Завершено добавление вопроса к тесту.", cancellationToken);
                        }
                        return;
                }
            }
                            {
                                new KeyboardButton[] { "Да, добавить еще вопрос" },
                                new KeyboardButton[] { "Нет, завершить добавление теста" }
                            }) { ResizeKeyboard = true, OneTimeKeyboard = true };
                            await botClient.SendTextMessageAsync(chatId_msg, "Вопрос добавлен. Хотите добавить еще один вопрос?", replyMarkup: askMoreKeyboard, cancellationToken: cancellationToken);
                        }
                        else
                        {
                            await botClient.SendTextMessageAsync(chatId_msg, $"Неверный номер. Введите число от 1 до {session_msg.PendingQuestion.Options.Count}.", cancellationToken: cancellationToken);
                        }
                        return;

                    case UserCurrentState.AdminAddingTestQuestionAskMore:
                        if (session_msg.PendingTest == null) { /* Error */ await GoToAdminRoot(botClient, session_msg, chatId_msg, "Ошибка: не удалось завершить тест.", cancellationToken); return; }
                        if (messageText.ToLower() == "да, добавить еще вопрос")
                        {
                            session_msg.CurrentState = UserCurrentState.AdminAddingTestQuestionText;
                            session_msg.PendingQuestion = new QuestionData(); // Initialize for next question
                            await botClient.SendTextMessageAsync(chatId_msg, "Введите текст следующего вопроса:", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
                        }
                        else if (messageText.ToLower() == "нет, завершить добавление теста")
                        {
                            if (!session_msg.PendingTest.Questions.Any())
                            {
                                await botClient.SendTextMessageAsync(chatId_msg, "Тест должен содержать хотя бы один вопрос. Пожалуйста, добавьте вопрос.", cancellationToken: cancellationToken);
                                // Go back to adding a question
                                session_msg.CurrentState = UserCurrentState.AdminAddingTestQuestionText;
                                session_msg.PendingQuestion = new QuestionData();
                                await botClient.SendTextMessageAsync(chatId_msg, "Введите текст вопроса:", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
                                return;
                            }
                            await _adminDataService.AddTest(session_msg.PendingTest);
                            await botClient.SendTextMessageAsync(chatId_msg, $"Тест \"{session_msg.PendingTest.TestName}\" успешно добавлен со всеми вопросами.", cancellationToken: cancellationToken);
                            session_msg.PendingTest = null;
                            await GoToAdminRoot(botClient, session_msg, chatId_msg, "Выберите следующее действие:", cancellationToken);
                        }
                        else
                        {
                            await botClient.SendTextMessageAsync(chatId_msg, "Пожалуйста, выберите 'Да' или 'Нет'.", cancellationToken: cancellationToken);
                            // Resend keyboard
                             var resendAskMoreKeyboard = new ReplyKeyboardMarkup(new[]
                            {
                                new KeyboardButton[] { "Да, добавить еще вопрос" },
                                new KeyboardButton[] { "Нет, завершить добавление теста" }
                            }) { ResizeKeyboard = true, OneTimeKeyboard = true };
                            await botClient.SendTextMessageAsync(chatId_msg, "Хотите добавить еще один вопрос?", replyMarkup: resendAskMoreKeyboard, cancellationToken: cancellationToken);
                        }
                        return;
                    case UserCurrentState.AdminEditingTestEnterNewValue: // Used for Test metadata
                        if (string.IsNullOrEmpty(session_msg.EditingItemId) || string.IsNullOrEmpty(session_msg.EditingField))
                        {
                            await GoToAdminRoot(botClient, session_msg, chatId_msg, "Ошибка: не указан тест или поле для редактирования.", cancellationToken);
                            return;
                        }
                        var testToEdit = _adminDataService.GetTestById(session_msg.EditingItemId);
                        if (testToEdit == null)
                        {
                            await GoToAdminRoot(botClient, session_msg, chatId_msg, "Ошибка: тест для редактирования не найден.", cancellationToken);
                            return;
                        }

                        bool testMetaEdited = false;
                        switch (session_msg.EditingField.ToLower())
                        {
                            case "title": // Actually TestName for TestData
                                testToEdit.TestName = messageText.Trim();
                                testMetaEdited = true;
                                break;
                            case "description":
                                testToEdit.Description = messageText.Trim();
                                testMetaEdited = true;
                                break;
                            case "difficulty":
                                if (Enum.TryParse<TestDifficulty>(messageText, true, out var newDifficulty))
                                {
                                    testToEdit.Difficulty = newDifficulty;
                                    testMetaEdited = true;
                                }
                                else
                                {
                                    await botClient.SendTextMessageAsync(chatId_msg, "Неверная сложность. Пожалуйста, выберите из предложенных вариантов или введите корректное значение (Easy, Medium, Hard).", cancellationToken: cancellationToken);
                                    var resendDifficultyKeyboard = new ReplyKeyboardMarkup(new[]
                                    {
                                        new[] { new KeyboardButton(TestDifficulty.Easy.ToString()), new KeyboardButton(TestDifficulty.Medium.ToString()) },
                                        new[] { new KeyboardButton(TestDifficulty.Hard.ToString()) }
                                    }) { ResizeKeyboard = true, OneTimeKeyboard = true };
                                    await botClient.SendTextMessageAsync(chatId_msg, "Выберите или введите новую сложность:", replyMarkup: resendDifficultyKeyboard, cancellationToken: cancellationToken);
                                    return; // Keep state, wait for valid input
                                }
                                break;
                        }

                        if (testMetaEdited)
                        {
                            await _adminDataService.UpdateTest(testToEdit);
                            // After editing a field, go back to the "Edit Test" menu for that specific test
                            session_msg.CurrentState = UserCurrentState.AdminEditingTestSelectField; // Go back to field selection for the same test
                            var editTestOptionsKeyboard = new InlineKeyboardMarkup(new[]
                            {
                                new[] { InlineKeyboardButton.WithCallbackData("Изменить Название", $"admin_edit_testmeta_title") },
                                new[] { InlineKeyboardButton.WithCallbackData("Изменить Описание", $"admin_edit_testmeta_description") },
                                new[] { InlineKeyboardButton.WithCallbackData("Изменить Сложность", $"admin_edit_testmeta_difficulty") },
                                new[] { InlineKeyboardButton.WithCallbackData("Редактировать Вопросы", $"admin_edit_testquestions_{testToEdit.Id}") },
                                new[] { InlineKeyboardButton.WithCallbackData("<< Назад к выбору теста", $"admin_back_to_test_select_for_edit") }
                            });
                             await botClient.SendTextMessageAsync(chatId_msg, $"Поле '{session_msg.EditingField}' теста \"{testToEdit.TestName}\" успешно обновлено.\nЧто еще хотите изменить в этом тесте?", replyMarkup: editTestOptionsKeyboard, cancellationToken: cancellationToken);
                        }
                        else if(session_msg.EditingField.ToLower() != "difficulty")
                        {
                            await botClient.SendTextMessageAsync(chatId_msg, "Не удалось распознать поле для редактирования.", cancellationToken: cancellationToken);
                            await GoToAdminRoot(botClient, session_msg, chatId_msg, "Выберите следующее действие:", cancellationToken);
                        }
                        // Clear EditingField and potentially EditingItemId if going back to AdminRoot, but here we go back to test's edit menu
                        session_msg.EditingField = null;
                        return;

                    case UserCurrentState.AdminEditingTestQuestionEnterNewValue:
                        if (string.IsNullOrEmpty(session_msg.EditingItemId) || session_msg.EditingQuestionIndex == null || string.IsNullOrEmpty(session_msg.EditingField))
                        {
                            await GoToAdminRoot(botClient, session_msg, chatId_msg, "Ошибка: не указан тест, вопрос или поле для редактирования.", cancellationToken);
                            return;
                        }
                        var testForQuestionEdit = _adminDataService.GetTestById(session_msg.EditingItemId);
                        if (testForQuestionEdit == null || session_msg.EditingQuestionIndex >= testForQuestionEdit.Questions.Count)
                        {
                            await GoToAdminRoot(botClient, session_msg, chatId_msg, "Ошибка: тест или вопрос для редактирования не найден.", cancellationToken);
                            return;
                        }
                        var questionBeingEdited = testForQuestionEdit.Questions[session_msg.EditingQuestionIndex.Value];
                        bool questionEdited = false;

                        switch (session_msg.EditingField)
                        {
                            case "question_text":
                                questionBeingEdited.Text = messageText.Trim();
                                questionEdited = true;
                                break;
                            case "question_options":
                                var newOptions = messageText.Split(',').Select(o => o.Trim()).Where(o => !string.IsNullOrEmpty(o)).ToList();
                                if (newOptions.Count < 2)
                                {
                                    await botClient.SendTextMessageAsync(chatId_msg, "Нужно как минимум 2 варианта ответа. Попробуйте снова, через запятую:", cancellationToken: cancellationToken);
                                    return; // Keep state, wait for valid input
                                }
                                questionBeingEdited.Options = newOptions;
                                // Crucially, if options change, the CorrectOptionIndex might become invalid or point to a different answer.
                                // Best practice: Reset CorrectOptionIndex and ask for it again, or ensure admin is aware.
                                // For now, let's just update options. Admin must be careful. A better UI would handle this.
                                // A simple fix: if current CorrectOptionIndex is now out of bounds, reset to 0.
                                if (questionBeingEdited.CorrectOptionIndex >= newOptions.Count) {
                                    questionBeingEdited.CorrectOptionIndex = 0; // Default to first option
                                    await botClient.SendTextMessageAsync(chatId_msg, "Внимание: Индекс правильного ответа был сброшен из-за изменения опций. Пожалуйста, проверьте и исправьте его отдельно.", cancellationToken: cancellationToken);
                                }
                                questionEdited = true;
                                break;
                            case "question_correct":
                                if (int.TryParse(messageText, out int newCorrectIdxNumber) && newCorrectIdxNumber > 0 && newCorrectIdxNumber <= questionBeingEdited.Options.Count)
                                {
                                    questionBeingEdited.CorrectOptionIndex = newCorrectIdxNumber - 1;
                                    questionEdited = true;
                                }
                                else
                                {
                                    var q = questionBeingEdited;
                                    var optionsText = string.Join("\n", q.Options.Select((opt, idx) => $"{idx + 1}. {opt}"));
                                    await botClient.SendTextMessageAsync(chatId_msg, $"Неверный номер. Введите число от 1 до {questionBeingEdited.Options.Count}.\nТекущие варианты:\n{optionsText}", cancellationToken: cancellationToken);
                                    return; // Keep state
                                }
                                break;
                        }

                        if (questionEdited)
                        {
                            await _adminDataService.UpdateTest(testForQuestionEdit);
                            await botClient.SendTextMessageAsync(chatId_msg, $"Вопрос {session_msg.EditingQuestionIndex.Value + 1} успешно обновлен.", cancellationToken: cancellationToken);
                            // Go back to question list for this test
                            // This requires reconstructing the keyboard or having a helper
                            var qKeyboardRefresh = new List<IEnumerable<InlineKeyboardButton>>();
                            qKeyboardRefresh.Add(new[] { InlineKeyboardButton.WithCallbackData("Добавить новый вопрос к этому тесту", $"admin_add_qst_to_test_{testForQuestionEdit.Id}") });
                            if (testForQuestionEdit.Questions.Any())
                            {
                                for(int i=0; i < testForQuestionEdit.Questions.Count; i++)
                                {
                                    var q = testForQuestionEdit.Questions[i];
                                    string qTextShort = q.Text.Length > 30 ? q.Text.Substring(0, 27) + "..." : q.Text;
                                    qKeyboardRefresh.Add(new[] {
                                        InlineKeyboardButton.WithCallbackData($"✏️ {i+1}. {qTextShort}", $"admin_edit_existing_qst_{testForQuestionEdit.Id}_{i}"),
                                        InlineKeyboardButton.WithCallbackData($"🗑️ Удалить", $"admin_delete_existing_qst_{testForQuestionEdit.Id}_{i}")
                                    });
                                }
                            }
                            qKeyboardRefresh.Add(new[] { InlineKeyboardButton.WithCallbackData("<< Назад к редактированию теста", $"admin_edit_test_{testForQuestionEdit.Id}") });
                            session_msg.CurrentState = UserCurrentState.AdminEditingTestQuestionSelect;
                            session_msg.EditingField = null;
                            session_msg.EditingQuestionIndex = null;
                             await botClient.SendTextMessageAsync(chatId_msg, $"Редактирование вопросов для теста: \"{testForQuestionEdit.TestName}\"\nВсего вопросов: {testForQuestionEdit.Questions.Count}",
                                replyMarkup: new InlineKeyboardMarkup(qKeyboardRefresh), cancellationToken: cancellationToken);
                        }
                        return;

                    // States for adding a Test
                    case UserCurrentState.AdminAddingTestTitle:
                        if (session_msg.PendingTest == null) { /* Should have been initialized */ await GoToAdminRoot(botClient, session_msg, chatId_msg, "Ошибка: тест не инициализирован.", cancellationToken); return; }
                        session_msg.PendingTest.TestName = messageText.Trim();
                if (!session.ActiveTestId.HasValue || !activeTestsData.TryGetValue(session.ActiveTestId.Value, out var currentTestData) ||
                    session.CurrentQuestionIndex >= currentTestData.Questions.Count)
                {
                    await botClient.SendTextMessageAsync(chatId, "Произошла ошибка с текущим тестом. Возвращаемся в главное меню.", replyMarkup: MainCommandKeyboard, cancellationToken: cancellationToken);
                    session.EndCurrentTest();
                    return;
                }

                QuestionData currentQuestion = currentTestData.Questions[session.CurrentQuestionIndex];
                int selectedOptionIdx = currentQuestion.Options.IndexOf(messageText);

                if (selectedOptionIdx != -1)
                {
                    if (selectedOptionIdx == currentQuestion.CorrectOptionIndex) session.CurrentTestScore++;
                    session.CurrentQuestionIndex++;

                    if (session.CurrentQuestionIndex < currentTestData.Questions.Count)
                    {
                        await DisplayCurrentTestQuestionAsync(botClient, session, chatId, cancellationToken);
                    }
                    else
                    {
                        if (session.ActiveTestId.HasValue)
                        {
                            var historyEntry = new TestHistoryEntry
                            {
                                TestId = session.ActiveTestId.Value, TestTitle = currentTestData.TestName,
                                PassedAt = DateTime.UtcNow, TotalQuestions = currentTestData.Questions.Count,
                                CorrectAnswers = session.CurrentTestScore
                            };
                            session.TestHistory.Add(historyEntry);
                            Console.WriteLine($"Saved test history for user {userId}, test {historyEntry.TestTitle}");
                        }
                        _userSessionService.UpdateProgress(userId, session.CurrentTestScore);
                        string resultMessage = $"Тест \"{currentTestData.TestName}\" завершён.\nВаш результат: {session.CurrentTestScore} из {currentTestData.Questions.Count}.";
                        await botClient.SendTextMessageAsync(chatId, resultMessage, replyMarkup: AfterTestMenuKeyboard, cancellationToken: cancellationToken);
                        session.EndCurrentTest();
                    }
                }
                else if (messageText.ToLower() == "/stoptest")
                {
                    await HandleStopTestCommandAsync(botClient, session, chatId, cancellationToken);
                }
                else
                {
                    await botClient.SendTextMessageAsync(chatId, "Пожалуйста, выберите один из предложенных вариантов.", cancellationToken: cancellationToken);
                    await DisplayCurrentTestQuestionAsync(botClient, session, chatId, cancellationToken);
                }
                return;
            }

            // Flashcard handling (when in ReviewingFlashcards state)
            // Place these inside the Program class

            static List<Flashcard> GetFlashcardsByLevel(int userProfileLevel, IEnumerable<Flashcard> allFlashcards)
            {
                if (userProfileLevel < 3)
                    return allFlashcards.Where(f => f.Level == LessonLevel.Beginner).ToList();
                else if (userProfileLevel < 6)
                    return allFlashcards.Where(f => f.Level == LessonLevel.Beginner || f.Level == LessonLevel.Intermediate).ToList();
                else
                    return allFlashcards.ToList();
            }

            static async Task ShowNextFlashcardAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
            {
                if (session.FlashcardQueue == null || session.FlashcardQueue.Count == 0)
                {
                    await botClient.SendTextMessageAsync(chatId, "✅ Все карточки просмотрены!", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                    session.EndFlashcardSession();
                    return;
                }

                var card = session.FlashcardQueue.Dequeue();
                session.CurrentFlashcard = card;

                await botClient.SendTextMessageAsync(
                    chatId,
                    $"❓ {card.Question}",
                    replyMarkup: FlashcardQuestionKeyboard,
                    cancellationToken: ct
                );
                session.CurrentState = UserCurrentState.ReviewingFlashcards;
            }

            static async Task StartFlashcardSessionAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
            {
                if (session.CurrentState == UserCurrentState.TakingTest && session.ActiveTestId.HasValue)
                {
                    await botClient.SendTextMessageAsync(chatId, "Пожалуйста, завершите или остановите текущий тест (команда /stoptest), прежде чем начинать флеш-карточки.", cancellationToken: ct);
                    await DisplayCurrentTestQuestionAsync(botClient, session, chatId, ct);
                    return;
                }

                var flashcardsForUser = GetFlashcardsByLevel(session.Profile.Level, allFlashcardsData);

                if (flashcardsForUser == null || !flashcardsForUser.Any())
                {
                    await botClient.SendTextMessageAsync(chatId, "Флеш-карточки для вашего уровня пока не добавлены.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                    session.CurrentState = UserCurrentState.MainMenu;
                    return;
                }

                var random = new Random();
                session.FlashcardQueue = new Queue<Flashcard>(flashcardsForUser.OrderBy(x => random.Next()));
                session.CurrentFlashcard = null;

                await botClient.SendTextMessageAsync(chatId, "Начинаем сессию флеш-карточек!", cancellationToken: ct, replyMarkup: new ReplyKeyboardRemove());
                await ShowNextFlashcardAsync(botClient, session, chatId, ct);
            }
            if (session.CurrentState == UserCurrentState.ReviewingFlashcards)
            {
                bool flashcardActionProcessed = true;
                switch (messageText)
                {
                    case "Показать ответ":
                        if (session.CurrentFlashcard != null)
                        {
                            await botClient.SendTextMessageAsync(chatId, $"💡 Ответ: {session.CurrentFlashcard.Answer}", replyMarkup: FlashcardQuestionKeyboard, cancellationToken: cancellationToken);
                        }
                        else
                        {
                            await botClient.SendTextMessageAsync(chatId, "Ошибка: Текущая карточка не найдена.", replyMarkup: FlashcardQuestionKeyboard, cancellationToken: cancellationToken);
                        }
                        break;
                    case "Следующая карточка":
                        await ShowNextFlashcardAsync(botClient, session, chatId, cancellationToken);
                        break;
                    case "↩ Меню":
                        session.EndFlashcardSession(); // Clears flashcard state and sets CurrentState to MainMenu
                        await HandleStartCommandAsync(botClient, session, chatId, cancellationToken); // Shows main menu and keyboard
                        break;
                    default:
                        flashcardActionProcessed = false; // Not a flashcard action button
                        break;
                }
                if (flashcardActionProcessed) return; // Input was handled as a flashcard action
            }


            if (session.IsAuthenticated)
            {
                bool keyboardButtonProcessed = true;
                switch (messageText)
                {
                    // Admin panel exit
                    case "↩️ Выйти из админ панели":
                        if (IsAdmin(userId) && session.CurrentState.ToString().StartsWith("Admin")) // Check if current state is an admin state
                        {
                            session.CurrentState = UserCurrentState.MainMenu;
                            await botClient.SendTextMessageAsync(chatId, "Вы вышли из админ-панели.", replyMarkup: MainCommandKeyboard, cancellationToken: cancellationToken);
                        }
                        else
                        {
                            // If not in admin state or not admin, treat as unhandled or fall through
                            keyboardButtonProcessed = false;
                        }
                        break;
                    // Admin Panel Buttons
                    case "➕ Добавить урок":
                        if (IsAdmin(userId) && session.CurrentState == UserCurrentState.AdminRoot)
                        {
                            session.CurrentState = UserCurrentState.AdminAddingLessonTitle;
                            await botClient.SendTextMessageAsync(chatId, "Введите название нового урока:", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
                        }
                        else keyboardButtonProcessed = false;
                        break;
                    case "✏️ Редактировать урок":
                        if (IsAdmin(userId) && session.CurrentState == UserCurrentState.AdminRoot)
                        {
                            var allLessons = _adminDataService.GetAllLessons();
                            if (!allLessons.Any())
                            {
                                await botClient.SendTextMessageAsync(chatId, "Пока нет уроков для редактирования.", cancellationToken: cancellationToken);
                                await GoToAdminRoot(botClient, session, chatId, "Выберите действие:", cancellationToken);
                                break;
                            }

                            var inlineKeyboardButtons = allLessons.Select(lesson =>
                                InlineKeyboardButton.WithCallbackData($"{lesson.Title} (ID: {lesson.Id.Substring(0, 8)}...)", $"admin_edit_lesson_{lesson.Id}")
                            ).ToList();

                            // Arrange buttons in rows, e.g., 2 per row
                            var rows = new List<IEnumerable<InlineKeyboardButton>>();
                            for (int i = 0; i < inlineKeyboardButtons.Count; i += 2)
                            {
                                rows.Add(inlineKeyboardButtons.Skip(i).Take(2));
                            }
                            var inlineKeyboard = new InlineKeyboardMarkup(rows);

                            session.CurrentState = UserCurrentState.AdminEditingLessonSelect;
                            await botClient.SendTextMessageAsync(chatId, "Выберите урок для редактирования:", replyMarkup: inlineKeyboard, cancellationToken: cancellationToken);
                        }
                        else keyboardButtonProcessed = false;
                        break;
                    case "🗑 Удалить урок":
                        if (IsAdmin(userId) && session.CurrentState == UserCurrentState.AdminRoot)
                        {
                            var allLessons = _adminDataService.GetAllLessons();
                            if (!allLessons.Any())
                            {
                                await botClient.SendTextMessageAsync(chatId, "Пока нет уроков для удаления.", cancellationToken: cancellationToken);
                                await GoToAdminRoot(botClient, session, chatId, "Выберите действие:", cancellationToken);
                                break;
                            }

                            var inlineKeyboardButtons = allLessons.Select(lesson =>
                                InlineKeyboardButton.WithCallbackData($"{lesson.Title} (ID: {lesson.Id.Substring(0, 8)}...)", $"admin_delete_lesson_{lesson.Id}")
                            ).ToList();

                            var rows = new List<IEnumerable<InlineKeyboardButton>>();
                            for (int i = 0; i < inlineKeyboardButtons.Count; i += 2)
                            {
                                rows.Add(inlineKeyboardButtons.Skip(i).Take(2));
                            }
                            var inlineKeyboard = new InlineKeyboardMarkup(rows);

                            session.CurrentState = UserCurrentState.AdminDeletingLessonSelect;
                            await botClient.SendTextMessageAsync(chatId, "Выберите урок для удаления:", replyMarkup: inlineKeyboard, cancellationToken: cancellationToken);
                        }
                        else keyboardButtonProcessed = false;
                        break;
                    case "➕ Добавить тест":
                        if (IsAdmin(userId) && session.CurrentState == UserCurrentState.AdminRoot)
                        {
                            session.CurrentState = UserCurrentState.AdminAddingTestTitle;
                            // Initialize PendingTest here or when first piece of data (e.g., title) is received
                            session.PendingTest = new TestData { CreatedBy = userId, CreatedAt = DateTime.UtcNow, Questions = new List<QuestionData>() };
                            await botClient.SendTextMessageAsync(chatId, "Введите название нового теста:", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
                        }
                        else keyboardButtonProcessed = false;
                        break;
                    case "✏️ Редактировать тест":
                        if (IsAdmin(userId) && session.CurrentState == UserCurrentState.AdminRoot)
                        {
                            var allTests = _adminDataService.GetAllTests();
                            if (!allTests.Any())
                            {
                                await botClient.SendTextMessageAsync(chatId, "Пока нет тестов для редактирования.", cancellationToken: cancellationToken);
                                await GoToAdminRoot(botClient, session, chatId, "Выберите действие:", cancellationToken);
                                break;
                            }

                            var inlineKeyboardButtons = allTests.Select(test =>
                                InlineKeyboardButton.WithCallbackData($"{test.TestName} (ID: {test.Id.Substring(0, 8)}...)", $"admin_edit_test_{test.Id}")
                            ).ToList();

                            var rows = new List<IEnumerable<InlineKeyboardButton>>();
                            for (int i = 0; i < inlineKeyboardButtons.Count; i += 2)
                            {
                                rows.Add(inlineKeyboardButtons.Skip(i).Take(2));
                            }
                            var inlineKeyboard = new InlineKeyboardMarkup(rows);

                            session.CurrentState = UserCurrentState.AdminEditingTestSelect;
                            await botClient.SendTextMessageAsync(chatId, "Выберите тест для редактирования:", replyMarkup: inlineKeyboard, cancellationToken: cancellationToken);
                        }
                        else keyboardButtonProcessed = false;
                        break;
                    case "🗑 Удалить тест":
                        if (IsAdmin(userId) && session.CurrentState == UserCurrentState.AdminRoot)
                        {
                            var allTests = _adminDataService.GetAllTests();
                            if (!allTests.Any())
                            {
                                await botClient.SendTextMessageAsync(chatId, "Пока нет тестов для удаления.", cancellationToken: cancellationToken);
                                await GoToAdminRoot(botClient, session, chatId, "Выберите действие:", cancellationToken);
                                break;
                            }

                            var inlineKeyboardButtons = allTests.Select(test =>
                                InlineKeyboardButton.WithCallbackData($"{test.TestName} (ID: {test.Id.Substring(0, 8)}...)", $"admin_delete_test_{test.Id}")
                            ).ToList();

                            var rows = new List<IEnumerable<InlineKeyboardButton>>();
                            for (int i = 0; i < inlineKeyboardButtons.Count; i += 2)
                            {
                                rows.Add(inlineKeyboardButtons.Skip(i).Take(2));
                            }
                            var inlineKeyboard = new InlineKeyboardMarkup(rows);

                            session.CurrentState = UserCurrentState.AdminDeletingTestSelect;
                            await botClient.SendTextMessageAsync(chatId, "Выберите тест для удаления:", replyMarkup: inlineKeyboard, cancellationToken: cancellationToken);
                        }
                        else keyboardButtonProcessed = false;
                        break;
                    // End Admin Panel Buttons
                    case "📘 Уроки": await HandleLessonsListAsync(botClient, session, chatId, cancellationToken); break;
                    case "🧪 Тесты": await HandleTestsListAsync(botClient, session, chatId, cancellationToken); break;
                    case "🧠 Флеш-карточки": // New button for starting flashcards
                        await StartFlashcardSessionAsync(botClient, session, chatId, cancellationToken);
                        break;
                    case "История": await HandleHistoryAsync(botClient, session, chatId, cancellationToken); break;
                    case "👤 Профиль": await HandleProfileAsync(botClient, session, chatId, cancellationToken); break;
                    case "🏆 Топ": // Handle "Топ" button
                        await ShowLeaderboardAsync(botClient, session, chatId, cancellationToken);
                        break;
                    case "Назад":
                        if (session.CurrentState == UserCurrentState.ViewingTestDetail) await HandleTestsListAsync(botClient, session, chatId, cancellationToken);
                        else
                        {
                            await botClient.SendTextMessageAsync(chatId, "Главное меню.", replyMarkup: MainCommandKeyboard, cancellationToken: cancellationToken);
                            session.CurrentState = UserCurrentState.MainMenu;
                        }
                        break;
                    case "Назад к списку уроков":
                         await HandleLessonsListAsync(botClient, session, chatId, cancellationToken);
                         break;
                    case "Начать тест":
                        if (session.CurrentState == UserCurrentState.ViewingTestDetail && session.ViewingItemId.HasValue) await StartActualTestAsync(botClient, session, chatId, session.ViewingItemId.Value, cancellationToken);
                        else if (session.CurrentState == UserCurrentState.ViewingTestDetail && !session.ViewingItemId.HasValue) await botClient.SendTextMessageAsync(chatId, "Ошибка: не удалось определить, какой тест запустить.", replyMarkup: TestDetailKeyboard, cancellationToken: cancellationToken);
                        else await botClient.SendTextMessageAsync(chatId, "Пожалуйста, сначала выберите тест из списка.", replyMarkup: MainCommandKeyboard, cancellationToken: cancellationToken);
                        break;
                    case "Вернуться в меню":
                        await HandleStartCommandAsync(botClient, session, chatId, cancellationToken);
                        session.CurrentState = UserCurrentState.MainMenu;
                        break;
                    case "🔐 Выйти": await HandleLogoutCommandAsync(botClient, session, chatId, cancellationToken); break;
                    default: keyboardButtonProcessed = false; break;
                }
                if (keyboardButtonProcessed) return;
            }

            if (session.IsAuthenticated)
            {
                bool inputHandled = false;
                if (int.TryParse(messageText, out int selectionNumber) && selectionNumber > 0)
                {
                     if (session.CurrentState == UserCurrentState.ViewingLessonList)
                    {
                        if (selectionNumber > 0 && selectionNumber <= allLessonsData.Count)
                        {
                             await HandleLessonContentAsync(botClient, session, chatId, allLessonsData[selectionNumber-1], cancellationToken);
                             inputHandled = true;
                        } else {
                            await botClient.SendTextMessageAsync(chatId, "Неверный номер урока.", replyMarkup: MainCommandKeyboard, cancellationToken: cancellationToken);
                            inputHandled = true;
                        }
                    }
                    else if (session.CurrentState == UserCurrentState.ViewingTestList)
                    {
                        if (session.LastShownTestList != null && selectionNumber <= session.LastShownTestList.Count)
                        {
                            TestData selectedTest = session.LastShownTestList[selectionNumber - 1];
                            await HandleTestDetailAsync(botClient, session, chatId, selectedTest.TestId, cancellationToken);
                        }
                        else
                        {
                            await botClient.SendTextMessageAsync(chatId, "Неверный номер теста. Пожалуйста, выберите из списка.", replyMarkup: MainCommandKeyboard, cancellationToken: cancellationToken);
                        }
                        inputHandled = true;
                    }
                }
                else
                {
                    if (session.CurrentState == UserCurrentState.ViewingLessonList && session.LastShownLessonTitles != null &&
                        session.LastShownLessonTitles.Any(title => title.Equals(messageText.Trim(), StringComparison.OrdinalIgnoreCase)))
                    {
                        var lessonToView = allLessonsData.FirstOrDefault(l => l.Title.Equals(messageText.Trim(), StringComparison.OrdinalIgnoreCase));
                        if (lessonToView != null)
                        {
                            await HandleLessonContentAsync(botClient, session, chatId, lessonToView, cancellationToken);
                            inputHandled = true;
                        }
                    }
                }
                if (inputHandled) return;
            }

            var parts = messageText.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            var command = parts[0].ToLower();
            var argument = parts.Length > 1 ? parts[1] : null;

            if (!session.IsAuthenticated && command != "/login" && command != "/start" && command != "/help")
            {
                await botClient.SendTextMessageAsync(chatId, "You are not authenticated. Please use /login <password> to authenticate.", cancellationToken: cancellationToken);
                return;
            }

            try
            {
                switch (command)
                {
                    case "/login": await HandleLoginCommandAsync(botClient, session, chatId, argument, cancellationToken); break;
                    case "/logout": await HandleLogoutCommandAsync(botClient, session, chatId, cancellationToken); break;
                    case "/start": await HandleStartCommandAsync(botClient, session, chatId, cancellationToken); break;
                    case "/help": await HandleHelpCommandAsync(botClient, session, chatId, cancellationToken); break;
                    case "/courses": await HandleCoursesCommandAsync(botClient, session, chatId, cancellationToken); break;
                    case "/lesson": await HandleLessonCommandAsync(botClient, session, chatId, argument, cancellationToken); break;
                    case "/test": await HandleTestCommandAsync(botClient, session, chatId, argument, cancellationToken); break;
                    case "/stoptest": await HandleStopTestCommandAsync(botClient, session, chatId, cancellationToken); break;
                    case "/setname":
                        if (!session.IsAuthenticated) {
                            await botClient.SendTextMessageAsync(chatId, "Пожалуйста, сначала авторизуйтесь.", cancellationToken: cancellationToken);
                            break;
                        }
                        if (session.CurrentState == UserCurrentState.TakingTest && session.ActiveTestId.HasValue) {
                             await botClient.SendTextMessageAsync(chatId, "Нельзя менять имя во время прохождения теста. Завершите или остановите тест (/stoptest).", cancellationToken: cancellationToken);
                             await DisplayCurrentTestQuestionAsync(botClient, session, chatId, cancellationToken);
                             break;
                        }
                        await botClient.SendTextMessageAsync(chatId, "Введите ваше имя:", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
                        session.WaitingForNameInput = true;
                        session.CurrentState = UserCurrentState.WaitingForNameInput;
                        break;
                    case "/profile": await HandleProfileAsync(botClient, session, chatId, cancellationToken); break;
                    case "/history": await HandleHistoryAsync(botClient, session, chatId, cancellationToken); break;
                    case "/leaderboard": // Handle /leaderboard command
                    case "/top":         // Alias /top
                        await ShowLeaderboardAsync(botClient, session, chatId, cancellationToken);
                        break;
                    case "/export":
                        if (IsAdmin(userId))
                        {
                            await ExportDataAsync();
                            await botClient.SendTextMessageAsync(chatId, "📤 Данные успешно экспортированы.", cancellationToken: cancellationToken);
                        }
                        else
                        {
                            await botClient.SendTextMessageAsync(chatId, "Эта команда доступна только администратору.", cancellationToken: cancellationToken);
                        }
                        break;
                    case "/import":
                        if (IsAdmin(userId))
                        {
                            await ImportDataAsync();
                            await botClient.SendTextMessageAsync(chatId, "📥 Данные успешно импортированы.", cancellationToken: cancellationToken);
                        }
                        else
                        {
                            await botClient.SendTextMessageAsync(chatId, "Эта команда доступна только администратору.", cancellationToken: cancellationToken);
                        }
                        break;
                    case "/admin": // New admin panel command
                        if (IsAdmin(userId))
                        {
                            var adminKeyboard = new ReplyKeyboardMarkup(new[]
                            {
                                new[] { new KeyboardButton("➕ Добавить урок"), new KeyboardButton("➕ Добавить тест") },
                                new[] { new KeyboardButton("✏️ Редактировать урок"), new KeyboardButton("✏️ Редактировать тест") },
                                new[] { new KeyboardButton("🗑 Удалить урок"), new KeyboardButton("🗑 Удалить тест") },
                                new[] { new KeyboardButton("↩️ Выйти из админ панели") } // Added an exit button
                            })
                            {
                                ResizeKeyboard = true
                            };
                            session.CurrentState = UserCurrentState.AdminRoot;
                            await botClient.SendTextMessageAsync(chatId, "Добро пожаловать в админ-панель.\nВыберите действие:", replyMarkup: adminKeyboard, cancellationToken: cancellationToken);
                        }
                        else
                        {
                            await botClient.SendTextMessageAsync(chatId, "У вас нет прав для доступа к админ-панели.", cancellationToken: cancellationToken);
                        }
                        break;
                    default:
                        await botClient.SendTextMessageAsync(chatId, $"Unknown command '{command}'. Try /help for commands.", cancellationToken: cancellationToken);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing command '{command}' for user {userId}: {ex}");
                await botClient.SendTextMessageAsync(chatId, "An error occurred while processing your request.", cancellationToken: cancellationToken);
            }
        }

        static Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var ErrorMessage = exception switch
            {
                ApiRequestException apiRequestException => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => exception.ToString()
            };
            Console.WriteLine(ErrorMessage);
            return Task.CompletedTask;
        }

        private static async Task ExportDataAsync()
        {
            const string backupDir = "backup";
            const string exportFileName = "omnieye_export.json";
            string filePath = Path.Combine(backupDir, exportFileName);

            try
            {
                // Ensure backup directory exists
                if (!Directory.Exists(backupDir))
                {
                    Directory.CreateDirectory(backupDir);
                    Console.WriteLine($"Created directory: {Path.GetFullPath(backupDir)}");
                }

                var users = _userSessionService.GetAllUserProfiles() ?? new List<UserProfile>();
                var lessons = allLessonsData ?? new List<Lesson>(); // Using the static list from Program.cs

                var tests = new List<Omnieye.Bot.Models.Test>();
                var loadedTest = _testLoaderService.LoadTest(); // TestLoaderService loads one Test structure
                if (loadedTest != null)
                {
                    tests.Add(loadedTest);
                }

                var data = new BotData
                {
                    Users = users,
                    Lessons = lessons,
                    Tests = tests
                };

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(data, options);
                await IOFile.WriteAllTextAsync(filePath, json);
                Console.WriteLine($"Data successfully exported to {Path.GetFullPath(filePath)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during data export: {ex.Message}");
                // Optionally, notify admin or log to a more persistent error log
            }
        }

        // Placeholder for admin check. Replace with actual admin logic.
        // For example, check against a configuration file or a list of admin IDs.
        private static bool IsAdmin(long userId)
        {
            // Admin User ID
            long adminUserId = 907191168;
            return userId == adminUserId;
        }

        private static async Task PerformAutoBackupAsync()
        {
            const string backupDir = "backup";
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            string filename = $"auto_backup_{timestamp}.json";
            string filePath = Path.Combine(backupDir, filename);

            try
            {
                if (!Directory.Exists(backupDir))
                {
                    Directory.CreateDirectory(backupDir);
                    Console.WriteLine($"Created directory for auto-backup: {Path.GetFullPath(backupDir)}");
                }

                var users = _userSessionService.GetAllUserProfiles() ?? new List<UserProfile>();
                var lessons = allLessonsData ?? new List<Lesson>();

                var tests = new List<Omnieye.Bot.Models.Test>();
                var loadedTest = _testLoaderService.LoadTest();
                if (loadedTest != null)
                {
                    tests.Add(loadedTest);
                }

                var data = new BotData
                {
                    Users = users,
                    Lessons = lessons,
                    Tests = tests
                };

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(data, options);
                await IOFile.WriteAllTextAsync(filePath, json);
                Console.WriteLine($"Auto backup successful: Data saved to {Path.GetFullPath(filePath)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during auto backup to {filePath}: {ex.Message}");
            }
        }

        private static void StartAutoBackup()
        {
            // Timer interval in milliseconds. 30 minutes = 30 * 60 * 1000 ms
            double interval = 30 * 60 * 1000;
            var timer = new System.Timers.Timer(interval);

            timer.Elapsed += async (sender, e) => await PerformAutoBackupAsync();
            timer.AutoReset = true; // Makes the timer raise the Elapsed event repeatedly
            timer.Enabled = true;   // Starts the timer

            Console.WriteLine($"Auto-backup service started. Backups will be performed every {interval / (60 * 1000)} minutes.");
        }

        private static async Task ImportDataAsync()
        {
            const string backupDir = "backup";
            const string importFileName = "omnieye_export.json";
            string filePath = Path.Combine(backupDir, importFileName);

            if (!IOFile.Exists(filePath))
            {
                Console.WriteLine($"Import file not found: {Path.GetFullPath(filePath)}. Skipping import.");
                // Optionally, notify admin
                return;
            }

            try
            {
                var json = await IOFile.ReadAllTextAsync(filePath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true }; // Good practice for deserialization
                var data = JsonSerializer.Deserialize<BotData>(json, options);

                if (data is null)
                {
                    Console.WriteLine($"Failed to deserialize data from {Path.GetFullPath(filePath)}. Data is null. Skipping import.");
                    // Optionally, notify admin
                    return;
                }

                // These Load methods will be implemented in the respective services in a later step.
                // For UserProfile, the UserSessionService needs a way to overwrite or update its user profiles.
                // This might involve clearing existing profiles or merging. For now, assume replacement.
                if (data.Users != null)
                {
                    _userSessionService.LoadUsers(data.Users); // Assumes UserSessionService will have LoadUsers
                }

                // For Lessons, Program.cs currently holds them in a static list.
                // A true "LessonService" would be needed to make this cleaner.
                // For now, we can replace the static list.
                if (data.Lessons != null)
                {
                    // LessonService.LoadLessons(data.Lessons); // Placeholder if we had a LessonService
                    allLessonsData.Clear();
                    allLessonsData.AddRange(data.Lessons);
                    Console.WriteLine($"Loaded {data.Lessons.Count} lessons into Program.cs static list.");
                }

                // For Tests, similar to Lessons, Program.cs uses _testLoaderService to load one test.
                // If we are importing a list of tests, how TestLoaderService handles this needs definition.
                // The current BotData structure implies a list of tests.
                // For simplicity, if there's at least one test in the import,
                // we can assume it's the one TestLoaderService should be aware of or
                // that activeTestsData should be updated.
                // This part is a bit tricky with the current structure.
                // Let's assume for now we replace activeTestsData if data.Tests is not null and has items.
                // This would require converting List<Omnieye.Bot.Models.Test> to Dictionary<int, TestData>
                // which is not straightforward as they are different structures.

                // Simplification: The export saves a List<Test>. The import will try to load this.
                // However, TestLoaderService.LoadTest() returns one Test.
                // And Program.cs uses activeTestsData (Dictionary<int, TestData>).
                // This is a mismatch.
                // For now, I will log that Test loading from BotData.Tests is not fully implemented
                // due to structural differences between BotData.Tests (List<Models.Test>)
                // and how tests are managed internally (activeTestsData: Dict<int, CoreModels.TestData>
                // and TestLoaderService loading a single Models.Test).
                // A proper TestService.LoadTests(List<Models.Test>) would be needed.

                if (data.Tests != null && data.Tests.Any())
                {
                    // TestService.LoadTests(data.Tests); // Placeholder
                    Console.WriteLine($"Imported {data.Tests.Count} tests. Manual integration into TestLoaderService or activeTestsData would be needed with current structure.");
                    // For now, let's try to replace the test loaded by _testLoaderService if there's one test in the import.
                    if (data.Tests.Count == 1)
                    {
                        // This doesn't directly update _testLoaderService, as it loads from file.
                        // This is more of a conceptual "the main test is now this one".
                        // The `tests_junior_admin.json` would ideally be updated by this import.
                        // For now, we can log this. The export saves the state of tests_junior_admin.json.
                        // The import should ideally overwrite tests_junior_admin.json with the imported test.
                        var firstTest = data.Tests.First();
                        string testFilePath = Path.Combine("materials/junior_admin", "tests_junior_admin.json");
                        try
                        {
                            var testJson = JsonSerializer.Serialize(firstTest, new JsonSerializerOptions { WriteIndented = true });
                            await IOFile.WriteAllTextAsync(testFilePath, testJson);
                            Console.WriteLine($"Successfully updated '{testFilePath}' with the imported test data.");
                            // Optionally, re-initialize _testLoaderService or clear its cache if it has one.
                             _testLoaderService = new TestLoaderService(); // Re-instantiate to pick up changes on next LoadTest() call
                        }
                        catch(Exception ex)
                        {
                             Console.WriteLine($"Could not write imported test to {testFilePath}: {ex.Message}");
                        }
                    }
                }

                Console.WriteLine($"Data successfully imported from {Path.GetFullPath(filePath)}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during data import from {Path.GetFullPath(filePath)}: {ex.Message}");
                // Optionally, notify admin
            }
        }

        static async Task HandleLoginCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, string? password, CancellationToken ct)
        {
            if (session.CurrentState == UserCurrentState.TakingTest && session.ActiveTestId.HasValue) {
                await botClient.SendTextMessageAsync(chatId, "Пожалуйста, завершите или остановите текущий тест (команда /stoptest), прежде чем пытаться войти.", cancellationToken: ct);
                await DisplayCurrentTestQuestionAsync(botClient, session, chatId, ct);
                return;
            }
            if (session.IsAuthenticated) {
                await botClient.SendTextMessageAsync(chatId, "You are already authenticated.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                return;
            }
            string? trimmedPassword = password?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedPassword)) {
                await botClient.SendTextMessageAsync(chatId, "Please provide a password. Usage: /login <password>", cancellationToken: ct);
                return;
            }
            if (AuthorizationService.Authenticate(session.UserId, trimmedPassword, _userSessionService)) {
                await botClient.SendTextMessageAsync(chatId, "Вы успешно авторизованы.\nВыберите действие:", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                session.CurrentState = UserCurrentState.MainMenu;
            } else {
                await botClient.SendTextMessageAsync(chatId, "Authentication failed. Invalid password.", cancellationToken: ct);
            }
        }

        static async Task HandleLogoutCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            if (session.CurrentState == UserCurrentState.TakingTest && session.ActiveTestId.HasValue)  {
                await botClient.SendTextMessageAsync(chatId, "Пожалуйста, завершите или остановите текущий тест (команда /stoptest), прежде чем выходить из системы.", cancellationToken: ct);
                await DisplayCurrentTestQuestionAsync(botClient, session, chatId, ct);
                return;
            }
            if (!session.IsAuthenticated) {
                 await botClient.SendTextMessageAsync(chatId, "You are not currently authenticated.", cancellationToken: ct);
                return;
            }
            AuthorizationService.Logout(session.UserId, _userSessionService);
            session.CurrentState = UserCurrentState.MainMenu;
            await botClient.SendTextMessageAsync(chatId, "You have been logged out.", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: ct);
        }

        static async Task HandleStartCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            var messages = new List<string> { "Welcome to Omnieye Certification Bot! Use /courses to see available courses, or /help for more commands." };
            if (!session.IsAuthenticated) messages.Add("Please use /login <password> to access content.");

            if (session.CurrentState == UserCurrentState.TakingTest && session.ActiveTestId.HasValue)
            {
                await botClient.SendTextMessageAsync(chatId, "Вы находитесь в процессе теста. Введите ответ или /stoptest для остановки.", cancellationToken: ct);
                await DisplayCurrentTestQuestionAsync(botClient, session, chatId, ct);
            }
            else if (session.IsAuthenticated)
            {
                await SendCombinedMessages(botClient, chatId, messages, ct, MainCommandKeyboard);
                session.CurrentState = UserCurrentState.MainMenu;
            }
            else
            {
                await SendCombinedMessages(botClient, chatId, messages, ct);
            }
        }

        static async Task HandleHelpCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            var messages = new List<string> { "Available commands:" };
            IReplyMarkup? currentKeyboard = null;

            if (session.CurrentState == UserCurrentState.TakingTest && session.ActiveTestId.HasValue)
            {
                messages.Add("Вы находитесь в процессе теста.");
                messages.Add("Выберите вариант ответа кнопкой или введите /stoptest для остановки теста.");
                messages.Add("/stoptest - Stop the current test.");
            }
            else if (!session.IsAuthenticated)
            {
                messages.Add("/login <password> - Authenticate to access the bot");
                messages.Add("/start - Welcome message");
            }
            else
            {
                messages.Add("/logout - Log out from the bot");
                messages.Add("/courses - List available courses (or use '📘 Уроки' button)");
                messages.Add("/lesson <number> - Get lesson content");
                messages.Add("/test <number> - Start a specific test (or use '🧪 Тесты' button)");
                messages.Add("/profile - View your profile");
                messages.Add("/history - View your test history");
                messages.Add("/setname - Set your display name");
                messages.Add("/start - Welcome message & main menu");

                if (session.CurrentState == UserCurrentState.ViewingLessonDetail) currentKeyboard = LessonDetailKeyboard;
                else if (session.CurrentState == UserCurrentState.ViewingTestDetail) currentKeyboard = TestDetailKeyboard;
                else currentKeyboard = MainCommandKeyboard;
            }
            messages.Add("/help - Show this help message");
            await SendCombinedMessages(botClient, chatId, messages, ct, currentKeyboard);
        }

        static async Task HandleCoursesCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            if (session.CurrentState == UserCurrentState.TakingTest && session.ActiveTestId.HasValue) {
                await botClient.SendTextMessageAsync(chatId, "Пожалуйста, завершите или остановите текущий тест (команда /stoptest), прежде чем просматривать курсы.", cancellationToken: ct);
                await DisplayCurrentTestQuestionAsync(botClient, session, chatId, ct);
                return;
            }
            await HandleLessonsListAsync(botClient, session, chatId, ct);
        }

        static async Task HandleLessonCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, string? argument, CancellationToken ct)
        {
            if (session.CurrentState == UserCurrentState.TakingTest && session.ActiveTestId.HasValue) {
                await botClient.SendTextMessageAsync(chatId, "Пожалуйста, завершите или остановите текущий тест (команда /stoptest), прежде чем просматривать урок.", cancellationToken: ct);
                await DisplayCurrentTestQuestionAsync(botClient, session, chatId, ct);
                return;
            }
            if (string.IsNullOrWhiteSpace(argument) || !int.TryParse(argument, out int lessonNumber) || lessonNumber <= 0) {
                await botClient.SendTextMessageAsync(chatId, "Пожалуйста, укажите номер урока. Например: /lesson 1", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                session.CurrentState = UserCurrentState.MainMenu;
                return;
            }
            if (lessonNumber > 0 && lessonNumber <= allLessonsData.Count)
            {
                await HandleLessonContentAsync(botClient, session, chatId, allLessonsData[lessonNumber - 1], ct);
            }
            else
            {
                 await botClient.SendTextMessageAsync(chatId, "Извините, урок с таким номером не найден.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                 session.CurrentState = UserCurrentState.ViewingLessonList;
            }
        }

        static async Task HandleTestCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, string? argument, CancellationToken ct)
        {
             if (session.CurrentState == UserCurrentState.TakingTest && session.ActiveTestId.HasValue) {
                await botClient.SendTextMessageAsync(chatId, "Вы уже находитесь в процессе теста. Введите ответ или /stoptest для остановки.", cancellationToken: ct);
                await DisplayCurrentTestQuestionAsync(botClient, session, chatId, ct);
                return;
            }
            if (string.IsNullOrWhiteSpace(argument) || !int.TryParse(argument, out int testIdToStart) || testIdToStart <= 0)
            {
                await HandleTestsListAsync(botClient, session, chatId, ct);
                await botClient.SendTextMessageAsync(chatId, "Чтобы начать конкретный тест командой, введите /test <номер_теста>.", cancellationToken: ct);
                return;
            }
            await StartActualTestAsync(botClient, session, chatId, testIdToStart, ct);
        }

        static List<Lesson> GetAvailableLessonsForUserLevel(int userProfileLevel, IEnumerable<Lesson> allLessons)
        {
            if (userProfileLevel < 3)
                return allLessons.Where(l => l.Level == LessonLevel.Beginner).ToList();
            else if (userProfileLevel < 6)
                return allLessons.Where(l => l.Level == LessonLevel.Beginner || l.Level == LessonLevel.Intermediate).ToList();
            else
                return allLessons.ToList();
        }

        static string GetLessonDifficultyIcon(LessonLevel level) => level switch
        {
            LessonLevel.Beginner => "🟢",
            LessonLevel.Intermediate => "🟡",
            LessonLevel.Advanced => "🔴",
            _ => "⚪"
        };

        static string GetDifficultyIcon(TestDifficulty difficulty) => difficulty switch
        {
            TestDifficulty.Easy => "🟢",
            TestDifficulty.Medium => "🟡",
            TestDifficulty.Hard => "🔴",
            _ => "⚪"
        };

        static async Task HandleLessonsListAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            if (session.CurrentState == UserCurrentState.TakingTest && session.ActiveTestId.HasValue)
            {
                await botClient.SendTextMessageAsync(chatId, "Пожалуйста, завершите или остановите текущий тест (команда /stoptest), прежде чем просматривать уроки.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                await DisplayCurrentTestQuestionAsync(botClient, session, chatId, ct);
                return;
            }

            var lessonsToShow = GetAvailableLessonsForUserLevel(session.Profile.Level, allLessonsData);

            if (!lessonsToShow.Any()) {
                await botClient.SendTextMessageAsync(chatId, "Для вашего уровня пока нет доступных уроков.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                session.CurrentState = UserCurrentState.MainMenu;
                session.LastShownLessonTitles = null;
                return;
            }

            session.LastShownLessonTitles = lessonsToShow.Select(l => l.Title).ToList();

            var messageBuilder = new StringBuilder("Доступные уроки для вашего уровня:\n\n");
            foreach (var lesson in lessonsToShow)
            {
                var icon = GetLessonDifficultyIcon(lesson.Level);
                messageBuilder.AppendLine($"{icon} *{lesson.Title}*");
                messageBuilder.AppendLine($"_{lesson.Summary}_");
                messageBuilder.AppendLine();
            }
            messageBuilder.AppendLine("👉 Напиши точное название урока из списка, чтобы открыть его.");

            await SendLongMessageAsync(botClient, chatId, messageBuilder.ToString(), ct, MainCommandKeyboard, parseMode: ParseMode.Markdown);
            session.CurrentState = UserCurrentState.ViewingLessonList;
        }

        static async Task HandleLessonContentAsync(ITelegramBotClient botClient, UserSession session, long chatId, Lesson lessonToShow, CancellationToken ct)
        {
            if (lessonToShow == null)
            {
                await botClient.SendTextMessageAsync(chatId, "Ошибка: Урок не найден.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                await HandleLessonsListAsync(botClient, session, chatId, ct);
                return;
            }

            var icon = GetLessonDifficultyIcon(lessonToShow.Level);
            string header = $"{icon} *{lessonToShow.Title}*\n\n";

            await SendLongMessageAsync(botClient, chatId, header + lessonToShow.Content, ct, LessonDetailKeyboard, parseMode: ParseMode.Markdown);
            session.CurrentState = UserCurrentState.ViewingLessonDetail;
            session.ViewingItemId = null;
            session.ViewingLessonTitle = lessonToShow.Title;
        }

        static async Task HandleTestsListAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            if (session.CurrentState == UserCurrentState.TakingTest && session.ActiveTestId.HasValue)
            {
                await botClient.SendTextMessageAsync(chatId, "Пожалуйста, завершите или остановите текущий тест (команда /stoptest), прежде чем просматривать список тестов.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                await DisplayCurrentTestQuestionAsync(botClient, session, chatId, ct);
                return;
            }

            var userLevel = session.Profile.Level;
            List<TestData> testsToList;

            if (userLevel < 3)
                testsToList = activeTestsData.Values.Where(t => t.Difficulty == TestDifficulty.Easy).ToList();
            else if (userLevel < 6)
                testsToList = activeTestsData.Values.Where(t => t.Difficulty != TestDifficulty.Hard).ToList();
            else
                testsToList = activeTestsData.Values.ToList();

            if (!testsToList.Any()) {
                await botClient.SendTextMessageAsync(chatId, "Для вашего уровня пока нет доступных тестов или вы прошли все доступные.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                session.CurrentState = UserCurrentState.MainMenu;
                return;
            }

            session.LastShownTestList = testsToList;

            var messageBuilder = new StringBuilder("Доступные тесты для вашего уровня:\n");
            for (int i = 0; i < testsToList.Count; i++)
            {
                var test = testsToList[i];
                var icon = GetDifficultyIcon(test.Difficulty);
                messageBuilder.AppendLine($"{i + 1}. {icon} {test.TestName}");
            }
            messageBuilder.AppendLine("\nОтправьте номер теста для просмотра информации и начала.");

            await SendLongMessageAsync(botClient, chatId, messageBuilder.ToString(), ct, MainCommandKeyboard);
            session.CurrentState = UserCurrentState.ViewingTestList;
        }

        static async Task HandleLessonDetailAsync(ITelegramBotClient botClient, UserSession session, long chatId, int lessonNumber, CancellationToken ct)
        {
            if (lessonNumber > 0 && lessonNumber <= allLessonsData.Count)
            {
                var lessonToView = allLessonsData[lessonNumber - 1];
                await HandleLessonContentAsync(botClient, session, chatId, lessonToView, ct);
            }
            else
            {
                await botClient.SendTextMessageAsync(chatId, "Извините, урок с таким номером не найден.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                session.CurrentState = UserCurrentState.ViewingLessonList;
            }
        }

        static async Task HandleTestDetailAsync(ITelegramBotClient botClient, UserSession session, long chatId, int testId, CancellationToken ct)
        {
            if (activeTestsData.TryGetValue(testId, out var testData))
            {
                string description = testDetails.TryGetValue(testId, out var desc) ? desc : $"Тест: {testData.TestName}";
                var icon = GetDifficultyIcon(testData.Difficulty);
                string fullDetailText = $"{icon} *{testData.TestName}*\n{description}";

                await SendLongMessageAsync(botClient, chatId, fullDetailText, ct, TestDetailKeyboard, parseMode: ParseMode.Markdown);
                session.CurrentState = UserCurrentState.ViewingTestDetail;
                session.ViewingItemId = testId;
            }
            else
            {
                await botClient.SendTextMessageAsync(chatId, "Извините, тест с таким номером не найден или для него нет описания.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                session.CurrentState = UserCurrentState.ViewingTestList;
            }
        }

        static async Task StartActualTestAsync(ITelegramBotClient botClient, UserSession session, long chatId, int testId, CancellationToken ct)
        {
            if (!activeTestsData.TryGetValue(testId, out var testToStart))
            {
                await botClient.SendTextMessageAsync(chatId, "Ошибка: Выбранный тест не найден.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                session.CurrentState = UserCurrentState.ViewingTestList;
                return;
            }
            if (testToStart.Questions == null || !testToStart.Questions.Any())
            {
                await botClient.SendTextMessageAsync(chatId, $"Ошибка: В тесте \"{testToStart.TestName}\" нет вопросов.", replyMarkup: TestDetailKeyboard, cancellationToken: ct);
                session.CurrentState = UserCurrentState.ViewingTestDetail;
                return;
            }
            session.StartNewTest(testId);
            await botClient.SendTextMessageAsync(chatId, $"Начинаем тест: \"{testToStart.TestName}\"", cancellationToken: ct, replyMarkup: new ReplyKeyboardRemove());
            await DisplayCurrentTestQuestionAsync(botClient, session, chatId, ct);
        }

        static async Task DisplayCurrentTestQuestionAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            if (!session.ActiveTestId.HasValue || !activeTestsData.TryGetValue(session.ActiveTestId.Value, out var currentTestData))
            {
                await botClient.SendTextMessageAsync(chatId, "Ошибка: Тест не найден или не активен.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                session.EndCurrentTest();
                session.CurrentState = UserCurrentState.MainMenu;
                return;
            }
            if (session.CurrentQuestionIndex >= currentTestData.Questions.Count)
            {
                await botClient.SendTextMessageAsync(chatId, "Кажется, все вопросы закончились, но тест не был завершен корректно.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                session.EndCurrentTest();
                session.CurrentState = UserCurrentState.MainMenu;
                return;
            }
            QuestionData question = currentTestData.Questions[session.CurrentQuestionIndex];
            var keyboardButtons = question.Options.Select(option => new KeyboardButton(option)).ToArray();
            var replyKeyboardMarkup = new ReplyKeyboardMarkup(keyboardButtons.Select(kb => new[] { kb }))
            {
                ResizeKeyboard = true,
                OneTimeKeyboard = true
            };
            string questionMessage = $"Вопрос {session.CurrentQuestionIndex + 1} из {currentTestData.Questions.Count}:\n\n{question.Text}";
            await botClient.SendTextMessageAsync(chatId, questionMessage, replyMarkup: replyKeyboardMarkup, cancellationToken: ct);
        }

        static async Task HandleStopTestCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            bool testWasStopped = false;
            if (session.ActiveTestId.HasValue && session.CurrentState == UserCurrentState.TakingTest)
            {
                var testName = activeTestsData.TryGetValue(session.ActiveTestId.Value, out var testData) ? testData.TestName : "текущий";
                session.EndCurrentTest();
                await botClient.SendTextMessageAsync(chatId, $"Тест \"{testName}\" остановлен. Ваш прогресс не сохранен.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                testWasStopped = true;
            }
            if (!testWasStopped)
            {
                await botClient.SendTextMessageAsync(chatId, "Нет активного теста для остановки.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
            }
        }

        static async Task HandleHistoryAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            if (session.CurrentState == UserCurrentState.TakingTest && session.ActiveTestId.HasValue)
            {
                await botClient.SendTextMessageAsync(chatId, "Пожалуйста, завершите или остановите текущий тест (команда /stoptest), прежде чем просматривать историю.", cancellationToken: ct);
                await DisplayCurrentTestQuestionAsync(botClient, session, chatId, ct);
                return;
            }

            if (session.TestHistory == null || !session.TestHistory.Any())
            {
                await botClient.SendTextMessageAsync(chatId, "Вы ещё не проходили тесты.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
            }
            else
            {
                var historyTextBuilder = new StringBuilder("🕓 История тестов:\n");
                historyTextBuilder.Append(
                    string.Join("\n", session.TestHistory.Select((entry, i) =>
                        $"{i + 1}. 📘 {entry.TestTitle}: {entry.CorrectAnswers}/{entry.TotalQuestions} — {entry.PassedAt:g}"))
                );
                await SendLongMessageAsync(botClient, chatId, historyTextBuilder.ToString(), ct, MainCommandKeyboard);
            }
            session.CurrentState = UserCurrentState.MainMenu;
        }

        static async Task HandleProfileAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            if (session.CurrentState == UserCurrentState.TakingTest && session.ActiveTestId.HasValue)
            {
                await botClient.SendTextMessageAsync(chatId, "Пожалуйста, завершите или остановите текущий тест (команда /stoptest), прежде чем просматривать профиль.", cancellationToken: ct);
                await DisplayCurrentTestQuestionAsync(botClient, session, chatId, ct);
                return;
            }

            var profile = session.Profile;
            if (profile.UserId == 0 && session.UserId != 0) {
                profile.UserId = session.UserId;
            }

            string name = !string.IsNullOrWhiteSpace(profile.Name) ? profile.Name : $"User {profile.UserId}";

            var profileTextBuilder = new StringBuilder();
            profileTextBuilder.AppendLine($"👤 *Профиль*");
            profileTextBuilder.AppendLine($"Имя: {name}");
            profileTextBuilder.AppendLine($"Уровень: {profile.Level}");
            profileTextBuilder.AppendLine($"Тестов пройдено: {profile.TotalTestsTaken}");
            profileTextBuilder.AppendLine($"Правильных ответов: {profile.TotalCorrectAnswers}");
            profileTextBuilder.AppendLine($"Зарегистрирован: {profile.RegisteredAt:g}");

            await botClient.SendTextMessageAsync(
                chatId,
                profileTextBuilder.ToString(),
                parseMode: ParseMode.Markdown,
                replyMarkup: MainCommandKeyboard,
                cancellationToken: ct);

            session.CurrentState = UserCurrentState.MainMenu;
        }

        static async Task SendLongMessageAsync(ITelegramBotClient botClient, long chatId, string message, CancellationToken cancellationToken, IReplyMarkup? replyMarkup = null, ParseMode? parseMode = null, int chunkSize = 4000)
        {
            if (string.IsNullOrEmpty(message)) return;
            var chunks = SplitMessage(message, chunkSize);
            for (int i = 0; i < chunks.Count; i++)
            {
                bool isLastChunk = i == chunks.Count - 1;

                ParseMode? currentChunkParseMode = parseMode;

                if (currentChunkParseMode.HasValue)
                {
                    await botClient.SendTextMessageAsync(chatId, chunks[i],
                        replyMarkup: isLastChunk ? replyMarkup : null,
                        parseMode: currentChunkParseMode.Value,
                        cancellationToken: cancellationToken);
                }
                else
                {
                    await botClient.SendTextMessageAsync(chatId, chunks[i],
                        replyMarkup: isLastChunk ? replyMarkup : null,
                        cancellationToken: cancellationToken);
                }

                if (!isLastChunk) await Task.Delay(200, cancellationToken);
            }
        }

        static async Task SendCombinedMessages(ITelegramBotClient botClient, long chatId, List<string> messages, CancellationToken cancellationToken, IReplyMarkup? replyMarkup = null, ParseMode? parseMode = null)
        {
            string combined = string.Join("\n", messages);
            await SendLongMessageAsync(botClient, chatId, combined, cancellationToken, replyMarkup, parseMode: parseMode);
        }

        static async Task ShowLeaderboardAsync(ITelegramBotClient botClient, UserSession currentSession, long chatId, CancellationToken ct) // Added currentSession for context checks
        {
            if (currentSession.CurrentState == UserCurrentState.TakingTest && currentSession.ActiveTestId.HasValue)
            {
                await botClient.SendTextMessageAsync(chatId, "Пожалуйста, завершите или остановите текущий тест (команда /stoptest), прежде чем просматривать лидерборд.", cancellationToken: ct);
                await DisplayCurrentTestQuestionAsync(botClient, currentSession, chatId, ct);
                return;
            }

            // UserSessionService instance is _userSessionService (static field)
            var allUserProfiles = _userSessionService.GetAllUserProfiles();

            if (allUserProfiles == null || !allUserProfiles.Any())
            {
                await botClient.SendTextMessageAsync(chatId, "Лидерборд пока пуст.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                currentSession.CurrentState = UserCurrentState.MainMenu;
                return;
            }

            var topUsers = allUserProfiles
                .OrderByDescending(p => p.TotalCorrectAnswers) // Assuming score is TotalCorrectAnswers
                .ThenBy(p => p.RegisteredAt) // Secondary sort for tie-breaking by registration date (earlier is better)
                .Take(10)
                .ToList();

            if (!topUsers.Any()) // Should be caught by previous check, but good for safety
            {
                await botClient.SendTextMessageAsync(chatId, "Лидерборд пока пуст.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                currentSession.CurrentState = UserCurrentState.MainMenu;
                return;
            }

            var leaderboardText = new StringBuilder("🏆 Топ 10 пользователей:\n");
            int rank = 1;
            foreach (var profile in topUsers)
            {
                string name = !string.IsNullOrWhiteSpace(profile.Name) ? profile.Name : $"User {profile.UserId}";
                // Using TotalCorrectAnswers as "баллов" as per UserProfile structure
                leaderboardText.AppendLine($"{rank++}. {name} — {profile.TotalCorrectAnswers} баллов (Уровень: {profile.Level})");
            }

            await SendLongMessageAsync(botClient, chatId, leaderboardText.ToString(), ct, MainCommandKeyboard);
            currentSession.CurrentState = UserCurrentState.MainMenu; // Viewing leaderboard returns to main menu context
        }

        public static List<string> SplitMessage(string message, int chunkSize = 4000)
        {
            var messages = new List<string>();
            if (string.IsNullOrEmpty(message)) return messages;
            for (int i = 0; i < message.Length; i += chunkSize)
            {
                messages.Add(message.Substring(i, Math.Min(chunkSize, message.Length - i)));
            }
            return messages;
        }

        // Helper to return to Admin Root
        static async Task GoToAdminRoot(ITelegramBotClient botClient, UserSession session, long chatId, string message, CancellationToken cancellationToken)
        {
            var adminKeyboard = new ReplyKeyboardMarkup(new[]
            {
                new[] { new KeyboardButton("➕ Добавить урок"), new KeyboardButton("➕ Добавить тест") },
                new[] { new KeyboardButton("✏️ Редактировать урок"), new KeyboardButton("✏️ Редактировать тест") },
                new[] { new KeyboardButton("🗑 Удалить урок"), new KeyboardButton("🗑 Удалить тест") },
                new[] { new KeyboardButton("↩️ Выйти из админ панели") }
            })
            {
                ResizeKeyboard = true
            };
            session.CurrentState = UserCurrentState.AdminRoot;
            session.PendingLesson = null; // Clear any pending operations
            session.PendingTest = null;
            session.EditingItemId = null;
            session.EditingField = null;
            await botClient.SendTextMessageAsync(chatId, message, replyMarkup: adminKeyboard, cancellationToken: cancellationToken);
        }
    } // End of Program class

    public static class AuthorizationService
    {
        private const string HardcodedPassword = "omni_password123";

        public static bool Authenticate(long userId, string? password, UserSessionService sessionService)
        {
            if (password == HardcodedPassword)
            {
                sessionService.UpdateUserAuthentication(userId, true);
                Console.WriteLine($"Bot Response: Authentication successful for User {userId}. You now have access to all commands.");
                return true;
            }
            Console.WriteLine($"Bot Response: Authentication failed for User {userId}. Invalid password.");
            return false;
        }

        public static bool CheckAuthentication(long userId, UserSessionService sessionService)
        {
            var session = sessionService.GetUserSession(userId);
            return session.IsAuthenticated;
        }

        public static void Logout(long userId, UserSessionService sessionService)
        {
            sessionService.UpdateUserAuthentication(userId, false);
            var session = sessionService.GetUserSession(userId);
            session.EndCurrentTest();
            Console.WriteLine($"Bot Response: User {userId} has been logged out.");
        }
    }
}
