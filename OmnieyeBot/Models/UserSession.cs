namespace OmnieyeBot.Models
{
    // This class was previously defined in Services/UserSessionService.cs
    public class UserSession
    {
        public string CurrentModuleId { get; set; }
        // Potentially other session-specific data can be added here,
        // for example, to improve "Back" button behavior:
        // public ViewState CurrentView { get; set; }
    }

    // public enum ViewState
    // {
    //     MainMenu,
    //     ModuleList,
    //     LessonList,
    //     LessonContent,
    //     // Add other views as needed
    // }
}
