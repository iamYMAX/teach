using System;
using System.IO;
using System.Threading.Tasks;

namespace Omnieye.Bot.Services
{
    public class AdminActivityLogger
    {
        private readonly string _logFilePath;
        private static readonly object _lock = new object(); // For thread-safe file writes

        public AdminActivityLogger(string dataDirectory = "Data")
        {
            if (string.IsNullOrEmpty(dataDirectory))
            {
                dataDirectory = "Data"; // Default if null or empty
            }
            // Ensure the directory path is combined correctly, especially if dataDirectory could be absolute.
            var logDirectory = Path.IsPathRooted(dataDirectory) ? dataDirectory : Path.Combine(Directory.GetCurrentDirectory(), dataDirectory);

            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }
            _logFilePath = Path.Combine(logDirectory, "admin_log.txt");
        }

        public void Log(long adminId, string action, string? details = null)
        {
            try
            {
                string logEntry = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC} | AdminID: {adminId} | Action: {action}";
                if (!string.IsNullOrWhiteSpace(details))
                {
                    logEntry += $" | Details: {details}";
                }
                logEntry += Environment.NewLine;

                lock (_lock) // Ensure only one thread writes to the file at a time
                {
                    File.AppendAllText(_logFilePath, logEntry);
                }
            }
            catch (Exception ex)
            {
                // Log error to console or another more robust logging system if available
                Console.WriteLine($"Failed to write to admin log: {ex.Message}");
                // Depending on policy, could re-throw or handle silently.
            }
        }

        public async Task LogAsync(long adminId, string action, string? details = null)
        {
            try
            {
                string logEntry = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC} | AdminID: {adminId} | Action: {action}";
                if (!string.IsNullOrWhiteSpace(details))
                {
                    logEntry += $" | Details: {details}";
                }
                logEntry += Environment.NewLine;

                // For async, consider a more sophisticated approach if high contention is expected,
                // but for typical bot admin actions, simple async append should be fine.
                // File.AppendAllTextAsync is not directly available in all .NET versions/targets in a simple way
                // that is as robust as sync File.AppendAllText with a lock.
                // Using Task.Run to offload the synchronous locked write.
                await Task.Run(() =>
                {
                    lock (_lock)
                    {
                        File.AppendAllText(_logFilePath, logEntry);
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to write to admin log asynchronously: {ex.Message}");
            }
        }
    }
}
