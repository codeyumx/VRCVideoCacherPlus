using Avalonia.Controls;
using Avalonia.Interactivity;

namespace VRCVideoCacher.Views;

public partial class PopupWindow : Window
{
    public bool Confirmed { get; private set; }

    private string? _folderPath;

    public PopupWindow() : this(string.Empty)
    {
    }

    public PopupWindow(string error)
    {
        InitializeComponent();
        this.FindControl<TextBlock>("ErrorTextBlock")!.Text = error;
    }

    public void SetFolderHint(string hintLabel, string folderPath)
    {
        _folderPath = folderPath;
        this.FindControl<TextBlock>("FolderHintLabel")!.Text = hintLabel;
        this.FindControl<TextBlock>("FolderPathText")!.Text = folderPath;
        this.FindControl<Border>("FolderHintBorder")!.IsVisible = true;
    }

    public static PopupWindow CreateConfirm(string message, string confirmLabel = "Yes", string cancelLabel = "No")
    {
        var w = new PopupWindow(message);
        var ok = w.FindControl<Button>("OkButton")!;
        var cancel = w.FindControl<Button>("CancelButton")!;
        ok.Content = confirmLabel;
        cancel.Content = cancelLabel;
        cancel.IsVisible = true;
        return w;
    }

    private void FolderPathButton_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_folderPath)) return;
        if (OperatingSystem.IsWindows())
            System.Diagnostics.Process.Start("explorer.exe", _folderPath);
        else if (OperatingSystem.IsLinux())
            System.Diagnostics.Process.Start("xdg-open", _folderPath);
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        this.Close();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        this.Close();
    }
}
