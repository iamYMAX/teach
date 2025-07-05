using System.Collections.Generic;
using Omnieye.Bot.States;
// Omnieye.Bot.CoreModels is the current namespace, so direct access to Lesson and Test (CoreModels.Test) is fine.
// using Omnieye.Bot.CoreModels; // For Lesson - Not strictly needed if types are in the same namespace.
// using Omnieye.Bot.Models;   // For Test - REMOVE THIS to avoid conflict

namespace Omnieye.Bot.CoreModels
{
    public class BotData
    {
        public List<UserProfile> Users { get; set; } = new();
        public List<Lesson> Lessons { get; set; } = new(); // Will resolve to CoreModels.Lesson
        public List<Test> Tests { get; set; } = new();   // Should now resolve to CoreModels.Test
    }
}
