using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKaraoke.Core.Search;
using OpenKaraoke.Desktop.Diagnostics;

namespace OpenKaraoke.Desktop.ViewModels;

public enum SearchUiState
{
    Idle,
    Searching,
    Results,
    Empty,
    Error,
}

/// <summary>
/// Drives YouTube search from the header search box and exposes the result tiles for
/// the left content panel. All user-visible strings are Korean.
/// </summary>
public partial class SearchViewModel : ObservableObject
{
    private YoutubeSearchService _searchService;

    public ObservableCollection<SearchResultItemViewModel> Results { get; } = new();

    /// <summary>Raised when the owner presses "다운로드" on a search result tile.</summary>
    public event Action<SearchResultItemViewModel>? DownloadRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsSearching), nameof(HasResults),
        nameof(IsEmptyResult), nameof(HasError))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    private SearchUiState _state = SearchUiState.Idle;

    public bool IsIdle => State == SearchUiState.Idle;
    public bool IsSearching => State == SearchUiState.Searching;
    public bool HasResults => State == SearchUiState.Results;
    public bool IsEmptyResult => State == SearchUiState.Empty;
    public bool HasError => State == SearchUiState.Error;

    [ObservableProperty]
    private string _queryText = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _resultSummary;

    [ObservableProperty]
    private SearchResultItemViewModel? _selectedResult;

    public SearchViewModel(YoutubeSearchService searchService)
    {
        _searchService = searchService;
    }

    /// <summary>
    /// Swaps in a service built from a freshly saved API key so the change applies without
    /// a restart, dropping any results that were fetched with the previous key.
    /// </summary>
    public void RebindService(YoutubeSearchService searchService)
    {
        _searchService = searchService;
        Results.Clear();
        SelectedResult = null;
        ResultSummary = null;
        ErrorMessage = null;
        State = SearchUiState.Idle;
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        string query = QueryText.Trim();
        if (query.Length == 0)
        {
            ErrorMessage = "검색어를 입력해 주세요.";
            State = SearchUiState.Error;
            return;
        }

        if (IsSearching)
        {
            return;
        }

        State = SearchUiState.Searching;
        ErrorMessage = null;

        try
        {
            SearchResponse response = await _searchService.SearchAsync(query);

            Results.Clear();
            foreach (SearchVideoItem item in response.Items)
            {
                Results.Add(new SearchResultItemViewModel(item));
            }

            if (Results.Count == 0)
            {
                ResultSummary = $"“{response.QueryUsed}” 검색 결과가 없습니다";
                State = SearchUiState.Empty;
            }
            else
            {
                ResultSummary = $"“{response.QueryUsed}” 검색 결과 {Results.Count}곡";
                State = SearchUiState.Results;
            }
        }
        catch (SearchException ex)
        {
            AppLog.Write($"[search] \"{query}\" 실패: {ex.Message}");
            ErrorMessage = ex.Message;
            State = SearchUiState.Error;
        }
        catch (Exception ex)
        {
            AppLog.Write($"[search] \"{query}\" 예외: {ex}");
            ErrorMessage = "검색 중 오류가 발생했습니다. 잠시 후 다시 시도해 주세요.";
            State = SearchUiState.Error;
        }
    }

    [RelayCommand(CanExecute = nameof(HasResults))]
    private void Download(SearchResultItemViewModel? item)
    {
        if (item is null || !HasResults)
        {
            return;
        }

        DownloadRequested?.Invoke(item);
    }
}
