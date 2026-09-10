namespace OpenKaraoke.Core.Audio;

/// <summary>
/// Shared limits for the key/tempo controls, so every backend clamps identically.
/// </summary>
public static class AudioRanges
{
    public const int MinKeySemitones = -6;
    public const int MaxKeySemitones = 6;
    public const double MinTempo = 0.8;
    public const double MaxTempo = 1.2;
}
