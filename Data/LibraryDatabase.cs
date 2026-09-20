using System;
using System.Collections.Generic;
using System.IO;
using Lutris.Models;
using Microsoft.Data.Sqlite;

namespace Lutris.Data;

/// <summary>
/// SQLite storage for the game library. The schema matches the earlier Electron
/// version of the app so exported archives can be imported in either direction.
/// All members must be called from a single thread (the UI thread).
/// </summary>
public sealed class LibraryDatabase : IDisposable
{
    private readonly string _path;
    private SqliteConnection? _connection;

    public LibraryDatabase(string path)
    {
        _path = path;
    }

    public bool IsOpen => _connection is not null;

    public void Open()
    {
        if (_connection is not null) return;

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        // Pooling is disabled so Close() really releases the file; import/export replace it.
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        connection.Open();

        Execute(connection, "PRAGMA journal_mode = WAL;");
        Execute(connection, "PRAGMA foreign_keys = ON;");
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS games (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              title TEXT NOT NULL,
              release_year INTEGER,
              exe_path TEXT NOT NULL,
              portrait_path TEXT,
              landscape_path TEXT,
              last_played_at INTEGER,
              total_play_time_ms INTEGER NOT NULL DEFAULT 0,
              created_at INTEGER NOT NULL DEFAULT (strftime('%s','now') * 1000)
            );
            """);

        // Libraries created before play time tracking existed lack this column.
        if (!HasColumn(connection, "games", "total_play_time_ms"))
        {
            Execute(connection, "ALTER TABLE games ADD COLUMN total_play_time_ms INTEGER NOT NULL DEFAULT 0;");
        }

        _connection = connection;
    }

    public void Close()
    {
        if (_connection is null) return;
        _connection.Close();
        _connection.Dispose();
        _connection = null;
    }

    public void Dispose() => Close();

    /// <summary>Folds the write-ahead log into the main file so it can be copied on its own.</summary>
    public void Checkpoint()
    {
        Execute(Connection, "PRAGMA wal_checkpoint(TRUNCATE);");
    }

    public List<Game> ListGames()
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM games ORDER BY title COLLATE NOCASE";
        using var reader = cmd.ExecuteReader();
        var list = new List<Game>();
        while (reader.Read()) list.Add(ReadGame(reader));
        return list;
    }

    public Game? GetGame(long id)
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM games WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadGame(reader) : null;
    }

    public Game Insert(string title, int? releaseYear, string exePath, string? portraitFile, string? landscapeFile)
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO games (title, release_year, exe_path, portrait_path, landscape_path)
            VALUES ($title, $year, $exe, $portrait, $landscape);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$title", title);
        cmd.Parameters.AddWithValue("$year", (object?)releaseYear ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$exe", exePath);
        cmd.Parameters.AddWithValue("$portrait", (object?)portraitFile ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$landscape", (object?)landscapeFile ?? DBNull.Value);
        var id = (long)cmd.ExecuteScalar()!;
        return GetGame(id)!;
    }

    public Game? Update(long id, string title, int? releaseYear, string exePath, string? portraitFile, string? landscapeFile)
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = """
            UPDATE games
            SET title = $title, release_year = $year, exe_path = $exe,
                portrait_path = $portrait, landscape_path = $landscape
            WHERE id = $id
            """;
        cmd.Parameters.AddWithValue("$title", title);
        cmd.Parameters.AddWithValue("$year", (object?)releaseYear ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$exe", exePath);
        cmd.Parameters.AddWithValue("$portrait", (object?)portraitFile ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$landscape", (object?)landscapeFile ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        return GetGame(id);
    }

    public void Delete(long id)
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM games WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void MarkPlayed(long id)
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = "UPDATE games SET last_played_at = $now WHERE id = $id";
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void AddPlayTime(long id, long milliseconds)
    {
        if (milliseconds <= 0) return;
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = "UPDATE games SET total_play_time_ms = total_play_time_ms + $ms WHERE id = $id";
        cmd.Parameters.AddWithValue("$ms", milliseconds);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Copies another SQLite library into this one using the online backup API, which reads
    /// through the source's write-ahead log without modifying the source files.
    /// </summary>
    public static void CopyDatabase(string sourcePath, string destinationPath)
    {
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
    }

    private SqliteConnection Connection =>
        _connection ?? throw new InvalidOperationException("The library database is not open.");

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static bool HasColumn(SqliteConnection connection, string table, string column)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static Game ReadGame(SqliteDataReader r)
    {
        return new Game(
            Id: r.GetInt64(r.GetOrdinal("id")),
            Title: r.GetString(r.GetOrdinal("title")),
            ReleaseYear: r.IsDBNull(r.GetOrdinal("release_year")) ? null : r.GetInt32(r.GetOrdinal("release_year")),
            ExePath: r.GetString(r.GetOrdinal("exe_path")),
            PortraitFile: r.IsDBNull(r.GetOrdinal("portrait_path")) ? null : r.GetString(r.GetOrdinal("portrait_path")),
            LandscapeFile: r.IsDBNull(r.GetOrdinal("landscape_path")) ? null : r.GetString(r.GetOrdinal("landscape_path")),
            LastPlayedAt: r.IsDBNull(r.GetOrdinal("last_played_at")) ? null : r.GetInt64(r.GetOrdinal("last_played_at")),
            TotalPlayTimeMs: r.IsDBNull(r.GetOrdinal("total_play_time_ms")) ? 0 : r.GetInt64(r.GetOrdinal("total_play_time_ms")),
            CreatedAt: r.GetInt64(r.GetOrdinal("created_at")));
    }
}
