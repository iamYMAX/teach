using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Omnieye.Bot.CoreModels;
using Omnieye.Bot.Services;
using Omnieye.Bot.States;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using System.Text.Json;
using Omnieye.Bot.Models;
using System.Timers;
using IOFile = System.IO.File;

// Using statements for the NEW course navigation logic
// Aliases removed, will use FQTNs or direct namespace usings
// using NewMessageHandler = OmnieyeBot.BotHandlers.MessageHandler;
// using NewCourseContentLoaderService = OmnieyeBot.Services.CourseContentLoaderService;
using OmnieyeBot.BotHandlers; // For MessageHandler
using OmnieyeBot.Services;    // For CourseContentLoaderService


namespace Omnieye.Bot
{
    class Program
    {
        // Existing services
        // Use FQTN for existing UserSessionService to be absolutely clear
        private static Omnieye.Bot.Services.UserSessionService _userSessionService = new Omnieye.Bot.Services.UserSessionService();
        private static MaterialLoader _materialLoader = new MaterialLoader();
        private static TestLoaderService _testLoaderService = new TestLoaderService();

        // New services and handlers for course navigation
        private static OmnieyeBot.Services.CourseContentLoaderService _courseContentLoaderService;
        private static OmnieyeBot.BotHandlers.MessageHandler _newCourseMessageHandler;

        private static ITelegramBotClient? _botClient;
        private static CancellationTokenSource? _cts;

        // --- Full original static fields for keyboards, lessons, tests data ---
        private static readonly List<string> availableLessons_OLD_FORMAT = new List<string>
        {
            "Урок 1: Введение в систему", "Урок 2: Основы работы", "Урок 3: Продвинутые возможности"
        };
        private static readonly Dictionary<int, string> lessonDetails_OLD_FORMAT = new Dictionary<int, string>
        {
            { 1, "Урок 1: Введение в систему\n\nЗдесь рассказывается об основах работы с ботом и системой." },
            { 2, "Урок 2: Основы работы\n\nОписание основных функций и интерфейса." },
            { 3, "Урок 3: Продвинутые возможности\n\nДополнительные настройки и советы." }
        };
         private static readonly List<string> availableTests_OLD_FORMAT = new List<string>
        {
            "Тест 1: Проверка знаний по основам", "Тест 2: Продвинутый тест"
        };
        private static readonly Dictionary<int, string> testDetails = new Dictionary<int, string> // Restored original name
        {
            { 1, "Тест 1: Проверка знаний по основам\n\nВключает вопросы по базовым темам." },
            { 2, "Тест 2: Продвинутый тест\n\nСложные вопросы для опытных пользователей." }
        };

        private static readonly ReplyKeyboardMarkup MainCommandKeyboard = new ReplyKeyboardMarkup(new KeyboardButton[][]
        {
            // Используем константу из MainMenuKeyboard, которая теперь без эмодзи
            new KeyboardButton[] { new KeyboardButton(OmnieyeBot.Keyboards.MainMenuKeyboard.LessonsButtonText), new KeyboardButton("🧪 Тесты") },
            new KeyboardButton[] { new KeyboardButton("🧠 Флеш-карточки"), new KeyboardButton("История") },
            new KeyboardButton[] { new KeyboardButton("🏆 Топ"), new KeyboardButton("👤 Профиль") },
            new KeyboardButton[] { new KeyboardButton("🔐 Выйти") }
        })
        {
            ResizeKeyboard = true
        };

        private static readonly ReplyKeyboardMarkup LessonDetailKeyboard = new ReplyKeyboardMarkup(new KeyboardButton[][]
        {
            new KeyboardButton[] { "Назад к списку уроков" }
        })
        {
            ResizeKeyboard = true
        };

        private static readonly ReplyKeyboardMarkup TestDetailKeyboard = new ReplyKeyboardMarkup(new KeyboardButton[][]
        {
            new KeyboardButton[] { "Начать тест", "Назад" }
        })
        {
            ResizeKeyboard = true
        };

        private static readonly ReplyKeyboardMarkup AfterTestMenuKeyboard = new ReplyKeyboardMarkup(new KeyboardButton[][]
        {
            new KeyboardButton[] { "Вернуться в меню" }
        })
        {
            ResizeKeyboard = true,
            OneTimeKeyboard = true
        };

        private static readonly ReplyKeyboardMarkup FlashcardQuestionKeyboard = new ReplyKeyboardMarkup(new KeyboardButton[][]
        {
            new KeyboardButton[] { new KeyboardButton("Показать ответ") },
            new KeyboardButton[] { new KeyboardButton("Следующая карточка") },
            new KeyboardButton[] { new KeyboardButton("↩ Меню") }
        })
        {
            ResizeKeyboard = true
        };

        private static readonly Dictionary<int, TestData> activeTestsData = new Dictionary<int, TestData> // Restored original name
        {
            {
                1, new TestData(1, "Тест по основам", new List<QuestionData>
                {
                    new QuestionData("Вопрос 1: Что такое бот?", new List<string>{ "Программа", "Человек", "Животное" }, 0),
                    new QuestionData("Вопрос 2: Какой язык используется в этом боте?", new List<string>{ "C#", "Python", "JavaScript" }, 0)
                }, TestDifficulty.Easy)
            },
            {
                2, new TestData(2, "Продвинутый тест", new List<QuestionData>
                {
                    new QuestionData("Вопрос 1 (П): Что такое сеть?", new List<string>{ "Группа компьютеров", "Отдельный компьютер", "Принтер" }, 0),
                    new QuestionData("Вопрос 2 (П): IP-адрес это?", new List<string>{ "Физический адрес", "Логический адрес", "Почтовый адрес" }, 1)
                }, TestDifficulty.Medium)
            }
        };

