# Omnieye Certification Bot

The Omnieye Certification Bot is a Telegram bot designed to provide access to educational materials and tests for system and network administration certifications.

## Project Overview

This project aims to create an educational platform consisting of:
-   **Uчебные модули:** Theory, practice, and tests for different admin levels (Junior, System, Senior).
-   **Веб-сайт:** (To be detailed further, hosted on GitHub Pages) For accessing course materials.
-   **Telegram-бот:** (This component) For interactive learning, taking tests, and getting information.
-   **Система аутентификации:** Simple password-based authentication within the bot.
-   **Формат материалов:** Markdown for theory/practice, JSON for tests.

## Bot Features

-   **User Authentication:** Secure access to bot features using `/login <password>` and `/logout`.
-   **Course Material Access:**
    -   `/lesson <number>`: Displays theory and practice materials for the specified lesson number of the Junior Admin course.
-   **Interactive Testing:**
    -   `/test <number>`: Starts an interactive test session for the Junior Admin course (currently, the argument is noted but a single test loads). Users answer questions by sending the number of their chosen option.
    -   `/stoptest`: Allows users to stop an ongoing test.
-   **Command Keyboard:** After successful login, a custom keyboard with main commands ("📘 Уроки", "🧪 Тесты", "🔐 Выйти") is displayed for easy access.
-   **Navigation & Help:**
    -   `/start`: Displays a welcome message and initial instructions.
    -   `/help`: Provides a list of available commands based on authentication and test status.
    -   `/courses`: Lists available courses (currently focused on Junior Admin).
-   **Session Persistence:** User authentication and active test states are saved and restored across bot restarts.

## Setup and Running the Bot

### Prerequisites

1.  **.NET 8 SDK:** Ensure you have the .NET 8 SDK installed.
2.  **Telegram Bot Token:** You need a token for your Telegram bot, obtained from [BotFather](https://t.me/botfather).

### Configuration

1.  **Bot Token:** The bot token is required to run the bot. It's recommended to set this as an environment variable:
    ```bash
    export OMNIEYE_BOT_TOKEN="YOUR_ACTUAL_BOT_TOKEN"
    ```
    Alternatively, you can directly replace the placeholder in `bot/Program.cs` (not recommended for production or shared code).

### Running Locally

1.  **Clone the repository (if applicable).**
2.  **Navigate to the bot directory:**
    ```bash
    cd path/to/your/project/bot
    ```
3.  **Restore dependencies:**
    ```bash
    dotnet restore
    ```
4.  **Run the bot:**
    ```bash
    dotnet run
    ```
    The bot will connect to Telegram and start listening for messages. You should see output in the console indicating it has started, e.g., "Bot @YourBotUsername started and listening for messages."

### Data Persistence

-   User sessions (authentication status, current test state) are saved in a `user_sessions.json` file. This file is automatically created in a `data` subdirectory within the bot's execution directory (e.g., `bot/bin/Debug/net8.0/data/user_sessions.json`).
-   Course materials are loaded from the `materials/junior_admin` directory relative to the bot's execution path.

## Available Bot Commands

The bot understands the following commands. Some commands are only available after authentication.

### General Commands
-   `/start`: Shows a welcome message and initial instructions.
-   `/help`: Shows a list of available commands and how to use them.
-   `/login <password>`: Authenticates you to use the bot. (The default password for initial setup is `omni_password123`).

### Authenticated User Commands
*(Requires successful `/login`)*

After logging in, a command keyboard will appear with quick actions:
-   **"📘 Уроки"**: Displays a list of available lessons. Send the number of a lesson to view its details.
-   **"🧪 Тесты"**: Displays a list of available tests. Send the number of a test to view its details.
-   **"🔐 Выйти"**: Logs you out and removes the keyboard.

When viewing lesson details, a "Назад" button will appear to return to the lesson list.
When viewing test details, "Начать тест" (placeholder for now) and "Назад" buttons will appear.

You can also use the following slash commands:
-   `/logout`: Logs you out of the bot (and removes the keyboard).
-   `/courses`: Lists available course levels. (Equivalent to "📘 Уроки" button action)
-   `/lesson <number>`: Manually retrieves and displays the theory and practice content for the specified lesson number (note: this is the old way, primarily for direct access if needed. The keyboard flow is preferred).
    *   Example: `/lesson 1`
-   `/test <test_id_or_lesson_number>`: Manually starts an interactive test (note: this is the old way, primarily for direct access if needed. The keyboard flow is preferred for viewing details first).
    *   Example: `/test 1`
    *   During a test, simply send the number corresponding to your chosen answer.
-   `/stoptest`: If you are in an active test, this command will stop it. Your progress for that test attempt will not be saved.

## Project Structure Overview

```
.
├── bot/                    # C# Telegram Bot source code
│   ├── Properties/
│   ├── Models/             # C# classes for Test (TestQuestion, Test, UserTestState)
│   ├── Services/           # Services (Authorization, TestLoader, UserSessionService)
│   ├── States/             # UserSession class
│   ├── MaterialLoader.cs   # Loads Markdown course materials
│   ├── Omnieye.Bot.csproj  # Project file
│   ├── Program.cs          # Main application entry point, Telegram client logic
│   └── ...                 # Other .cs files
├── docs/                   # HTML/CSS for GitHub Pages website
│   ├── index.html
│   └── ...
├── materials/              # Course content
│   └── junior_admin/
│       ├── theory_chapter1.md
│       ├── practice_assignment1.md
│       └── tests_junior_admin.json
└── README.md               # This file
```

## Future Enhancements
*(This section can be expanded as the project grows)*
-   Implementation of `System Administrator` and `Senior Architect` course levels.
-   More detailed website content and dynamic loading of lessons.
-   Advanced Telegram bot features (inline keyboards for tests, progress tracking, reminders).
-   More robust error handling and logging.
-   CI/CD for website deployment.

## Contributing
(Details on how to contribute can be added here if the project becomes open source).
```
