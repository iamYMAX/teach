using System.Collections.Generic;

namespace OmnieyeBot.Models
{
    public class CourseModule
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public List<Lesson> Lessons { get; set; } = new();
    }
}