        private static readonly List<Lesson> allLessonsData = new List<Lesson> // This is Omnieye.Bot.CoreModels.Lesson
        {
            new Lesson("Основы IP-адресации", "Введение в IP, IPv4 и IPv6.", "IP-адрес — это уникальный идентификатор устройства в сети...\n\nIPv4 адреса состоят из 4 октетов и разделяются точками (например, 192.168.1.1). Они обеспечивают около 4 миллиардов уникальных адресов.\n\nIPv6 адреса намного длиннее, используют шестнадцатеричную систему и разделяются двоеточиями (например, 2001:0db8:85a3:0000:0000:8a2e:0370:7334). Они предоставляют практически неисчерпаемый пул адресов.", LessonLevel.Beginner),
            new Lesson("Маршрутизация в сетях", "Принципы работы маршрутизаторов и таблиц маршрутизации.", "Маршрутизация - это процесс определения оптимального пути для передачи данных от источника к получателю через одну или несколько сетей. Маршрутизаторы используют таблицы маршрутизации для принятия этих решений.\n\nСтатическая маршрутизация настраивается вручную администратором.\nДинамическая маршрутизация использует протоколы (например, OSPF, BGP, RIP) для автоматического обмена информацией о маршрутах и обновления таблиц.", LessonLevel.Intermediate),
            new Lesson("Ключевые Сетевые Протоколы", "Обзор TCP, UDP, HTTP, DNS и их функций.", "TCP (Transmission Control Protocol) - протокол с установлением соединения, гарантирующий доставку данных и их порядок.\nUDP (User Datagram Protocol) - протокол без установления соединения, быстрый, но не гарантирует доставку.\nHTTP/HTTPS (HyperText Transfer Protocol/Secure) - основа для передачи данных в WWW.\nDNS (Domain Name System) - преобразует доменные имена в IP-адреса.", LessonLevel.Beginner),
            new Lesson("Виртуализация Сетей", "Основы SDN и NFV, их применение.", "SDN (Software-Defined Networking) отделяет управляющий уровень сети (control plane) от уровня передачи данных (data plane), позволяя централизованно управлять сетевыми устройствами.\nNFV (Network Functions Virtualization) виртуализирует сетевые функции, такие как брандмауэры, балансировщики нагрузки, позволяя им работать на стандартном оборудовании.", LessonLevel.Advanced)
        };

        private static readonly List<Flashcard> allFlashcardsData = new List<Flashcard>
        {
            new Flashcard("Что такое DHCP?", "Протокол динамической настройки узла (Dynamic Host Configuration Protocol), позволяющий устройствам автоматически получать IP-адреса и другие сетевые параметры.", LessonLevel.Beginner),
            new Flashcard("Назовите 3 уровня модели OSI.", "Физический, Канальный, Сетевой (или любые три из семи: Физический, Канальный, Сетевой, Транспортный, Сеансовый, Представления, Прикладной).", LessonLevel.Beginner),
            new Flashcard("Что означает VPN?", "Virtual Private Network (Виртуальная Частная Сеть) - технология, позволяющая создавать безопасное зашифрованное соединение поверх другой сети (обычно Интернет).", LessonLevel.Intermediate),
            new Flashcard("Для чего используется порт 22/TCP?", "Для протокола SSH (Secure Shell), обеспечивающего безопасное удаленное управление.", LessonLevel.Intermediate),
            new Flashcard("Что такое контейнеризация?", "Метод виртуализации на уровне операционной системы, позволяющий упаковывать приложение и его зависимости в изолированные окружения (контейнеры).", LessonLevel.Advanced),
            new Flashcard("Кратко опишите CI/CD.", "Continuous Integration / Continuous Delivery (or Deployment) - практики автоматизации сборки, тестирования и развертывания программного обеспечения.", LessonLevel.Advanced)
        };
        // --- End of full original static fields ---

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

            // Initialize NEW CourseContentLoaderService
            _courseContentLoaderService = new OmnieyeBot.Services.CourseContentLoaderService();

            // Initialize NEW MessageHandler for course navigation
            // It needs botClient, existing userSessionService, and the new courseContentLoaderService
            _newCourseMessageHandler = new OmnieyeBot.BotHandlers.MessageHandler(_botClient, _userSessionService, _courseContentLoaderService);

            StartAutoBackup();

            var receiverOptions = new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() };

