using Microsoft.Data.Sqlite;

namespace MonAlbumPc.Core.Storage;

/// <summary>SQLite catalog holding albums, devices and the import history.</summary>
public sealed class Database
{
    private static readonly string[] Migrations =
    [
        """
        CREATE TABLE albums (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            destination_root TEXT NOT NULL,
            color TEXT NOT NULL,
            created_at TEXT NOT NULL
        );
        CREATE TABLE devices (
            id TEXT PRIMARY KEY,
            album_id TEXT NOT NULL REFERENCES albums(id),
            name TEXT NOT NULL,
            model TEXT NOT NULL,
            secret_hash BLOB NOT NULL,
            app_version TEXT,
            paired_at TEXT NOT NULL,
            last_seen_at TEXT,
            revoked_at TEXT
        );
        CREATE TABLE imports (
            id INTEGER PRIMARY KEY,
            sha256 TEXT NOT NULL,
            album_id TEXT NOT NULL,
            device_id TEXT,
            size INTEGER NOT NULL,
            original_name TEXT NOT NULL,
            source_path TEXT,
            dest_path TEXT NOT NULL,
            taken_at TEXT,
            channel TEXT NOT NULL,
            imported_at TEXT NOT NULL
        );
        CREATE INDEX ix_imports_album_sha ON imports(album_id, sha256);
        CREATE INDEX ix_imports_device_source ON imports(device_id, source_path);
        CREATE TABLE index_entries (
            album_id TEXT NOT NULL,
            path TEXT NOT NULL,
            sha256 TEXT NOT NULL,
            size INTEGER NOT NULL,
            name TEXT NOT NULL,
            taken_at TEXT,
            modified_at TEXT NOT NULL,
            PRIMARY KEY (album_id, path)
        );
        CREATE INDEX ix_index_sha ON index_entries(album_id, sha256);
        CREATE INDEX ix_index_name ON index_entries(album_id, name COLLATE NOCASE);
        """,
        """
        CREATE TABLE sessions (
            id INTEGER PRIMARY KEY,
            device_id TEXT NOT NULL,
            album_id TEXT NOT NULL,
            channel TEXT NOT NULL,
            started_at TEXT NOT NULL,
            ended_at TEXT NOT NULL,
            photos INTEGER NOT NULL,
            videos INTEGER NOT NULL,
            error_code TEXT,
            error_message TEXT
        );
        CREATE INDEX ix_sessions_started ON sessions(started_at);
        """,
    ];

    private readonly string _connectionString;

    public Database(string path)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = true }.ToString();
        Migrate();
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        connection.Execute("PRAGMA foreign_keys = ON;");
        return connection;
    }

    private void Migrate()
    {
        using var connection = Open();
        connection.Execute("PRAGMA journal_mode = WAL;");
        var version = Convert.ToInt32(connection.Scalar("PRAGMA user_version;"));
        for (var i = version; i < Migrations.Length; i++)
        {
            using var transaction = connection.BeginTransaction();
            connection.Execute(Migrations[i]);
            connection.Execute($"PRAGMA user_version = {i + 1};");
            transaction.Commit();
        }
    }
}
