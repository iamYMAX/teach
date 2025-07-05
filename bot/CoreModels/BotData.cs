using System.Collections.Generic;
using Omnieye.Bot.States;
using Omnieye.Bot.CoreModels; // For Lesson
using Omnieye.Bot.Models;   // For Test

namespace Omnieye.Bot.CoreModels
{
    public class BotData
    {
        public List<UserProfile> Users { get; set; } = new();
        public List<Lesson> Lessons { get; set; } = new();
        public List<Test> Tests { get; set; } = new();
    }
}
