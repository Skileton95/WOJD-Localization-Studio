using System.IO;
using WOJD.LocalizationStudio.Services;
namespace WOJD.LocalizationStudio.ViewModels;
public sealed partial class MainViewModel
{
    public string CurrentAuthor
    {
        get { try { var state = ProjectMetadataService.Load<CollaborationState>(Path.Combine(ProjectDataDirectory, "collaboration.json"), () => new()); CollaborationService.Validate(state); return state.Author; } catch (Exception e) { IssueLogService.Record(e.Message); return Environment.UserName; } }
    }
}
