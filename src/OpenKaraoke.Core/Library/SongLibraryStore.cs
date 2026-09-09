using Microsoft.Data.Sqlite;

namespace OpenKaraoke.Core.Library;

/// <summary>Persistence for the song library. Add/update, list/search and delete.</summary>
public interface ISongLibraryStore
{
    /// <summary>Creates the database file and schema if needed. Call once at startup.</summary>
    Task InitializeAsync();

    Task<List<SongRecord>> GetAllAsync(string? searchText = null);

    Task<SongRecord?> GetByIdAsync(long id);

    Task<SongRecord?> GetByVideoIdAsync(string videoId);

    /// <summary>
    /// Inserts a new song, or updates the existing row with the same video id
    /// (a re-download refreshes the local file/metadata but keeps the original id and added date).
    /// Returns the stored record.
    /// </summary>
    Task<SongRecord> AddOrUpdateAsync(SongRecord song);

    Task<bool> DeleteAsync(long id);

    Task<int> CountAsync();
}

/// <summary>SQLite-backed <see cref="ISongLibraryStore"/>. Thread-safety is the caller's concern (UI thread only).</summary>
public sealed class SqliteSongLibraryStore : ISongLibraryStore
{
    private readonly string _databasePath;
    private readonly string _connectionString;

    public SqliteSongLibraryStore(string databasePath)
    {
        _databasePath = databasePath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
    }

    public async Task InitializeAsync()
    {
        string? directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS songs (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                video_id        TEXT NOT NULL UNIQUE,
                title           TEXT NOT NULL,
                artist          TEXT NOT NULL,
                tj_number       TEXT NULL,
                local_path      TEXT NOT NULL,
                duration_seconds INTEGER NOT NULL DEFAULT 0,
                file_size_bytes INTEGER NOT NULL DEFAULT 0,
                thumbnail_url   TEXT NULL,
                added_at_utc    TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_songs_title ON songs(title);
            CREATE INDEX IF NOT EXISTS idx_songs_artist ON songs(artist);
            """;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task<List<SongRecord>> GetAllAsync(string? searchText = null)
    {
        var result = new List<SongRecord>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        string where = string.Empty;
        string? like = null;
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            like = $"%{EscapeLike(searchText.Trim())}%";
            where = " WHERE title LIKE @q ESCAPE '\\' OR artist LIKE @q ESCAPE '\\' OR COALESCE(tj_number,'') LIKE @q ESCAPE '\\'";
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
             SELECT id, video_id, title, artist, tj_number, local_path,
                    duration_seconds, file_size_bytes, thumbnail_url, added_at_utc
             FROM songs{where}
             ORDER BY added_at_utc DESC, id DESC
             """;
        if (like != null)
        {
            command.Parameters.AddWithValue("@q", like);
        }

        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            result.Add(ReadSong(reader));
        }

        return result;
    }

    public async Task<SongRecord?> GetByIdAsync(long id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, video_id, title, artist, tj_number, local_path,
                   duration_seconds, file_size_bytes, thumbnail_url, added_at_utc
            FROM songs WHERE id = @id
            """;
        command.Parameters.AddWithValue("@id", id);

        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        return await reader.ReadAsync().ConfigureAwait(false) ? ReadSong(reader) : null;
    }

    public async Task<SongRecord?> GetByVideoIdAsync(string videoId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, video_id, title, artist, tj_number, local_path,
                   duration_seconds, file_size_bytes, thumbnail_url, added_at_utc
            FROM songs WHERE video_id = @video_id
            """;
        command.Parameters.AddWithValue("@video_id", videoId);

        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        return await reader.ReadAsync().ConfigureAwait(false) ? ReadSong(reader) : null;
    }

    public async Task<SongRecord> AddOrUpdateAsync(SongRecord song)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO songs (video_id, title, artist, tj_number, local_path,
                               duration_seconds, file_size_bytes, thumbnail_url, added_at_utc)
            VALUES (@video_id, @title, @artist, @tj_number, @local_path,
                    @duration_seconds, @file_size_bytes, @thumbnail_url, @added_at_utc)
            ON CONFLICT(video_id) DO UPDATE SET
                title = excluded.title,
                artist = excluded.artist,
                tj_number = excluded.tj_number,
                local_path = excluded.local_path,
                duration_seconds = excluded.duration_seconds,
                file_size_bytes = excluded.file_size_bytes,
                thumbnail_url = excluded.thumbnail_url
            RETURNING id, added_at_utc
            """;

        string addedAt = song.AddedAtUtc == default
            ? DateTime.UtcNow.ToString("O")
            : song.AddedAtUtc.ToString("O");

        command.Parameters.AddWithValue("@video_id", song.VideoId);
        command.Parameters.AddWithValue("@title", song.Title);
        command.Parameters.AddWithValue("@artist", song.Artist);
        command.Parameters.AddWithValue("@tj_number", (object?)song.TjNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("@local_path", song.LocalPath);
        command.Parameters.AddWithValue("@duration_seconds", song.DurationSeconds);
        command.Parameters.AddWithValue("@file_size_bytes", song.FileSizeBytes);
        command.Parameters.AddWithValue("@thumbnail_url", (object?)song.ThumbnailUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("@added_at_utc", addedAt);

        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        if (!await reader.ReadAsync().ConfigureAwait(false))
        {
            throw new InvalidOperationException("라이브러리 저장에 실패했습니다.");
        }

        long id = reader.GetInt64(0);
        string storedAddedAt = reader.GetString(1);
        return song with { Id = id, AddedAtUtc = DateTime.Parse(storedAddedAt).ToUniversalTime() };
    }

    public async Task<bool> DeleteAsync(long id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM songs WHERE id = @id";
        command.Parameters.AddWithValue("@id", id);
        int affected = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        return affected > 0;
    }

    public async Task<int> CountAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM songs";
        object? result = await command.ExecuteScalarAsync().ConfigureAwait(false);
        return result == null ? 0 : Convert.ToInt32(result);
    }

    private static SongRecord ReadSong(SqliteDataReader reader)
    {
        return new SongRecord
        {
            Id = reader.GetInt64(0),
            VideoId = reader.GetString(1),
            Title = reader.GetString(2),
            Artist = reader.GetString(3),
            TjNumber = reader.IsDBNull(4) ? null : reader.GetString(4),
            LocalPath = reader.GetString(5),
            DurationSeconds = reader.GetInt32(6),
            FileSizeBytes = reader.GetInt64(7),
            ThumbnailUrl = reader.IsDBNull(8) ? null : reader.GetString(8),
            AddedAtUtc = DateTime.Parse(reader.GetString(9)).ToUniversalTime(),
        };
    }

    private static string EscapeLike(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
    }
}
