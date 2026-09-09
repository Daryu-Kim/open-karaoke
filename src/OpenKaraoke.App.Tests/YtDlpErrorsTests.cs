using OpenKaraoke.Core.Download;

namespace OpenKaraoke.App.Tests;

public class YtDlpErrorsTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void ToKorean_EmptyError_GivesGeneric(string? raw, bool toolMissing)
    {
        Assert.Equal("다운로드 중 오류가 발생했습니다. 잠시 후 다시 시도해 주세요.",
            YtDlpErrors.ToKorean(raw, toolMissing));
    }

    [Fact]
    public void ToKorean_ToolMissing_ExplainsInstallation()
    {
        Assert.Equal(YtDlpErrors.ToolMissingMessage, YtDlpErrors.ToKorean("anything", true));
    }

    [Theory]
    [InlineData("ERROR: Video unavailable", "재생할 수 없는 영상입니다. (삭제·비공개 또는 저작권 제한)")]
    [InlineData("This video is private", "재생할 수 없는 영상입니다. (삭제·비공개 또는 저작권 제한)")]
    [InlineData("Video has been removed for copyright", "재생할 수 없는 영상입니다. (삭제·비공개 또는 저작권 제한)")]
    [InlineData("Sign in to confirm you're not a bot", "로그인·연령 확인이 필요한 영상입니다.")]
    [InlineData("This video is age-restricted", "로그인·연령 확인이 필요한 영상입니다.")]
    [InlineData("network is unreachable", "네트워크 오류가 발생했습니다. 인터넷 연결을 확인하고 다시 시도해 주세요.")]
    [InlineData("request timed out", "네트워크 오류가 발생했습니다. 인터넷 연결을 확인하고 다시 시도해 주세요.")]
    [InlineData("Watch on YouTube does not exist", "영상을 찾을 수 없습니다.")]
    [InlineData("Unsupported URL", "다운로드를 지원하지 않는 영상입니다.")]
    public void ToKorean_KnownErrors_MappedToKorean(string raw, string expected)
    {
        Assert.Equal(expected, YtDlpErrors.ToKorean(raw, false));
    }

    [Fact]
    public void ToKorean_UnknownError_KeepsDetail()
    {
        string result = YtDlpErrors.ToKorean("some obscure failure", false);
        Assert.Contains("some obscure failure", result);
        Assert.StartsWith("다운로드에 실패했습니다.", result);
    }
}
