using LogsResolver.Services;

namespace LogsResolver.ViewModels;

public sealed class RawFileViewModel : ViewModelBase
{
    private readonly RawFileService _rawFileService;
    private string? _filePath;
    private string? _content;
    private bool _isLoading;

    public RawFileViewModel(RawFileService rawFileService)
    {
        _rawFileService = rawFileService;
    }

    public string? FilePath
    {
        get => _filePath;
        private set => SetProperty(ref _filePath, value);
    }

    public string? Content
    {
        get => _content;
        private set => SetProperty(ref _content, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public async Task LoadAsync(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        FilePath = filePath;
        IsLoading = true;
        try
        {
            Content = await _rawFileService.ReadTextAsync(filePath).ConfigureAwait(true);
        }
        finally
        {
            IsLoading = false;
        }
    }
}
