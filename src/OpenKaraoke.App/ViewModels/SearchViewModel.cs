using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKaraoke.Core.Search;

namespace OpenKaraoke.App.ViewModels;

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
    private readonly YoutubeSearchService _searchService;

    public ObservableCollection<SearchResultItemViewModel> Results { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsSearching), nameof(HasResults),
        nameof(IsEmptyResult), nameof(HasError))]
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
            ErrorMessage = ex.Message;
            State = SearchUiState.Error;
        }
        catch (Exception)
        {
            ErrorMessage = "검색 중 오류가 발생했습니다. 잠시 후 다시 시도해 주세요.";
            State = SearchUiState.Error;
        }
    }
}
