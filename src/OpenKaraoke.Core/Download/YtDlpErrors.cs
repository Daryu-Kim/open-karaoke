namespace OpenKaraoke.Core.Download;

/// <summary>
/// Maps raw yt-dlp errors and tooling failures to short Korean messages that can be shown
/// directly in the karaoke UI.
/// </summary>
public static class YtDlpErrors
{
    public const string ToolMissingMessage =
        "yt-dlp가 없습니다. 앱 폴더나 PATH에 yt-dlp.exe를 설치한 후 다시 시도해 주세요.";

    public const string TimeoutOrCancelledMessage = "다운로드가 중단되었습니다.";

    public static string ToKorean(string? rawError, bool toolMissing)
    {
        if (toolMissing)
        {
            return ToolMissingMessage;
        }

        if (string.IsNullOrWhiteSpace(rawError))
        {
            return "다운로드 중 오류가 발생했습니다. 잠시 후 다시 시도해 주세요.";
        }

        string text = rawError.ToLowerInvariant();

        if (text.Contains("video unavailable") || text.Contains("private video") ||
            text.Contains("is private") || text.Contains("not available") ||
            text.Contains("has been removed") || text.Contains("copyright"))
        {
            return "재생할 수 없는 영상입니다. (삭제·비공개 또는 저작권 제한)";
        }

        if (text.Contains("sign in to confirm") || text.Contains("not a bot") ||
            text.Contains("age-restricted") || text.Contains("age"))
        {
            return "로그인·연령 확인이 필요한 영상입니다.";
        }

        if (text.Contains("timed out") || text.Contains("timeout") || text.Contains("network") ||
            text.Contains("connection") || text.Contains("unable to download") ||
            text.Contains("temporary") || text.Contains("403"))
        {
            return "네트워크 오류가 발생했습니다. 인터넷 연결을 확인하고 다시 시도해 주세요.";
        }

        if (text.Contains("does not exist") || text.Contains("not found") || text.Contains("video id"))
        {
            return "영상을 찾을 수 없습니다.";
        }

        if (text.Contains("permission") || text.Contains("access"))
        {
            return "영상에 접근할 수 없습니다.";
        }

        if (text.Contains("unsupported url") || text.Contains("no video"))
        {
            return "다운로드를 지원하지 않는 영상입니다.";
        }

        // Generic fallback keeps the original detail for debugging without leaking secrets.
        return $"다운로드에 실패했습니다. ({rawError})";
    }
}
