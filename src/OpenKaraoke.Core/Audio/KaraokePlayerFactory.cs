namespace OpenKaraoke.Core.Audio;

/// <summary>
/// Chooses the audio backend for the current platform: Media Foundation + WaveOut on Windows,
/// ffmpeg + OpenAL Soft everywhere else (Linux, macOS).
/// </summary>
public static class KaraokePlayerFactory
{
    public static IKaraokePlayer Create()
        => OperatingSystem.IsWindows() ? new KaraokeAudioPlayer() : new OpenAlAudioPlayer();
}
