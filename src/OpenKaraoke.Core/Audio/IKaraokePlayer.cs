namespace OpenKaraoke.Core.Audio;

/// <summary>
/// Abstraction over the audio engine so view models and tests never depend on NAudio directly.
/// </summary>
public interface IKaraokePlayer : IDisposable
{
    /// <summary>Raised (on the audio thread) when the current track plays to its natural end.</summary>
    event EventHandler? PlaybackEnded;

    /// <summary>True when a track has been opened and is ready to play.</summary>
    bool IsOpen { get; }

    PlayerState State { get; }

    /// <summary>Total length of the opened track.</summary>
    TimeSpan Duration { get; }

    /// <summary>Current playback position measured on the source timeline.</summary>
    TimeSpan Position { get; }

    /// <summary>Key shift in semitones, clamped to [-6, 6].</summary>
    int KeySemitones { get; set; }

    /// <summary>Playback tempo as a ratio (1.0 = normal), clamped to [0.8, 1.2].</summary>
    double Tempo { get; set; }

    /// <summary>Output volume, clamped to [0, 1].</summary>
    float Volume { get; set; }

    /// <summary>Opens a local audio file (m4a/mp3 via Media Foundation). Returns false when the file cannot be decoded.</summary>
    Task<bool> OpenAsync(string filePath);

    /// <summary>Starts or resumes playback. Returns false when no track/audio device is available.</summary>
    bool Play();

    void Pause();

    /// <summary>Stops playback and rewinds to the start of the track.</summary>
    void Stop();

    /// <summary>Moves playback to the given position on the source timeline.</summary>
    void Seek(TimeSpan position);
}
