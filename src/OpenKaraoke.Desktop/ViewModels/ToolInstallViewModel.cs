using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKaraoke.Core.Configuration;
using OpenKaraoke.Core.Tools;
using OpenKaraoke.Desktop.Diagnostics;

namespace OpenKaraoke.Desktop.ViewModels;

/// <summary>What the 도구 설치 dialog achieved, so the shell can react once it closes.</summary>
public sealed record ToolInstallOutcome(bool Installed, bool RestartRequired)
{
    public static readonly ToolInstallOutcome None = new(false, false);
}

/// <summary>One tool row: name, why it is needed, and its download state.</summary>
public partial class ToolRowViewModel : ObservableObject
{
    public ToolRowViewModel(ToolRequirement requirement, string targetPath)
    {
        Requirement = requirement;
        TargetPath = targetPath;
        _isInstalled = requirement.IsInstalled;
        _isSelected = requirement.IsRequired;
        _statusText = requirement.IsInstalled
            ? "이미 설치되어 있습니다."
            : requirement.IsRequired
                ? "설치되지 않았습니다."
                : "설치하지 않습니다.";
    }

    public ToolRequirement Requirement { get; }

    /// <summary>Where the automatic install would place the tool (next to the app).</summary>
    public string TargetPath { get; }

    public string DisplayName => Requirement.DisplayName;

    public string Purpose => Requirement.Purpose;

    /// <summary>필수 / 선택 badge text.</summary>
    public string BadgeText => Requirement.IsRequired ? "필수" : "선택";

    public bool IsRequired => Requirement.IsRequired;

    /// <summary>Optional tools can be ticked by the operator; required ones are always installed.</summary>
    public bool IsOptional => !Requirement.IsRequired;

    /// <summary>Whether the operator wants this optional tool downloaded.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Second line: download size, or the path actually in use once installed.</summary>
    public string DetailText => IsInstalled
        ? $"사용 중인 경로: {Requirement.ExpectedPath}"
        : $"내려받기 크기 {Requirement.DownloadSize} · 설치 위치: {TargetPath}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailText))]
    [NotifyPropertyChangedFor(nameof(CanSelect))]
    private bool _isInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSelect))]
    private bool _isBusy;

    /// <summary>A tool that is installed or being installed cannot be selected again.</summary>
    public bool CanSelect => !IsInstalled && !IsBusy;

    [ObservableProperty]
    private bool _isFailed;

    [ObservableProperty]
    private string _statusText;

    [ObservableProperty]
    private double _percent;

    [ObservableProperty]
    private bool _isIndeterminate = true;

    /// <summary>Mirrors the installer's progress report, including its Korean status message.</summary>
    public void ApplyProgress(ToolInstallProgress progress)
    {
        Percent = Math.Clamp(progress.Percent, 0, 100);
        IsIndeterminate = progress.Percent < 0;
        StatusText = progress.Message;
    }

    public void MarkInstalled()
    {
        IsInstalled = true;
        IsFailed = false;
        IsBusy = false;
        Percent = 100;
        IsIndeterminate = false;
        StatusText = $"설치 완료: {TargetPath}";
    }

    public void MarkFailed(string message)
    {
        IsInstalled = false;
        IsFailed = true;
        IsBusy = false;
        IsIndeterminate = false;
        StatusText = message;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (!IsInstalled && !IsBusy && !IsFailed)
        {
            StatusText = value ? "설치되지 않았습니다." : "설치하지 않습니다.";
        }
    }
}

/// <summary>
/// Backs the startup / 설정 dialog that offers to download the external tools (yt-dlp, ffmpeg)
/// into the app folder. Every string is Korean because the operator sees this directly.
/// </summary>
public partial class ToolInstallViewModel : ObservableObject
{
    private readonly ToolInstaller _installer;
    private readonly CancellationTokenSource _cancellation = new();
    private bool _installAttempted;

