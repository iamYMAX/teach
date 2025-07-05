public class CourseModule
{
    public string Id { get; set; }
    public string Title { get; set; }
    public System.Collections.Generic.List<Lesson> Lessons { get; set; } = new();
}
