using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Jeek.Avalonia.Localization;
using VRCVideoCacher.Services;

namespace VRCVideoCacher.ViewModels;

public partial class AboutViewModel : ViewModelBase
{
    public string Version { get; }
    public string PlusAuthor { get; } = "VRCVideoCacherPlus by codeyumx";
    public string CreatedBy { get; }
    public StatsViewModel Stats { get; } = new();

    public AboutViewModel()
    {
        Version = VRCVideoCacher.Program.Version;
        CreatedBy = Localizer.Get("CreatedBy") + $" {VRCVideoCacher.Program.Creator_Elly}, {VRCVideoCacher.Program.Creator_Natsumi}, {VRCVideoCacher.Program.Creator_Haxy}, {VRCVideoCacher.Program.Creator_Hauskaz}, {VRCVideoCacher.Program.Creator_DubyaDude}";
    }
}
