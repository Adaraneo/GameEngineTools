using Microsoft.Win32;

namespace LogsResolver.Services;

public sealed class FolderPickerService
{
    public string? PickFolder(string? title = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title ?? "Select Characters logs folder, logs folder, or repository root"
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
