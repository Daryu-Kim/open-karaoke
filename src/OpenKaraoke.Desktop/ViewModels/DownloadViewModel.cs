using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKaraoke.Core.Download;
using OpenKaraoke.Core.Library;
using OpenKaraoke.Core.Search;

namespace OpenKaraoke.Desktop.ViewModels;

/// <summary>State machine for one download dialog session.</summary>
public enum DownloadPhase
{
    Ready,
    Working,
    Success,
    Failed,
}

/// <summary>
/// Downloads the MR audio of a chosen search result into the songs folder, then stores it in
/// the SQLite library with the metadata the owner confirms (title/artist/TJ 번호).
/// </summary>
public partial class DownloadViewModel : ObservableObject
{
    private readonly IYtDlpRunner _runner;
    private readonly ISongLibraryStore _store;
    private readonly string _outputDirectory;
    private readonly CancellationTokenSource _cts = new();

    public DownloadViewModel(
        IYtDlpRunner runner,
        ISongLibraryStore store,
        string outputDirectory,
        SearchResultItemViewModel source)
    {
        _runner = runner;
        _store = store;
        _outputDirectory = outputDirectory;
        VideoId = source.VideoId;
        SongTitle = source.Title;
        Artist = source.ChannelTitle;
        ThumbnailUrl = source.ThumbnailUrl;
        DurationText = source.DurationText;
    }

    public string VideoId { get; }
    public string ThumbnailUrl { get; }
    public string DurationText { get; }

    /// <summary>Becomes non-null after a successful download; the caller stores/refreshes the library.</summary>
    public SongRecord? SavedSong { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(CanStart), nameof(IsFailed), nameof(IsSuccess))]
    private DownloadPhase _phase = DownloadPhase.Ready;

    public bool IsBusy => Phase == DownloadPhase.Working;
    public bool CanStart => Phase is DownloadPhase.Ready or DownloadPhase.Failed;

    /// <summary>Avalonia styles the status line through classes instead of WPF DataTriggers.</summary>
    public bool IsFailed => Phase == DownloadPhase.Failed;

    /// <summary>Avalonia styles the status line through classes instead of WPF DataTriggers.</summary>
    public bool IsSuccess => Phase == DownloadPhase.Success;

    [ObservableProperty]
    private string _songTitle = string.Empty;

    [ObservableProperty]
    private string _artist = string.Empty;

    [ObservableProperty]
    private string _tjNumber = string.Empty;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _statusText = "제목과 가수를 확인한 뒤 다운로드를 눌러 주세요.";

    /// <summary>Progress text shown under the bar, e.g. "37%".</summary>
    public string ProgressText => $"{ProgressPercent:0}%";

    partial void OnProgressPercentChanged(double value) => OnPropertyChanged(nameof(ProgressText));

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartDownloadAsync()
    {
        string title = SongTitle.Trim();
        string artist = Artist.Trim();

        if (title.Length == 0 || artist.Length == 0)
        {
            StatusText = "곡 제목과 가수명을 입력해 주세요.";
            Phase = DownloadPhase.Failed;
            return;
        }

        Phase = DownloadPhase.Working;
        ProgressPercent = 0;
        StatusText = "영상을 받는 중입니다...";

        try
        {
            var progress = new Progress<double>(p => ProgressPercent = p);
            YtDlpDownloadResult result =
                await _runner.DownloadAudioAsync(VideoId, _outputDirectory, progress, _cts.Token);

            if (!result.Success || result.OutputFilePath == null)
            {
                StatusText = result.ErrorMessage ?? "다운로드에 실패했습니다.";
                Phase = DownloadPhase.Failed;
                return;
            }

            string? tj = string.IsNullOrWhiteSpace(TjNumber) ? null : TjNumber.Trim();
            long fileSize = TryGetFileSize(result.OutputFilePath);

            SongRecord saved = await _store.AddOrUpdateAsync(new SongRecord
            {
                VideoId = VideoId,
                Title = title,
                Artist = artist,
                TjNumber = tj,
                LocalPath = result.OutputFilePath,
                ThumbnailUrl = string.IsNullOrWhiteSpace(ThumbnailUrl) ? null : ThumbnailUrl,
                FileSizeBytes = fileSize,
                AddedAtUtc = DateTime.UtcNow,
            });

            SavedSong = saved;
            ProgressPercent = 100;
            StatusText = $"“{title}” 저장 완료!";
            Phase = DownloadPhase.Success;
        }
        catch (OperationCanceledException)
        {
            StatusText = "다운로드를 취소했습니다.";
            Phase = DownloadPhase.Ready;
        }
        catch (Exception)
        {
            StatusText = "다운로드 중 오류가 발생했습니다. 잠시 후 다시 시도해 주세요.";
            Phase = DownloadPhase.Failed;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts.Cancel();
        StatusText = "다운로드를 취소하는 중입니다...";
    }

    private static long TryGetFileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
