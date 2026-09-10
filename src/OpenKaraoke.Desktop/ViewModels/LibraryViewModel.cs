using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKaraoke.Core.Library;

namespace OpenKaraoke.Desktop.ViewModels;

/// <summary>
/// Backs the "내 라이브러리" panel: lists locally stored songs (title/artist/TJ 번호),
/// supports text filtering and row deletion. Persistence happens in the SQLite store.
/// </summary>
public partial class LibraryViewModel : ObservableObject
{
    private readonly ISongLibraryStore _store;
    private CancellationTokenSource? _filterDebounce;

    public ObservableCollection<SongItemViewModel> Songs { get; } = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private string _countText = "0곡";

    public bool HasSongs => Songs.Count > 0;

    public LibraryViewModel(ISongLibraryStore store)
    {
        _store = store;
        Songs.CollectionChanged += OnSongsChanged;
    }

    /// <summary>Rebuilds the list after typing pauses, so filtering feels instant while keeping queries cheap.</summary>
    partial void OnFilterTextChanged(string value)
    {
        _filterDebounce?.Cancel();
        var cts = new CancellationTokenSource();
        _filterDebounce = cts;
        _ = DebouncedLoadAsync(cts.Token);
    }

    private async Task DebouncedLoadAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(250, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        await LoadAsync();
    }

    private void OnSongsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasSongs));
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            List<SongRecord> songs = await _store.GetAllAsync(FilterText);
            Songs.Clear();
            foreach (SongRecord song in songs)
            {
                Songs.Add(new SongItemViewModel(song));
            }

            CountText = $"{Songs.Count}곡";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Deletes the row plus its audio file from disk. Returns false when nothing matched.</summary>
    public async Task<bool> DeleteSongAsync(SongItemViewModel item)
    {
        bool removed = await _store.DeleteAsync(item.Id);
        if (removed)
        {
            TryDeleteFile(item.LocalPath);
            Songs.Remove(item);
            CountText = $"{Songs.Count}곡";
        }

        return removed;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // File locked by another process; DB row is already gone.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above; keep the library consistent.
        }
    }
}
