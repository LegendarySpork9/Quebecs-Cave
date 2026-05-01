using System.Data.Common;

namespace QuebecsCave.Data.Sql;

internal static class DbReaderExtensions
{
    // .NET 10's System.Data.DataReaderExtensions already provides
    // GetString/GetInt32/GetBoolean(this DbDataReader, string). We only add
    // the helpers that don't exist there.

    public static string? GetNullableString(this DbDataReader reader, string column)
    {
        var i = reader.GetOrdinal(column);
        return reader.IsDBNull(i) ? null : reader.GetString(i);
    }

    public static int? GetNullableInt32(this DbDataReader reader, string column)
    {
        var i = reader.GetOrdinal(column);
        return reader.IsDBNull(i) ? null : reader.GetInt32(i);
    }

    public static DateTimeOffset GetDateTimeOffset(this DbDataReader reader, string column)
    {
        var i = reader.GetOrdinal(column);
        return reader.GetFieldValue<DateTimeOffset>(i);
    }
}
