using Avalonia.Controls;
using Avalonia.Platform.Storage;
using VRCVideoCacher.ViewModels;

namespace VRCVideoCacher.Views;

public partial class CookieSetupWindow : Window
{
    public CookieSetupWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is CookieSetupViewModel vm)
        {
            vm.PickBrowserExecutable = async () =>
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select Browser",
                    AllowMultiple = false,
                });
                return files.Count > 0 ? files[0].TryGetLocalPath() : null;
            };
        }
    }
}
