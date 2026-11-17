using Microsoft.Data.Sqlite;
using ViberAttendance.Core.Configuration;

namespace ViberAttendance.Persistence;

internal sealed class SqliteConnectionFactory
{
    private readonly string _databasePath;

    internal SqliteConnectionFactory(AppSettings settings)
    {
        _databasePath = settings.DatabasePath;
    }

    public SqliteConnection Create()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        };

        return new SqliteConnection(builder.ToString());
    }
}