            _botClient.StartReceiving(
                updateHandler: GlobalUpdateHandlerAsync, // Changed to a new method
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

        // New Global Update Handler
        static async Task GlobalUpdateHandlerAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            try
            {
                if (update.Type == UpdateType.Message)
                {
                    // Try handling with the new course navigation logic first (text messages)
                    bool handledByCourseLogic = await _newCourseMessageHandler.HandleUpdateAsync(botClient, update, cancellationToken);

                    if (!handledByCourseLogic)
                    {
                        // If not handled by course logic, pass to the original generic handler for text messages
                        await HandleUpdateAsyncOriginal(botClient, update, cancellationToken);
                    }
                }
                else if (update.Type == UpdateType.CallbackQuery)
                {
                    // Handle inline button callbacks
                    await _newCourseMessageHandler.HandleCallbackQueryAsync(botClient, update.CallbackQuery, cancellationToken);
                }
                // Potentially handle other update types here if needed by original logic
                else if (update.Message != null) // Fallback for other message types to original handler
                {
                     await HandleUpdateAsyncOriginal(botClient, update, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GlobalUpdateHandlerAsync: {ex}");
                // Optionally, notify user or admin about the error
                if (update.Message?.Chat?.Id != null)
                {
                    await botClient.SendTextMessageAsync(update.Message.Chat.Id, "Произошла внутренняя ошибка. Попробуйте позже.", cancellationToken: cancellationToken);
                }
                else if (update.CallbackQuery?.Message?.Chat?.Id != null)
                {
                     await botClient.SendTextMessageAsync(update.CallbackQuery.Message.Chat.Id, "Произошла внутренняя ошибка при обработке вашего действия. Попробуйте позже.", cancellationToken: cancellationToken);
                }
            }
        }

        // This is the ORIGINAL HandleUpdateAsync, renamed to HandleUpdateAsyncOriginal
        static async Task HandleUpdateAsyncOriginal(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            // Original handler now only needs to process message updates not handled by new logic
            if (update.Message is not { } message) return;
            if (message.From is not { } user) return;
            // If it's not a text message, it wouldn't have been handled by _newCourseMessageHandler, so it's fine to process here.
            // However, _newCourseMessageHandler already checks for Text, so this condition is mainly for non-text messages or if new handler returned false.
            string messageText = message.Text ?? ""; // Use empty string if null for non-text messages to avoid null ref later

            long userId = user.Id;
            long chatId = message.Chat.Id;
            var session = _userSessionService.GetUserSession(userId);

            Console.WriteLine($"Original Handler: Received '{messageText}' from User {userId} in Chat {chatId}. State: {session.CurrentState}, WaitingForName: {session.WaitingForNameInput}");

            if (session.WaitingForNameInput)
            {
                if (messageText.StartsWith("/"))
                {
                    session.WaitingForNameInput = false;
                    session.CurrentState = UserCurrentState.MainMenu;
                    await botClient.SendTextMessageAsync(chatId, "Ввод имени отменен.", replyMarkup: MainCommandKeyboard, cancellationToken: cancellationToken);
                    if (messageText.ToLower() == "/setname") return; // Avoid re-processing /setname if it was the cancelling command
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

            if (session.CurrentState == UserCurrentState.TakingTest)
            {
                if (!session.ActiveTestId.HasValue || !activeTestsData.TryGetValue(session.ActiveTestId.Value, out var currentTestData) ||
                    session.CurrentQuestionIndex >= currentTestData.Questions.Count)
                {
                    await botClient.SendTextMessageAsync(chatId, "Произошла ошибка с текущим тестом. Возвращаемся в главное меню.", replyMarkup: MainCommandKeyboard, cancellationToken: cancellationToken);
                    session.EndCurrentTest(); // Resets state
                    return;
                }

                QuestionData currentQuestion = currentTestData.Questions[session.CurrentQuestionIndex];
                int selectedOptionIdx = currentQuestion.Options.IndexOf(messageText);

                if (selectedOptionIdx != -1) // User selected a valid option by text match
                {
                    if (selectedOptionIdx == currentQuestion.CorrectOptionIndex) session.CurrentTestScore++;
                    session.CurrentQuestionIndex++;

                    if (session.CurrentQuestionIndex < currentTestData.Questions.Count)
                    {
                        await DisplayCurrentTestQuestionAsync(botClient, session, chatId, cancellationToken);
                    }
                    else // Test finished
                    {
                        if (session.ActiveTestId.HasValue) // Should always be true here
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
                        _userSessionService.UpdateProgress(userId, session.CurrentTestScore); // Save progress
                        string resultMessage = $"Тест \"{currentTestData.TestName}\" завершён.\nВаш результат: {session.CurrentTestScore} из {currentTestData.Questions.Count}.";
                        await botClient.SendTextMessageAsync(chatId, resultMessage, replyMarkup: AfterTestMenuKeyboard, cancellationToken: cancellationToken);
                        session.EndCurrentTest(); // Resets test state, including CurrentState
                    }
                }
                else if (messageText.ToLower() == "/stoptest") // Allow stopping test with command
                {
                    await HandleStopTestCommandAsync(botClient, session, chatId, cancellationToken);
                }
                else // Invalid input during test
                {
                    await botClient.SendTextMessageAsync(chatId, "Пожалуйста, выберите один из предложенных вариантов.", cancellationToken: cancellationToken);
                    // Optionally re-display the question to ensure user sees options again if keyboard was one-time
                    await DisplayCurrentTestQuestionAsync(botClient, session, chatId, cancellationToken);
                }
                return; // All input during TakingTest state is handled here or is an error for this state.
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
                        session.EndFlashcardSession();
                        await HandleStartCommandAsync(botClient, session, chatId, cancellationToken);
                        break;
                    default:
                        flashcardActionProcessed = false;
                        break;
                }
                if (flashcardActionProcessed) return;
            }


            if (session.IsAuthenticated)
            {
                bool keyboardButtonProcessed = true;
                // "📘 Уроки" is handled by _newCourseMessageHandler. If we reach here, it wasn't that.
                switch (messageText)
                {
                    // case "📘 Уроки": // This case is now effectively handled by the new logic path
                    case "🧪 Тесты": await HandleTestsListAsync(botClient, session, chatId, cancellationToken); break;
                    case "🧠 Флеш-карточки":
                        await StartFlashcardSessionAsync(botClient, session, chatId, cancellationToken);
                        break;
                    case "История": await HandleHistoryAsync(botClient, session, chatId, cancellationToken); break;
                    case "👤 Профиль": await HandleProfileAsync(botClient, session, chatId, cancellationToken); break;
                    case "🏆 Топ":
                        await ShowLeaderboardAsync(botClient, session, chatId, cancellationToken);
                        break;
                    case "Назад": // Generic "Назад" for original menu system
                        if (session.CurrentState == UserCurrentState.ViewingTestDetail) await HandleTestsListAsync(botClient, session, chatId, cancellationToken);
                        else // Default "Назад" goes to main menu
                        {
                            await botClient.SendTextMessageAsync(chatId, "Главное меню.", replyMarkup: MainCommandKeyboard, cancellationToken: cancellationToken);
                            session.CurrentState = UserCurrentState.MainMenu;
                        }
                        break;
                    case "Назад к списку уроков": // Specific to old lesson viewing
                         await HandleLessonsListAsync(botClient, session, chatId, cancellationToken);
                         break;
                    case "Начать тест":
                        if (session.CurrentState == UserCurrentState.ViewingTestDetail && session.ViewingItemId.HasValue) await StartActualTestAsync(botClient, session, chatId, session.ViewingItemId.Value, cancellationToken);
                        else if (session.CurrentState == UserCurrentState.ViewingTestDetail && !session.ViewingItemId.HasValue) await botClient.SendTextMessageAsync(chatId, "Ошибка: не удалось определить, какой тест запустить.", replyMarkup: TestDetailKeyboard, cancellationToken: cancellationToken);
                        else await botClient.SendTextMessageAsync(chatId, "Пожалуйста, сначала выберите тест из списка.", replyMarkup: MainCommandKeyboard, cancellationToken: cancellationToken);
                        break;
                    case "Вернуться в меню": // After test
                        await HandleStartCommandAsync(botClient, session, chatId, cancellationToken); // This will show main menu
                        session.CurrentState = UserCurrentState.MainMenu; // Ensure state is reset
                        break;
                    case "🔐 Выйти": await HandleLogoutCommandAsync(botClient, session, chatId, cancellationToken); break;
                    default: keyboardButtonProcessed = false; break; // Not a known button for this handler
                }
                if (keyboardButtonProcessed) return;
            }

            // Handling numeric input for old lesson/test system
            if (session.IsAuthenticated)
            {
                bool inputHandled = false;
                if (int.TryParse(messageText, out int selectionNumber) && selectionNumber > 0)
                {
                     if (session.CurrentState == UserCurrentState.ViewingLessonList) // Old lesson list
                    {
                        if (selectionNumber > 0 && selectionNumber <= allLessonsData.Count) // allLessonsData is Omnieye.Bot.CoreModels.Lesson
                        {
                             await HandleLessonContentAsync(botClient, session, chatId, allLessonsData[selectionNumber-1], cancellationToken);
                             inputHandled = true;
                        } else {
                            await botClient.SendTextMessageAsync(chatId, "Неверный номер урока.", replyMarkup: MainCommandKeyboard, cancellationToken: cancellationToken);
                            inputHandled = true; // Error was "handled"
                        }
                    }
                    else if (session.CurrentState == UserCurrentState.ViewingTestList) // Old test list
                    {
                        if (session.LastShownTestList != null && selectionNumber <= session.LastShownTestList.Count)
                        {
                            TestData selectedTest = session.LastShownTestList[selectionNumber - 1]; // TestData is Omnieye.Bot.CoreModels.TestData
                            await HandleTestDetailAsync(botClient, session, chatId, selectedTest.TestId, cancellationToken);
                        }
                        else
                        {
                            await botClient.SendTextMessageAsync(chatId, "Неверный номер теста. Пожалуйста, выберите из списка.", replyMarkup: MainCommandKeyboard, cancellationToken: cancellationToken);
                        }
                        inputHandled = true;
                    }
                }
                else // Handling selection by title for old lesson system
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

            // Command processing
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
                    // These are superseded by button UI handled by _newCourseMessageHandler or original button handler
                    // case "/courses": await HandleCoursesCommandAsync(botClient, session, chatId, cancellationToken); break;
                    // case "/lesson": await HandleLessonCommandAsync(botClient, session, chatId, argument, cancellationToken); break;
                    case "/test": await HandleTestCommandAsync(botClient, session, chatId, argument, cancellationToken); break; // For starting specific test by ID
                    case "/stoptest": await HandleStopTestCommandAsync(botClient, session, chatId, cancellationToken); break;
                    case "/setname":
                        if (!session.IsAuthenticated) { /* ... */ break; }
                        if (session.CurrentState == UserCurrentState.TakingTest && session.ActiveTestId.HasValue) { /* ... */ break; }
                        await botClient.SendTextMessageAsync(chatId, "Введите ваше имя:", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
                        session.WaitingForNameInput = true;
                        session.CurrentState = UserCurrentState.WaitingForNameInput;
                        break;
                    case "/profile": await HandleProfileAsync(botClient, session, chatId, cancellationToken); break;
                    case "/history": await HandleHistoryAsync(botClient, session, chatId, cancellationToken); break;
                    case "/leaderboard":
                    case "/top":
                        await ShowLeaderboardAsync(botClient, session, chatId, cancellationToken);
                        break;
                    case "/export":
                        if (IsAdmin(userId)) { /* ... */ } else { /* ... */ }
                        break;
                    case "/import":
                        if (IsAdmin(userId)) { /* ... */ } else { /* ... */ }
                        break;
                    default:
                        // If new logic didn't handle it, and it's not a known original command/button, then it's unknown.
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
                if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);

                var users = _userSessionService.GetAllUserProfiles() ?? new List<UserProfile>();
                var lessons = allLessonsData ?? new List<Lesson>();
                var tests = new List<Omnieye.Bot.Models.Test>();
                var loadedTest = _testLoaderService.LoadTest();
                if (loadedTest != null) tests.Add(loadedTest);

                var data = new BotData { Users = users, Lessons = lessons, Tests = tests };
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(data, options);
                await IOFile.WriteAllTextAsync(filePath, json);
                Console.WriteLine($"Data successfully exported to {Path.GetFullPath(filePath)}");
            }
            catch (Exception ex) { Console.WriteLine($"Error during data export: {ex.Message}"); }
        }

        private static bool IsAdmin(long userId)
        {
            long adminUserId = 123456789; // EXAMPLE ADMIN USER ID - CHANGE THIS!
            return userId == adminUserId;
            // Fallback removed for safety, ensure adminUserId is set.
        }

        private static async Task PerformAutoBackupAsync()
        {
            const string backupDir = "backup";
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            string filename = $"auto_backup_{timestamp}.json";
            string filePath = Path.Combine(backupDir, filename);
            try
            {
                if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);
                var users = _userSessionService.GetAllUserProfiles() ?? new List<UserProfile>();
                var lessons = allLessonsData ?? new List<Lesson>();
                var tests = new List<Omnieye.Bot.Models.Test>();
                var loadedTest = _testLoaderService.LoadTest();
                if (loadedTest != null) tests.Add(loadedTest);
                var data = new BotData { Users = users, Lessons = lessons, Tests = tests };
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(data, options);
                await IOFile.WriteAllTextAsync(filePath, json);
                Console.WriteLine($"Auto backup successful: Data saved to {Path.GetFullPath(filePath)}");
            }
            catch (Exception ex) { Console.WriteLine($"Error during auto backup to {filePath}: {ex.Message}"); }
        }

        private static void StartAutoBackup()
        {
            double interval = 30 * 60 * 1000; // 30 minutes
            var timer = new System.Timers.Timer(interval);
            timer.Elapsed += async (sender, e) => await PerformAutoBackupAsync();
            timer.AutoReset = true;
            timer.Enabled = true;
            Console.WriteLine($"Auto-backup service started. Backups will be performed every {interval / (60 * 1000)} minutes.");
        }

        private static async Task ImportDataAsync()
        {
            const string backupDir = "backup";
            const string importFileName = "omnieye_export.json";
            string filePath = Path.Combine(backupDir, importFileName);

            if (!IOFile.Exists(filePath)) { Console.WriteLine($"Import file not found: {Path.GetFullPath(filePath)}. Skipping import."); return; }
            try
            {
                var json = await IOFile.ReadAllTextAsync(filePath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var data = JsonSerializer.Deserialize<BotData>(json, options);
                if (data is null) { Console.WriteLine($"Failed to deserialize data from {Path.GetFullPath(filePath)}. Data is null. Skipping import."); return; }

                if (data.Users != null) _userSessionService.LoadUsers(data.Users);
                if (data.Lessons != null)
                {
                    allLessonsData.Clear();
                    allLessonsData.AddRange(data.Lessons);
                    Console.WriteLine($"Loaded {data.Lessons.Count} lessons into Program.cs static list.");
                }
                if (data.Tests != null && data.Tests.Any())
                {
                    Console.WriteLine($"Imported {data.Tests.Count} tests. Manual integration or TestService.LoadTests needed.");
                    if (data.Tests.Count == 1)
                    {
                        var firstTest = data.Tests.First();
                        string testFilePath = Path.Combine("materials/junior_admin", "tests_junior_admin.json");
                        try
                        {
                            var testJson = JsonSerializer.Serialize(firstTest, new JsonSerializerOptions { WriteIndented = true });
                            await IOFile.WriteAllTextAsync(testFilePath, testJson);
                            Console.WriteLine($"Successfully updated '{testFilePath}' with the imported test data.");
                            _testLoaderService = new TestLoaderService();
                        }
                        catch(Exception ex) { Console.WriteLine($"Could not write imported test to {testFilePath}: {ex.Message}"); }
                    }
                }
                Console.WriteLine($"Data successfully imported from {Path.GetFullPath(filePath)}.");
            }
            catch (Exception ex) { Console.WriteLine($"Error during data import from {Path.GetFullPath(filePath)}: {ex.Message}"); }
        }

        static async Task HandleLoginCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, string? password, CancellationToken ct)
        {
            if (session.CurrentState == UserCurrentState.TakingTest && session.ActiveTestId.HasValue) { /* ... */ return; }
            if (session.IsAuthenticated) { /* ... */ return; }
            string? trimmedPassword = password?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedPassword)) { /* ... */ return; }
            if (AuthorizationService.Authenticate(session.UserId, trimmedPassword, _userSessionService))
            {
                await botClient.SendTextMessageAsync(chatId, "Вы успешно авторизованы.\nВыберите действие:", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                session.CurrentState = UserCurrentState.MainMenu;
            } else { /* ... */ }
        }

        static async Task HandleLogoutCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            if (session.CurrentState == UserCurrentState.TakingTest && session.ActiveTestId.HasValue)  { /* ... */ return; }
            if (!session.IsAuthenticated) { /* ... */ return; }
            AuthorizationService.Logout(session.UserId, _userSessionService);
            session.CurrentState = UserCurrentState.MainMenu; // Reset state
            await botClient.SendTextMessageAsync(chatId, "You have been logged out.", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: ct);
        }

