using Microsoft.Win32;

namespace LogsResolver.Services;

public sealed class FolderPickerService
{
    public string? PickFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Characters logs folder, logs folder, or repository root"
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
