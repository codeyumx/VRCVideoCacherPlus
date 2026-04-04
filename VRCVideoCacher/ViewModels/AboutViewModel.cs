using CodingSeb.Localization;

namespace VRCVideoCacher.ViewModels;

public class AboutViewModel : ViewModelBase
{
    public string Version { get; }
    public string PlusAuthor { get; } = "VRCVideoCacherPlus by codeyumx";
    public string CreatedBy { get; }

    public AboutViewModel()
    {
        Version = VRCVideoCacher.Program.Version;
        CreatedBy = $"{VRCVideoCacher.Program.Creator_Elly}, {VRCVideoCacher.Program.Creator_Natsumi}, {VRCVideoCacher.Program.Creator_Haxy}, {VRCVideoCacher.Program.Creator_Hauskaz}, {VRCVideoCacher.Program.Creator_DubyaDude}";
    }
}
