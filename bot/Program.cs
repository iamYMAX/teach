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
            User? user = null;
            long chatId = 0;
            UserSession session;
            string? messageText = null;
            string? callbackData = null;
            Message? incomingMessageContext = null; // To access MessageId for callback replies etc.
            CallbackQuery? cbQuery = null;

            if (update.Type == UpdateType.Message && update.Message != null)
            {
                incomingMessageContext = update.Message;
                user = incomingMessageContext.From;
                chatId = incomingMessageContext.Chat.Id;
                messageText = incomingMessageContext.Text;
            }
            else if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
            {
                cbQuery = update.CallbackQuery;
                user = cbQuery.From;
                if (cbQuery.Message == null) {
                    Console.WriteLine("CallbackQuery without a message context received. Cannot process.");
                    if(!string.IsNullOrEmpty(cbQuery.Id)) await botClient.AnswerCallbackQueryAsync(cbQuery.Id, "Ошибка: нет контекста сообщения.", cancellationToken: cancellationToken);
                    return;
                }
                incomingMessageContext = cbQuery.Message;
                chatId = incomingMessageContext.Chat.Id;
                callbackData = cbQuery.Data;
            }

            if (user == null || chatId == 0) // Ensure essential context
            {
                Console.WriteLine($"Update type {update.Type} without sufficient User/Chat context. Ignoring.");
                if(cbQuery?.Id != null) await botClient.AnswerCallbackQueryAsync(cbQuery.Id, "Ошибка контекста.", cancellationToken: cancellationToken);
                return;
            }

            session = _userSessionService.GetUserSession(user.Id);

            // 1. Handle Callback Queries
            if (update.Type == UpdateType.CallbackQuery && cbQuery != null && incomingMessageContext != null)
            {
                Console.WriteLine($"Received CallbackQuery '{callbackData}' from User {user.Id} in Chat {chatId}. State: {session.CurrentState}");
                bool callbackHandledInAdminBlock = false;
                if (IsAdmin(user.Id))
                {
                    // ADMIN CALLBACKS
                    // Lesson Edit
                    if (callbackData != null && callbackData.StartsWith("admin_edit_lesson_")) {
                        string lessonId = callbackData.Substring("admin_edit_lesson_".Length);
                        session.EditingItemId = lessonId; session.CurrentState = UserCurrentState.AdminEditingLessonSelectField;
                        var kbd = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("Название", $"admin_edit_field_title") }, new[] { InlineKeyboardButton.WithCallbackData("Описание", $"admin_edit_field_description") }, new[] { InlineKeyboardButton.WithCallbackData("Содержание", $"admin_edit_field_content") }, new[] { InlineKeyboardButton.WithCallbackData("Уровень", $"admin_edit_field_level") } });
                        await botClient.EditMessageTextAsync(chatId, incomingMessageContext.MessageId, "Выбран урок. Какое поле редактировать?", replyMarkup: kbd, cancellationToken: cancellationToken);
                        callbackHandledInAdminBlock = true;
                    } else if (callbackData != null && callbackData.StartsWith("admin_edit_field_") && session.CurrentState == UserCurrentState.AdminEditingLessonSelectField) {
                        string fieldToEdit = callbackData.Substring("admin_edit_field_".Length);
                        session.EditingField = fieldToEdit; session.CurrentState = UserCurrentState.AdminEditingLessonEnterNewValue;
                        await botClient.EditMessageReplyMarkupAsync(chatId, incomingMessageContext.MessageId, replyMarkup: null, cancellationToken: cancellationToken); // Remove inline
                        if (fieldToEdit == "level") {
                            var kbd = new ReplyKeyboardMarkup(new[] { new[] { new KeyboardButton(LessonLevel.Beginner.ToString()), new KeyboardButton(LessonLevel.Intermediate.ToString()) }, new[] { new KeyboardButton(LessonLevel.Advanced.ToString()) }}) { ResizeKeyboard = true, OneTimeKeyboard = true };
                            await botClient.SendTextMessageAsync(chatId, $"Урок (ID: {session.EditingItemId?.Substring(0,Math.Min(8, session.EditingItemId?.Length ?? 0) )}...). Нов. уровень:", replyMarkup: kbd, cancellationToken: cancellationToken);
                        } else { await botClient.SendTextMessageAsync(chatId, $"Урок (ID: {session.EditingItemId?.Substring(0,Math.Min(8, session.EditingItemId?.Length ?? 0) )}...). Нов. значение для '{fieldToEdit}':", cancellationToken: cancellationToken); }
                        callbackHandledInAdminBlock = true;
                    }
                    // Lesson Delete
                    else if (callbackData != null && callbackData.StartsWith("admin_delete_lesson_") && session.CurrentState == UserCurrentState.AdminDeletingLessonSelect) { /* ... as before ... */
                        string lessonId = callbackData.Substring("admin_delete_lesson_".Length); var lesson = _adminDataService.GetLessonById(lessonId);
                        if (lesson != null) { session.EditingItemId = lessonId; var kbd = new InlineKeyboardMarkup(new[] { InlineKeyboardButton.WithCallbackData($"Да, удалить \"{lesson.Title.Substring(0, Math.Min(20, lesson.Title.Length))}...\"", $"admin_confirm_delete_lesson_{lessonId}"), InlineKeyboardButton.WithCallbackData("Нет", "admin_cancel_delete") }); await botClient.EditMessageTextAsync(chatId, incomingMessageContext.MessageId, $"Удалить урок \"{lesson.Title}\"?", replyMarkup: kbd, cancellationToken: cancellationToken); }
                        else { await GoToAdminRoot(botClient, session, chatId, "Ошибка: урок не найден.", cancellationToken); }
                        callbackHandledInAdminBlock = true;
                    } else if (callbackData != null && callbackData.StartsWith("admin_confirm_delete_lesson_") && session.CurrentState == UserCurrentState.AdminDeletingLessonSelect) { /* ... */
                        if (!string.IsNullOrEmpty(session.EditingItemId) && callbackData.EndsWith(session.EditingItemId)) { try { await _adminDataService.DeleteLesson(session.EditingItemId); await botClient.EditMessageTextAsync(chatId, incomingMessageContext.MessageId, "Урок удален.", cancellationToken: cancellationToken); } catch (KeyNotFoundException) { await botClient.EditMessageTextAsync(chatId, incomingMessageContext.MessageId, "Ошибка: Урок не найден.", cancellationToken: cancellationToken); } await GoToAdminRoot(botClient, session, chatId, "Выберите действие:", cancellationToken); }
                        else { await GoToAdminRoot(botClient, session, chatId, "Ошибка подтверждения.", cancellationToken); } session.EditingItemId = null;
                        callbackHandledInAdminBlock = true;
                    } else if (callbackData == "admin_cancel_delete" && session.CurrentState == UserCurrentState.AdminDeletingLessonSelect) { /* ... */
                        await botClient.EditMessageTextAsync(chatId, incomingMessageContext.MessageId, "Удаление отменено.", cancellationToken: cancellationToken); await GoToAdminRoot(botClient, session, chatId, "Выберите действие:", cancellationToken);
                        callbackHandledInAdminBlock = true;
                    }
                    // Test Edit (Metadata & Navigation)
                    else if (callbackData != null && callbackData.StartsWith("admin_edit_test_") && session.CurrentState == UserCurrentState.AdminEditingTestSelect) { /* ... */
                        string testId = callbackData.Substring("admin_edit_test_".Length); var test = _adminDataService.GetTestById(testId);
                        if (test != null) { session.EditingItemId = testId; session.CurrentState = UserCurrentState.AdminEditingTestSelectField; var kbd = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("Название", $"admin_edit_testmeta_title") }, new[] { InlineKeyboardButton.WithCallbackData("Описание", $"admin_edit_testmeta_description") }, new[] { InlineKeyboardButton.WithCallbackData("Сложность", $"admin_edit_testmeta_difficulty") }, new[] { InlineKeyboardButton.WithCallbackData("Вопросы", $"admin_edit_testquestions_{testId}") }, new[] { InlineKeyboardButton.WithCallbackData("<< Назад", $"admin_back_to_test_select_for_edit") } }); await botClient.EditMessageTextAsync(chatId, incomingMessageContext.MessageId, $"Тест: \"{test.TestName}\". Что изменить?", replyMarkup: kbd, cancellationToken: cancellationToken); }
                        else { await GoToAdminRoot(botClient, session, chatId, "Ошибка: тест не найден.", cancellationToken); }
                        callbackHandledInAdminBlock = true;
                    } else if (callbackData != null && callbackData.StartsWith("admin_edit_testmeta_") && session.CurrentState == UserCurrentState.AdminEditingTestSelectField) { /* ... */
                        string field = callbackData.Substring("admin_edit_testmeta_".Length); session.EditingField = field; session.CurrentState = UserCurrentState.AdminEditingTestEnterNewValue;
                        await botClient.EditMessageReplyMarkupAsync(chatId, incomingMessageContext.MessageId, replyMarkup: null, cancellationToken: cancellationToken);
                        if (field == "difficulty") { var kbd = new ReplyKeyboardMarkup(new[] { new[] { new KeyboardButton(TestDifficulty.Easy.ToString()),new KeyboardButton(TestDifficulty.Medium.ToString())}, new[] {new KeyboardButton(TestDifficulty.Hard.ToString())}}) { ResizeKeyboard = true, OneTimeKeyboard = true }; await botClient.SendTextMessageAsync(chatId, $"Тест (ID: {session.EditingItemId?.Substring(0,Math.Min(8, session.EditingItemId?.Length??0))}...). Нов. сложность:", replyMarkup: kbd, cancellationToken: cancellationToken); }
                        else { await botClient.SendTextMessageAsync(chatId, $"Тест (ID: {session.EditingItemId?.Substring(0,Math.Min(8, session.EditingItemId?.Length??0))}...). Нов. значение для '{field}':", cancellationToken: cancellationToken); }
                        callbackHandledInAdminBlock = true;
                    } else if (callbackData == "admin_back_to_test_select_for_edit" && session.CurrentState == UserCurrentState.AdminEditingTestSelectField) { /* ... */
                        session.CurrentState = UserCurrentState.AdminRoot; await botClient.DeleteMessageAsync(chatId, incomingMessageContext.MessageId, cancellationToken);
                        await GoToAdminRoot(botClient, session, chatId, "Выберите тест (нажмите '✏️ Ред. тест').", cancellationToken);
                        callbackHandledInAdminBlock = true;
                    }
                    // Test Delete
                     else if (callbackData != null && callbackData.StartsWith("admin_delete_test_") && session.CurrentState == UserCurrentState.AdminDeletingTestSelect) { /* ... */
                        string testId = callbackData.Substring("admin_delete_test_".Length); var test = _adminDataService.GetTestById(testId);
                        if (test != null) { session.EditingItemId = testId; var kbd = new InlineKeyboardMarkup(new[] { InlineKeyboardButton.WithCallbackData($"Да, удалить \"{test.TestName.Substring(0,Math.Min(20,test.TestName.Length))}...\"", $"admin_confirm_delete_test_{testId}"), InlineKeyboardButton.WithCallbackData("Нет", "admin_cancel_delete_test") }); await botClient.EditMessageTextAsync(chatId, incomingMessageContext.MessageId, $"Удалить тест \"{test.TestName}\"?", replyMarkup: kbd, cancellationToken: cancellationToken); }
                        else { await GoToAdminRoot(botClient, session, chatId, "Ошибка: тест не найден.", cancellationToken); }
                        callbackHandledInAdminBlock = true;
                    } else if (callbackData != null && callbackData.StartsWith("admin_confirm_delete_test_") && session.CurrentState == UserCurrentState.AdminDeletingTestSelect) { /* ... */
                        if (!string.IsNullOrEmpty(session.EditingItemId) && callbackData.EndsWith(session.EditingItemId)) { try { await _adminDataService.DeleteTest(session.EditingItemId); await botClient.EditMessageTextAsync(chatId, incomingMessageContext.MessageId, "Тест удален.", cancellationToken: cancellationToken); } catch (KeyNotFoundException) { await botClient.EditMessageTextAsync(chatId, incomingMessageContext.MessageId, "Ошибка: Тест не найден.", cancellationToken: cancellationToken); } await GoToAdminRoot(botClient, session, chatId, "Выберите действие:", cancellationToken); }
                        else { await GoToAdminRoot(botClient, session, chatId, "Ошибка подтверждения.", cancellationToken); } session.EditingItemId = null;
                        callbackHandledInAdminBlock = true;
                    } else if (callbackData == "admin_cancel_delete_test" && session.CurrentState == UserCurrentState.AdminDeletingTestSelect) { /* ... */
                        await botClient.EditMessageTextAsync(chatId, incomingMessageContext.MessageId, "Удаление теста отменено.", cancellationToken: cancellationToken); await GoToAdminRoot(botClient, session, chatId, "Выберите действие:", cancellationToken);
                        callbackHandledInAdminBlock = true;
                    }
                    // Test Question Management
                    else if (callbackData != null && callbackData.StartsWith("admin_edit_testquestions_") && session.CurrentState == UserCurrentState.AdminEditingTestSelectField) { /* ... */
                        string testId = callbackData.Substring("admin_edit_testquestions_".Length); if (session.EditingItemId != testId) { await GoToAdminRoot(botClient, session, chatId, "Ошибка ID теста.", cancellationToken); return; } var test = _adminDataService.GetTestById(testId);
                        if (test != null) { session.CurrentState = UserCurrentState.AdminEditingTestQuestionSelect; var kbd = new List<IEnumerable<InlineKeyboardButton>> { new[] { InlineKeyboardButton.WithCallbackData("Добавить вопрос", $"admin_add_qst_to_test_{testId}") } }; if (test.Questions.Any()) { for(int i=0; i<test.Questions.Count; i++) { var q = test.Questions[i]; string qs = q.Text.Length > 30 ? q.Text.Substring(0,27)+"..." : q.Text; kbd.Add(new[] { InlineKeyboardButton.WithCallbackData($"✏️ {i+1}. {qs}", $"admin_edit_existing_qst_{testId}_{i}"), InlineKeyboardButton.WithCallbackData($"🗑️", $"admin_delete_existing_qst_{testId}_{i}") });}} else { kbd.Add(new[] { InlineKeyboardButton.WithCallbackData("Вопросов нет", "admin_no_op") });} kbd.Add(new[] { InlineKeyboardButton.WithCallbackData("<< Назад", $"admin_edit_test_{testId}") }); await botClient.EditMessageTextAsync(chatId, incomingMessageContext.MessageId, $"Вопросы теста: \"{test.TestName}\"", replyMarkup: new InlineKeyboardMarkup(kbd), cancellationToken: cancellationToken); }
                        else { await GoToAdminRoot(botClient, session, chatId, "Тест не найден.", cancellationToken); }
                        callbackHandledInAdminBlock = true;
                    } else if (callbackData != null && callbackData.StartsWith("admin_add_qst_to_test_") && session.CurrentState == UserCurrentState.AdminEditingTestQuestionSelect) { /* ... */
                        string testId = callbackData.Substring("admin_add_qst_to_test_".Length); if(session.EditingItemId != testId) return; session.PendingQuestion = new QuestionData(); session.CurrentState = UserCurrentState.AdminAddingTestQuestionText;
                        await botClient.EditMessageReplyMarkupAsync(chatId, incomingMessageContext.MessageId, replyMarkup:null, cancellationToken: cancellationToken); await botClient.SendTextMessageAsync(chatId, "Текст нового вопроса:", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
                        callbackHandledInAdminBlock = true;
                    } else if (callbackData != null && callbackData.StartsWith("admin_edit_existing_qst_") && session.CurrentState == UserCurrentState.AdminEditingTestQuestionSelect) { /* ... */
                        var p = callbackData.Split('_'); if(p.Length <6) return; string tId=p[4]; if(!int.TryParse(p[5], out int qIdx)) return; if(session.EditingItemId!=tId) return; var t = _adminDataService.GetTestById(tId);
                        if (t!=null && qIdx >=0 && qIdx < t.Questions.Count) { session.EditingQuestionIndex = qIdx; session.CurrentState = UserCurrentState.AdminEditingTestQuestionEditField; var q = t.Questions[qIdx]; var kbd = new InlineKeyboardMarkup(new[] {new[] {InlineKeyboardButton.WithCallbackData("Текст", $"admin_edit_qstpart_text_{tId}_{qIdx}")}, new[] {InlineKeyboardButton.WithCallbackData("Варианты", $"admin_edit_qstpart_options_{tId}_{qIdx}")}, new[] {InlineKeyboardButton.WithCallbackData("Прав. ответ", $"admin_edit_qstpart_correct_{tId}_{qIdx}")}, new[] {InlineKeyboardButton.WithCallbackData("<< Назад", $"admin_edit_testquestions_{tId}")} }); await botClient.EditMessageTextAsync(chatId, incomingMessageContext.MessageId, $"Вопрос {qIdx+1}: \"{q.Text.Substring(0,Math.Min(30,q.Text.Length))}...\". Что изменить?", replyMarkup:kbd,cancellationToken:cancellationToken); }
                        callbackHandledInAdminBlock = true;
                    } else if (callbackData != null && callbackData.StartsWith("admin_edit_qstpart_") && session.CurrentState == UserCurrentState.AdminEditingTestQuestionEditField) { /* ... */
                        var p = callbackData.Split('_'); if(p.Length<6) return; string type=p[3]; string tId=p[4]; if(!int.TryParse(p[5], out int qIdx)) return; if(session.EditingItemId!=tId || session.EditingQuestionIndex!=qIdx) return;
                        session.EditingField = $"question_{type}"; session.CurrentState = UserCurrentState.AdminEditingTestQuestionEnterNewValue; string prompt="";
                        switch(type){ case "text": prompt="Новый текст вопроса:"; break; case "options": prompt="Новые варианты через запятую:"; break;
                            case "correct": var t = _adminDataService.GetTestById(tId); if(t!=null && qIdx < t.Questions.Count){var q=t.Questions[qIdx]; var otxt = string.Join("\n",q.Options.Select((o,i)=>$"{i+1}. {o}")); prompt=$"Варианты:\n{otxt}\n\nНовый номер прав. ответа:";}else{prompt="Нов. номер прав. ответа:";} break;
                            default: await GoToAdminRoot(botClient, session, chatId, "Неизвестное поле.", cancellationToken); return;}
                        await botClient.EditMessageReplyMarkupAsync(chatId, incomingMessageContext.MessageId, replyMarkup:null, cancellationToken:cancellationToken); await botClient.SendTextMessageAsync(chatId, prompt, replyMarkup: new ReplyKeyboardRemove(), cancellationToken:cancellationToken);
                        callbackHandledInAdminBlock = true;
                    } else if (callbackData != null && callbackData.StartsWith("admin_delete_existing_qst_") && session.CurrentState == UserCurrentState.AdminEditingTestQuestionSelect) { /* ... */
                        var p = callbackData.Split('_'); if(p.Length <6) return; string tId=p[4]; if(!int.TryParse(p[5], out int qIdx)) return; if(session.EditingItemId!=tId) return;
                        var t = _adminDataService.GetTestById(tId);
                        if (t!=null && qIdx>=0 && qIdx < t.Questions.Count) { var qDel=t.Questions[qIdx]; t.Questions.RemoveAt(qIdx); await _adminDataService.UpdateTest(t); await botClient.AnswerCallbackQueryAsync(cbQuery.Id, $"Вопрос удален.",cancellationToken:cancellationToken); callbackHandledInAdminBlock=true;
                            var kbdList = new List<IEnumerable<InlineKeyboardButton>> { new[] { InlineKeyboardButton.WithCallbackData("Добавить вопрос", $"admin_add_qst_to_test_{t.Id}") } }; if (t.Questions.Any()) { for(int i=0; i < t.Questions.Count; i++) { var q = t.Questions[i]; string qs = q.Text.Length > 30 ? q.Text.Substring(0, 27) + "..." : q.Text; kbdList.Add(new[] { InlineKeyboardButton.WithCallbackData($"✏️ {i+1}. {qs}", $"admin_edit_existing_qst_{t.Id}_{i}"), InlineKeyboardButton.WithCallbackData($"🗑️", $"admin_delete_existing_qst_{t.Id}_{i}") }); } } else { kbdList.Add(new[] { InlineKeyboardButton.WithCallbackData("Вопросов нет", "admin_no_op") }); } kbdList.Add(new[] { InlineKeyboardButton.WithCallbackData("<< Назад", $"admin_edit_test_{t.Id}") }); await botClient.EditMessageTextAsync(chatId, incomingMessageContext.MessageId, $"Вопросы теста: \"{t.TestName}\"", replyMarkup: new InlineKeyboardMarkup(kbdList), cancellationToken:cancellationToken);
                        }
                    } else if (callbackData == "admin_no_op") { /* Acknowledge no-op button press */ callbackHandledInAdminBlock = true; }
                }

                if (!string.IsNullOrEmpty(cbQuery.Id) && !callbackHandledInAdminBlock) // Acknowledge if not handled by admin logic or if not admin
                {
                     await botClient.AnswerCallbackQueryAsync(cbQuery.Id, "Действие обработано или нет прав.", cancellationToken: cancellationToken);
                }
                 else if (!string.IsNullOrEmpty(cbQuery.Id) && callbackHandledInAdminBlock) // If handled by admin, acknowledge silently or with specific message if needed
                {
                    await botClient.AnswerCallbackQueryAsync(cbQuery.Id, cancellationToken: cancellationToken);
                }
                return;
            }

            // --- Message Processing Starts Here (if not a callback or callback was not fully handling) ---
            if (update.Type != UpdateType.Message || messageText == null)
            {
                if (update.Message != null) Console.WriteLine($"Ignoring non-text message type {update.Message.Type} from User {user.Id}.");
                return;
            }

            Console.WriteLine($"Received '{messageText}' from User {user.Id} in Chat {chatId}. State: {session.CurrentState}, WaitingForName: {session.WaitingForNameInput}");

            if (session.WaitingForNameInput)
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
                        if (session.CurrentState == UserCurrentState.TakingTest && !string.IsNullOrEmpty(session.ActiveTestId)) {
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
            if (session.CurrentState == UserCurrentState.TakingTest && !string.IsNullOrEmpty(session.ActiveTestId)) {
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
            if (session.CurrentState == UserCurrentState.TakingTest && !string.IsNullOrEmpty(session.ActiveTestId))  {
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

            if (session.CurrentState == UserCurrentState.TakingTest && !string.IsNullOrEmpty(session.ActiveTestId))
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

            if (session.CurrentState == UserCurrentState.TakingTest && !string.IsNullOrEmpty(session.ActiveTestId))
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
            if (session.CurrentState == UserCurrentState.TakingTest && !string.IsNullOrEmpty(session.ActiveTestId)) {
                await botClient.SendTextMessageAsync(chatId, "Пожалуйста, завершите или остановите текущий тест (команда /stoptest), прежде чем просматривать курсы.", cancellationToken: ct);
                await DisplayCurrentTestQuestionAsync(botClient, session, chatId, ct);
                return;
            }
            await HandleLessonsListAsync(botClient, session, chatId, ct);
        }

        static async Task HandleLessonCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, string? argument, CancellationToken ct)
        {
            if (session.CurrentState == UserCurrentState.TakingTest && !string.IsNullOrEmpty(session.ActiveTestId)) { // Check string ActiveTestId
                await botClient.SendTextMessageAsync(chatId, "Пожалуйста, завершите или остановите текущий тест (команда /stoptest), прежде чем просматривать урок.", cancellationToken: ct);
                await DisplayCurrentTestQuestionAsync(botClient, session, chatId, ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(argument))
            {
                await botClient.SendTextMessageAsync(chatId, "Пожалуйста, укажите номер или название урока.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                session.CurrentState = UserCurrentState.MainMenu; // Or ViewingLessonList
                return;
            }

            var allLessons = _adminDataService.GetAllLessons();
            Lesson? lessonToView = null;

            if (int.TryParse(argument, out int lessonNumber) && lessonNumber > 0 && lessonNumber <= allLessons.Count)
            {
                // Assuming lessons are displayed in a consistent order that allows selection by number
                lessonToView = allLessons[lessonNumber - 1];
            }
            else
            {
                // Try to find by title (case-insensitive)
                lessonToView = allLessons.FirstOrDefault(l => l.Title.Equals(argument.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            if (lessonToView != null)
            {
                await HandleLessonContentAsync(botClient, session, chatId, lessonToView.Id, ct);
            }
            else
            {
                 await botClient.SendTextMessageAsync(chatId, "Извините, урок с таким номером или названием не найден.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                 session.CurrentState = UserCurrentState.ViewingLessonList; // Or MainMenu
            }
        }

        static async Task HandleTestCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, string? argument, CancellationToken ct)
        {
            if (session.CurrentState == UserCurrentState.TakingTest && !string.IsNullOrEmpty(session.ActiveTestId)) { // Check string ActiveTestId
                await botClient.SendTextMessageAsync(chatId, "Вы уже находитесь в процессе теста. Введите ответ или /stoptest для остановки.", cancellationToken: ct);
                await DisplayCurrentTestQuestionAsync(botClient, session, chatId, ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(argument))
            {
                await HandleTestsListAsync(botClient, session, chatId, ct); // Show list if no arg
                await botClient.SendTextMessageAsync(chatId, "Чтобы начать конкретный тест командой, введите /test <номер_теста_из_списка_или_ID>.", cancellationToken: ct);
                return;
            }

            TestData? testToStart = null;
            var allTests = _adminDataService.GetAllTests();

            if (int.TryParse(argument, out int testNumberToList) && session.LastShownTestList != null && testNumberToList > 0 && testNumberToList <= session.LastShownTestList.Count)
            {
                // User provided a number from the last shown list
                testToStart = session.LastShownTestList[testNumberToList - 1];
            }
            else
            {
                // User provided an ID or name (treat argument as ID first, then try name if not found by ID)
                testToStart = _adminDataService.GetTestById(argument) ?? allTests.FirstOrDefault(t => t.TestName.Equals(argument.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            if (testToStart != null)
            {
                await StartActualTestAsync(botClient, session, chatId, testToStart.Id, ct);
            }
            else
            {
                await HandleTestsListAsync(botClient, session, chatId, ct);
                await botClient.SendTextMessageAsync(chatId, "Тест с таким номером/ID/названием не найден.", cancellationToken: ct);
            }
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
            if (session.CurrentState == UserCurrentState.TakingTest && !string.IsNullOrEmpty(session.ActiveTestId)) // Corrected: Was !string.IsNullOrEmpty(session.ActiveTestId) already, which is fine.
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
                messageBuilder.AppendLine($"_{lesson.Description}_"); // Corrected from Summary
                messageBuilder.AppendLine();
            }
            messageBuilder.AppendLine("👉 Напиши номер или точное название урока из списка, чтобы открыть его (e.g., `/lesson 1` or `/lesson Название урока`).");

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