    public ToolInstallViewModel(
        ToolInstaller installer,
        bool showSkipOption = false,
        IReadOnlyList<ToolRequirement>? requirements = null)
    {
        _installer = installer;
        ShowSkipOption = showSkipOption;

        Rows = new ObservableCollection<ToolRowViewModel>(
            (requirements ?? ToolCatalog.Inspect())
                .Select(requirement => new ToolRowViewModel(requirement, installer.TargetPathFor(requirement.Kind))));

        foreach (ToolRowViewModel row in Rows)
        {
            row.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(ToolRowViewModel.IsSelected) && !_installAttempted)
                {
                    UpdatePendingSummary();
                }

                InstallCommand.NotifyCanExecuteChanged();
            };
        }

        UpdatePendingSummary();
        _skipNextTime = AppSettings.GetFlag(AppSettings.UiSection, AppSettings.SkipToolInstallPromptKey);
        EnvironmentNotice = BuildEnvironmentNotice();
    }

    public ObservableCollection<ToolRowViewModel> Rows { get; }

    /// <summary>Shown only on the startup prompt; the 설정 dialog entry point hides it.</summary>
    public bool ShowSkipOption { get; }

    /// <summary>Set when an environment variable overrides the tool paths the app installs to.</summary>
    public string? EnvironmentNotice { get; }

    public bool HasEnvironmentNotice => EnvironmentNotice is not null;

    /// <summary>True when at least one tool was installed successfully.</summary>
    public bool Installed { get; private set; }

    /// <summary>True when ffmpeg was installed, which the running playback engine cannot pick up.</summary>
    public bool RestartRequired { get; private set; }

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private bool _isFailed;

    [ObservableProperty]
    private bool _isSuccess;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _skipNextTime;

    /// <summary>Stores the "다시 묻지 않기" choice; called when the dialog closes.</summary>
    public void PersistSkipChoice()
    {
        if (!ShowSkipOption)
        {
            return;
        }

        AppSettings.SetFlag(AppSettings.UiSection, AppSettings.SkipToolInstallPromptKey, SkipNextTime);
    }

    private bool CanInstall() => !IsBusy && Rows.Any(row => !row.IsInstalled && row.IsSelected);

    /// <summary>Only the missing tools the operator ticked (plus every required one) get downloaded.</summary>
    private List<ToolRowViewModel> SelectedRows() =>
        Rows.Where(row => !row.IsInstalled && row.IsSelected).ToList();

    private void UpdatePendingSummary()
    {
        int missing = Rows.Count(row => !row.IsInstalled && row.IsSelected);
        Summary = missing == 0
            ? "필요한 도구가 모두 설치되어 있습니다."
            : $"{missing}개 도구를 자동으로 내려받아 설치할 수 있습니다.";
    }

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        IsBusy = true;
        _installAttempted = true;
        InstallCommand.NotifyCanExecuteChanged();

        List<string> failures = new();
        int installed = 0;

        foreach (ToolRowViewModel row in SelectedRows())
        {
            row.IsBusy = true;
            try
            {
                var progress = new Progress<ToolInstallProgress>(row.ApplyProgress);
                await _installer.InstallAsync(row.Requirement.Kind, progress, _cancellation.Token);

                row.MarkInstalled();
                installed++;
                Installed = true;
                RestartRequired |= row.Requirement.Kind == ToolKind.Ffmpeg && !OperatingSystem.IsWindows();
                AppLog.Write($"[tools] {row.DisplayName} 설치 완료: {_installer.TargetPathFor(row.Requirement.Kind)}");
            }
            catch (OperationCanceledException)
            {
                row.MarkFailed("설치를 취소했습니다.");
                AppLog.Write($"[tools] {row.DisplayName} 설치 취소");
                break;
            }
            catch (ToolInstallException ex)
            {
                failures.Add($"{row.DisplayName}: {ex.Message}");
                row.MarkFailed(ex.Message);
                AppLog.Write($"[tools] {row.DisplayName} 설치 실패: {ex.Message}");
            }
            catch (Exception ex)
            {
                failures.Add($"{row.DisplayName}: {ex.Message}");
                row.MarkFailed($"설치하지 못했습니다: {ex.Message}");
                AppLog.Write($"[tools] {row.DisplayName} 설치 실패: {ex}");
            }
        }

        IsBusy = false;
        InstallCommand.NotifyCanExecuteChanged();

        if (failures.Count > 0)
        {
            SetStatus($"일부 도구를 설치하지 못했습니다.\n{string.Join("\n", failures)}", failed: true);
        }
        else if (installed > 0)
        {
            SetStatus(
                RestartRequired
                    ? "설치가 끝났습니다. ffmpeg는 앱을 다시 시작한 뒤부터 재생에 적용됩니다."
                    : "설치가 끝났습니다. 이제 그대로 사용할 수 있습니다.",
                failed: false);
        }

        Summary = Rows.Where(row => row.IsSelected).All(row => row.IsInstalled)
            ? "필요한 도구가 모두 설치되었습니다."
            : $"설치하지 못한 도구가 남아 있습니다. ({Rows.Count(row => row.IsSelected && !row.IsInstalled)}개)";
    }

    [RelayCommand]
    private void Cancel()
    {
        _cancellation.Cancel();
        AppLog.Write("[tools] 설치를 취소합니다");
    }

    private void SetStatus(string text, bool failed)
    {
        StatusText = text;
        IsFailed = failed;
        IsSuccess = !failed;
    }

    private static string? BuildEnvironmentNotice()
    {
        string[] variables = [AppSettings.YtDlpPathEnvVar, AppSettings.FfmpegPathEnvVar];
        string[] active = variables
            .Where(variable => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)))
            .ToArray();

        return active.Length == 0
            ? null
            : $"환경 변수({string.Join(", ", active)})가 설정되어 있어 그 경로가 우선 사용됩니다. "
                + "자동 설치한 도구를 쓰려면 환경 변수를 해제해 주세요.";
    }
}