        static async Task HandleStartCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            // This HandleStartCommand is part of the *original* logic.
            // The new Course Navigation also has a HandleStartCommand, which shows its own main menu.
            // This one should only be called if the new logic doesn't handle /start.
            var messages = new List<string>();
            IReplyMarkup? keyboardToShow = null;

            if (session.CurrentState == UserCurrentState.TakingTest && session.ActiveTestId.HasValue)
            {
                await botClient.SendTextMessageAsync(chatId, "Вы находитесь в процессе теста. Введите ответ или /stoptest для остановки.", cancellationToken: ct);
                await DisplayCurrentTestQuestionAsync(botClient, session, chatId, ct);
                return; // Don't proceed to show main menu if in test
            }

            if (session.IsAuthenticated)
            {
                messages.Add("Добро пожаловать! Выберите действие:");
                keyboardToShow = MainCommandKeyboard;
                session.CurrentState = UserCurrentState.MainMenu;
            }
            else
            {
                messages.Add("Welcome to Omnieye Certification Bot!");
                messages.Add("Please use /login <password> to access content.");
                // No keyboard for unauthenticated /start here, just info.
            }
            await SendCombinedMessages(botClient, chatId, messages, ct, keyboardToShow);
        }

        static async Task HandleHelpCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            var messages = new List<string> { "Available commands:" };
            IReplyMarkup? currentKeyboard = null;

            if (session.CurrentState == UserCurrentState.TakingTest && session.ActiveTestId.HasValue) { /* ... */ }
            else if (!session.IsAuthenticated) { /* ... */ }
            else
            {
                messages.Add("/logout - Log out from the bot");
                messages.Add("--- Используйте кнопки меню ---");
                messages.Add("📘 Уроки - Просмотр учебных материалов (новая система)");
                messages.Add("🧪 Тесты - Доступные тесты (старая система)");
                // ... other commands
                currentKeyboard = MainCommandKeyboard;
            }
            messages.Add("/help - Show this help message");
            await SendCombinedMessages(botClient, chatId, messages, ct, currentKeyboard);
        }

        // static async Task HandleCoursesCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct) { /* Superseded */ }
        // static async Task HandleLessonCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, string? argument, CancellationToken ct) { /* Superseded */ }

        static async Task HandleTestCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, string? argument, CancellationToken ct)
        {
             if (session.CurrentState == UserCurrentState.TakingTest && session.ActiveTestId.HasValue) { /* ... */ return; }
            if (string.IsNullOrWhiteSpace(argument) || !int.TryParse(argument, out int testIdToStart) || testIdToStart <= 0)
            {
                await HandleTestsListAsync(botClient, session, chatId, ct); // Show list if no valid ID
                await botClient.SendTextMessageAsync(chatId, "Чтобы начать конкретный тест командой, введите /test <номер_теста>.", cancellationToken: ct);
                return;
            }
            await StartActualTestAsync(botClient, session, chatId, testIdToStart, ct);
        }

        static List<Lesson> GetAvailableLessonsForUserLevel(int userProfileLevel, IEnumerable<Lesson> lessons) // Omnieye.Bot.CoreModels.Lesson
        {
            if (userProfileLevel < 3) return lessons.Where(l => l.Level == LessonLevel.Beginner).ToList();
            else if (userProfileLevel < 6) return lessons.Where(l => l.Level == LessonLevel.Beginner || l.Level == LessonLevel.Intermediate).ToList();
            else return lessons.ToList();
        }

        static string GetLessonDifficultyIcon(LessonLevel level) => level switch { LessonLevel.Beginner => "🟢", LessonLevel.Intermediate => "🟡", LessonLevel.Advanced => "🔴", _ => "⚪" };
        static string GetDifficultyIcon(TestDifficulty difficulty) => difficulty switch { TestDifficulty.Easy => "🟢", TestDifficulty.Medium => "🟡", TestDifficulty.Hard => "🔴", _ => "⚪" };

        static async Task HandleLessonsListAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct) // Old lesson list
        {
            if (session.CurrentState == UserCurrentState.TakingTest && session.ActiveTestId.HasValue) { /* ... */ return; }
            var lessonsToShow = GetAvailableLessonsForUserLevel(session.Profile.Level, allLessonsData); // allLessonsData is List<Omnieye.Bot.CoreModels.Lesson>
            if (!lessonsToShow.Any()) { /* ... */ return; }
            session.LastShownLessonTitles = lessonsToShow.Select(l => l.Title).ToList();
            var messageBuilder = new StringBuilder("Доступные уроки (старая система):\n\n");
            foreach (var lesson in lessonsToShow) { /* ... */ }
            messageBuilder.AppendLine("👉 Напиши точное название урока из списка, чтобы открыть его.");
            await SendLongMessageAsync(botClient, chatId, messageBuilder.ToString(), ct, MainCommandKeyboard, parseMode: ParseMode.Markdown);
            session.CurrentState = UserCurrentState.ViewingLessonList;
        }

        static async Task HandleLessonContentAsync(ITelegramBotClient botClient, UserSession session, long chatId, Lesson lessonToShow, CancellationToken ct) // Omnieye.Bot.CoreModels.Lesson
        {
            if (lessonToShow == null) { /* ... */ return; }
            var icon = GetLessonDifficultyIcon(lessonToShow.Level);
            string header = $"{icon} *{lessonToShow.Title}*\n\n";
            await SendLongMessageAsync(botClient, chatId, header + lessonToShow.Content, ct, LessonDetailKeyboard, parseMode: ParseMode.Markdown);
            session.CurrentState = UserCurrentState.ViewingLessonDetail;
            session.ViewingItemId = null;
            session.ViewingLessonTitle = lessonToShow.Title;
        }

        static async Task HandleTestsListAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct) // Old test list
        {
            if (session.CurrentState == UserCurrentState.TakingTest && session.ActiveTestId.HasValue) { /* ... */ return; }
            var userLevel = session.Profile.Level;
            List<TestData> testsToList; // Omnieye.Bot.CoreModels.TestData
            if (userLevel < 3) testsToList = activeTestsData.Values.Where(t => t.Difficulty == TestDifficulty.Easy).ToList();
            else if (userLevel < 6) testsToList = activeTestsData.Values.Where(t => t.Difficulty != TestDifficulty.Hard).ToList();
            else testsToList = activeTestsData.Values.ToList();
            if (!testsToList.Any()) { /* ... */ return; }
            session.LastShownTestList = testsToList;
            var messageBuilder = new StringBuilder("Доступные тесты (старая система):\n");
            for (int i = 0; i < testsToList.Count; i++) { /* ... */ }
            messageBuilder.AppendLine("\nОтправьте номер теста для просмотра информации и начала.");
            await SendLongMessageAsync(botClient, chatId, messageBuilder.ToString(), ct, MainCommandKeyboard);
            session.CurrentState = UserCurrentState.ViewingTestList;
        }

        static async Task HandleTestDetailAsync(ITelegramBotClient botClient, UserSession session, long chatId, int testId, CancellationToken ct) // Old test detail
        {
            if (activeTestsData.TryGetValue(testId, out var testData)) { /* ... */ } else { /* ... */ }
            session.CurrentState = UserCurrentState.ViewingTestDetail;
            session.ViewingItemId = testId;
        }

        static async Task StartActualTestAsync(ITelegramBotClient botClient, UserSession session, long chatId, int testId, CancellationToken ct) // Old test start
        {
            if (!activeTestsData.TryGetValue(testId, out var testToStart)) { /* ... */ return; }
            if (testToStart.Questions == null || !testToStart.Questions.Any()) { /* ... */ return; }
            session.StartNewTest(testId); // This method is on Omnieye.Bot.States.UserSession
            await botClient.SendTextMessageAsync(chatId, $"Начинаем тест: \"{testToStart.TestName}\"", cancellationToken: ct, replyMarkup: new ReplyKeyboardRemove());
            await DisplayCurrentTestQuestionAsync(botClient, session, chatId, ct);
        }

        static async Task DisplayCurrentTestQuestionAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct) // Old test display question
        {
            if (!session.ActiveTestId.HasValue || !activeTestsData.TryGetValue(session.ActiveTestId.Value, out var currentTestData)) { /* ... */ return; }
            if (session.CurrentQuestionIndex >= currentTestData.Questions.Count) { /* ... */ return; }
            QuestionData question = currentTestData.Questions[session.CurrentQuestionIndex];
            var keyboardButtons = question.Options.Select(option => new KeyboardButton(option)).ToArray();
            var replyKeyboardMarkup = new ReplyKeyboardMarkup(keyboardButtons.Select(kb => new[] { kb })) { ResizeKeyboard = true, OneTimeKeyboard = true };
            string questionMessage = $"Вопрос {session.CurrentQuestionIndex + 1} из {currentTestData.Questions.Count}:\n\n{question.Text}";
            await botClient.SendTextMessageAsync(chatId, questionMessage, replyMarkup: replyKeyboardMarkup, cancellationToken: ct);
        }

        static async Task HandleStopTestCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            bool testWasStopped = false;
            if (session.ActiveTestId.HasValue && session.CurrentState == UserCurrentState.TakingTest)
            {
                var testName = activeTestsData.TryGetValue(session.ActiveTestId.Value, out var testData) ? testData.TestName : "текущий";
                session.EndCurrentTest(); // Resets test state
                await botClient.SendTextMessageAsync(chatId, $"Тест \"{testName}\" остановлен. Ваш прогресс не сохранен.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                testWasStopped = true;
            }
            if (!testWasStopped) { /* ... */ }
        }

        static async Task HandleHistoryAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            if (session.CurrentState == UserCurrentState.TakingTest && session.ActiveTestId.HasValue) { /* ... */ return; }
            if (session.TestHistory == null || !session.TestHistory.Any()) { /* ... */ } else { /* ... */ }
            session.CurrentState = UserCurrentState.MainMenu;
        }

        static async Task HandleProfileAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            if (session.CurrentState == UserCurrentState.TakingTest && session.ActiveTestId.HasValue) { /* ... */ return; }
            var profile = session.Profile;
            if (profile.UserId == 0 && session.UserId != 0) profile.UserId = session.UserId;
            string name = !string.IsNullOrWhiteSpace(profile.Name) ? profile.Name : $"User {profile.UserId}";
            var profileTextBuilder = new StringBuilder(); /* ... */
            await botClient.SendTextMessageAsync(chatId, profileTextBuilder.ToString(), parseMode: ParseMode.Markdown, replyMarkup: MainCommandKeyboard, cancellationToken: ct);
            session.CurrentState = UserCurrentState.MainMenu;
        }

        static async Task SendLongMessageAsync(ITelegramBotClient botClient, long chatId, string message, CancellationToken cancellationToken, IReplyMarkup? replyMarkup = null, ParseMode? parseMode = null, int chunkSize = 4000)
        {
            if (string.IsNullOrEmpty(message)) return;
            var chunks = SplitMessage(message, chunkSize);
            for (int i = 0; i < chunks.Count; i++)
            {
                bool isLastChunk = i == chunks.Count - 1;
                // Only send replyMarkup with the last chunk
                await botClient.SendTextMessageAsync(chatId, chunks[i],
                    replyMarkup: isLastChunk ? replyMarkup : null,
                    parseMode: parseMode, // Apply parseMode to all chunks if specified
                    cancellationToken: cancellationToken);
                if (!isLastChunk) await Task.Delay(200, cancellationToken); // Small delay between chunks
            }
        }

        static async Task SendCombinedMessages(ITelegramBotClient botClient, long chatId, List<string> messages, CancellationToken cancellationToken, IReplyMarkup? replyMarkup = null, ParseMode? parseMode = null)
        {
            string combined = string.Join("\n", messages);
            await SendLongMessageAsync(botClient, chatId, combined, cancellationToken, replyMarkup, parseMode: parseMode);
        }

        static async Task ShowLeaderboardAsync(ITelegramBotClient botClient, UserSession currentSession, long chatId, CancellationToken ct)
        {
            if (currentSession.CurrentState == UserCurrentState.TakingTest && currentSession.ActiveTestId.HasValue) { /* ... */ return; }
            var allUserProfiles = _userSessionService.GetAllUserProfiles();
            if (allUserProfiles == null || !allUserProfiles.Any()) { /* ... */ return; }
            var topUsers = allUserProfiles.OrderByDescending(p => p.TotalCorrectAnswers).ThenBy(p => p.RegisteredAt).Take(10).ToList();
            if (!topUsers.Any()) { /* ... */ return; }
            var leaderboardText = new StringBuilder("🏆 Топ 10 пользователей:\n"); /* ... */
            await SendLongMessageAsync(botClient, chatId, leaderboardText.ToString(), ct, MainCommandKeyboard);
            currentSession.CurrentState = UserCurrentState.MainMenu;
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
        static async Task ShowNextFlashcardAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            if (session.FlashcardQueue == null || session.FlashcardQueue.Count == 0) { /* ... */ return; }
            var card = session.FlashcardQueue.Dequeue();
            session.CurrentFlashcard = card;
            await botClient.SendTextMessageAsync(chatId, $"❓ {card.Question}", replyMarkup: FlashcardQuestionKeyboard, cancellationToken: ct);
            session.CurrentState = UserCurrentState.ReviewingFlashcards;
        }
        static async Task StartFlashcardSessionAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            if (session.CurrentState == UserCurrentState.TakingTest && session.ActiveTestId.HasValue) { /* ... */ return; }
            var flashcardsForUser = GetFlashcardsByLevel(session.Profile.Level, allFlashcardsData);
            if (flashcardsForUser == null || !flashcardsForUser.Any()) { /* ... */ return; }
            var random = new Random();
            session.FlashcardQueue = new Queue<Flashcard>(flashcardsForUser.OrderBy(x => random.Next()));
            session.CurrentFlashcard = null;
            await botClient.SendTextMessageAsync(chatId, "Начинаем сессию флеш-карточек!", cancellationToken: ct, replyMarkup: new ReplyKeyboardRemove());
            await ShowNextFlashcardAsync(botClient, session, chatId, ct);
        }
        static List<Flashcard> GetFlashcardsByLevel(int userProfileLevel, IEnumerable<Flashcard> allFlashcards)
        {
            if (userProfileLevel < 3) return allFlashcards.Where(f => f.Level == LessonLevel.Beginner).ToList();
            else if (userProfileLevel < 6) return allFlashcards.Where(f => f.Level == LessonLevel.Beginner || f.Level == LessonLevel.Intermediate).ToList();
            else return allFlashcards.ToList();
        }

    } // End of Program class

    public static class AuthorizationService // Remains as is
    {
        private const string HardcodedPassword = "omni_password123";
        public static bool Authenticate(long userId, string? password, Omnieye.Bot.Services.UserSessionService sessionService)
        {
            if (password == HardcodedPassword) { sessionService.UpdateUserAuthentication(userId, true); return true; }
            return false;
        }
        public static bool CheckAuthentication(long userId, Omnieye.Bot.Services.UserSessionService sessionService)
        {
            return sessionService.GetUserSession(userId).IsAuthenticated;
        }
        public static void Logout(long userId, Omnieye.Bot.Services.UserSessionService sessionService)
        {
            sessionService.UpdateUserAuthentication(userId, false);
            sessionService.GetUserSession(userId).EndCurrentTest();
        }
    }
}
