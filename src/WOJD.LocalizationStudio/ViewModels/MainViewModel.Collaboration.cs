using System.IO;
using WOJD.LocalizationStudio.Services;
namespace WOJD.LocalizationStudio.ViewModels;
public sealed partial class MainViewModel
{
    private string? _authorPath; private DateTime _authorStamp; private string _author = Environment.UserName;
    public string CurrentAuthor
    {
        get
        {
            try { var path = Path.Combine(ProjectDataDirectory, "collaboration.json"); var stamp = File.GetLastWriteTimeUtc(path);
                if (_authorPath != path || _authorStamp != stamp) { var state = ProjectMetadataService.Load<CollaborationState>(path, () => new()); CollaborationService.Validate(state); _author = state.Author; _authorPath = path; _authorStamp = stamp; }
                return _author;
            } catch (Exception e) { IssueLogService.Record(e.Message); return Environment.UserName; }
        }
    }
}
