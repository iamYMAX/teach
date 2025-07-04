using System;

namespace Omnieye.Bot.States
{
    public class UserProfile
    {
        public long UserId { get; set; }
        public string? Name { get; set; } // Nullable if not set
        public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

        public int TotalTestsTaken { get; set; } = 0;
        public int TotalCorrectAnswers { get; set; } = 0;

        // Calculated property for Level
        public int Level => TotalCorrectAnswers / 10;

        // Parameterless constructor for deserialization and default creation
        public UserProfile()
        {
            // Name can be null initially
            // RegisteredAt is set by default initializer
        }

        // Optional: Constructor for specific UserId initialization
        public UserProfile(long userId) : this()
        {
            UserId = userId;
        }
    }
}
