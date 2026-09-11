using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKaraoke.Core.Download;
using OpenKaraoke.Core.Library;
using OpenKaraoke.Core.Search;
using OpenKaraoke.Core.Video;
using OpenKaraoke.Desktop.Diagnostics;

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
/// Downloads the MR media of a chosen search result into the songs folder, then stores it in
/// the SQLite library with the metadata the owner confirms (title/artist/TJ 번호).
/// The second constructor re-downloads an existing audio-only song as a video, keeping its
/// metadata (라이브러리의 "영상 다시 받기").
/// </summary>
public partial class DownloadViewModel : ObservableObject
{
    private readonly IYtDlpRunner _runner;
    private readonly ISongLibraryStore _store;
    private readonly string _outputDirectory;
    private readonly string? _previousFilePath;
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

    /// <summary>Re-download mode: replaces the file of a song that has no video (audio only).</summary>
    public DownloadViewModel(
        IYtDlpRunner runner,
        ISongLibraryStore store,
        string outputDirectory,
        SongItemViewModel song)
    {
        _runner = runner;
        _store = store;
        _outputDirectory = outputDirectory;
        _previousFilePath = song.LocalPath;
        IsRedownload = true;
        VideoId = song.VideoId;
        SongTitle = song.Title;
        Artist = song.Artist;
        TjNumber = song.TjNumber ?? string.Empty;
        ThumbnailUrl = song.ThumbnailUrl;
        DurationText = song.DurationText;
        StatusText = "이 곡을 가사 영상(1080p)으로 다시 내려받습니다. 제목과 가수를 확인한 뒤 다시 받기를 눌러 주세요.";
    }

    public string VideoId { get; }
    public string ThumbnailUrl { get; }
    public string DurationText { get; }

    /// <summary>True when this session replaces the file of an existing library song.</summary>
    public bool IsRedownload { get; }

    /// <summary>Library file this session replaces; null for a plain first download.</summary>
    public string? PreviousFilePath => IsRedownload ? _previousFilePath : null;

    /// <summary>Dialog caption; differs for the 영상 다시 받기 flow.</summary>
    public string DialogTitle => IsRedownload ? "영상 다시 받기" : "노래 다운로드";

    /// <summary>Sub-title shown under the dialog caption.</summary>
    public string DescriptionText => IsRedownload
        ? "가사(영상)가 있는 파일로 다시 받아 라이브러리의 곡을 교체합니다."
        : "라이브러리에 저장할 곡 정보를 확인하고 다운로드하세요.";

    /// <summary>Primary button caption.</summary>
    public string StartButtonText => IsRedownload ? "다시 받기" : "다운로드";

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
                await _runner.DownloadMediaAsync(VideoId, _outputDirectory, progress, _cts.Token);

            if (!result.Success || result.OutputFilePath == null)
            {
                StatusText = result.ErrorMessage ?? "다운로드에 실패했습니다.";
                AppLog.Write($"[download] {VideoId} 실패: {result.ErrorMessage}");
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
            ReplacePreviousFile(result.OutputFilePath);
            StatusText = IsRedownload ? $"“{title}” 영상 다시 받기 완료!" : $"“{title}” 저장 완료!";
            Phase = DownloadPhase.Success;
        }
        catch (OperationCanceledException)
        {
            StatusText = "다운로드를 취소했습니다.";
            Phase = DownloadPhase.Ready;
        }
        catch (Exception ex)
        {
            AppLog.Write($"[download] {VideoId} 예외: {ex}");
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

    /// <summary>
    /// Removes the audio-only file that the new download replaced so the songs folder does not grow
    /// with dead files. A file still open by the player is left behind on purpose.
    /// </summary>
    private void ReplacePreviousFile(string newPath)
    {
        if (!IsRedownload
            || string.IsNullOrWhiteSpace(_previousFilePath)
            || string.Equals(_previousFilePath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            if (File.Exists(_previousFilePath))
            {
                File.Delete(_previousFilePath);
                FfmpegMediaProbe.Invalidate(_previousFilePath);
                AppLog.Write($"[download] 이전 음원 삭제: {_previousFilePath}");
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"[download] 이전 음원 삭제 실패({_previousFilePath}): {ex.Message}");
        }
    }

    private static long TryGetFileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex)
        {
            AppLog.Write($"[download] 파일 크기 확인 실패({path}): {ex.Message}");
            return 0;
        }
    }
}
