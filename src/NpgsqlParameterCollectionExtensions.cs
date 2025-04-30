// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Npgsql;
using NpgsqlTypes;

namespace Microsoft.Extensions.Caching.Postgres;

internal static class NpgsqlParameterCollectionExtensions
{
    // For all values where the length is less than the below value, try setting the size of the
    // parameter for better performance.
    public const int DefaultValueColumnWidth = 8000;

    // Maximum size of a primary key column is 900 bytes (898 bytes from the key + 2 additional bytes required by
    // the database).
    // TODO Postgres impact?
    public const int CacheItemIdColumnWidth = 449;

    public static NpgsqlParameterCollection AddCacheItemId(this NpgsqlParameterCollection parameters, string value)
    {
        return parameters.AddWithValue2(Columns.Names.CacheItemId, NpgsqlDbType.Varchar, CacheItemIdColumnWidth, value);
    }

    public static NpgsqlParameterCollection AddCacheItemValue(this NpgsqlParameterCollection parameters, ArraySegment<byte> value)
    {
        if (value.Array is null) // null array (not really anticipating this, but...)
        {
            return parameters.AddWithValue2(Columns.Names.CacheItemValue, NpgsqlDbType.Bytea, Array.Empty<byte>());
        }

        if (value.Count == 0)
        {
            // workaround for https://github.com/dotnet/SqlClient/issues/2465
            value = new([], 0, 0);
        }

        if (value.Offset == 0 & value.Count == value.Array!.Length) // right-sized array
        {
            if (value.Count < DefaultValueColumnWidth)
            {
                return parameters.AddWithValue2(
                    Columns.Names.CacheItemValue,
                    NpgsqlDbType.Bytea,
                    DefaultValueColumnWidth, // send as varbinary(constantSize)
                    value.Array);
            }
            else
            {
                // do not mention the size
                return parameters.AddWithValue2(Columns.Names.CacheItemValue, NpgsqlDbType.Bytea, value.Array);
            }
        }
        else // array fragment; set the Size and Offset accordingly
        {
            var p = new NpgsqlParameter(Columns.Names.CacheItemValue, NpgsqlDbType.Bytea, value.Count);
            p.Value = value.Array;
            //p.Offset = value.Offset;  TODO IS OFFSET NEEDED?
            parameters.Add(p);
            return parameters;
        }
    }

    public static NpgsqlParameterCollection AddSlidingExpirationInSeconds(
        this NpgsqlParameterCollection parameters,
        TimeSpan? value)
    {
        if (value.HasValue)
        {
            return parameters.AddWithValue2(
                Columns.Names.SlidingExpirationInSeconds, NpgsqlDbType.Bigint, value.Value.TotalSeconds);
        }
        else
        {
            return parameters.AddWithValue2(Columns.Names.SlidingExpirationInSeconds, NpgsqlDbType.Bigint, DBNull.Value);
        }
    }

    public static NpgsqlParameterCollection AddAbsoluteExpiration(
        this NpgsqlParameterCollection parameters,
        DateTimeOffset? utcTime)
    {
        if (utcTime.HasValue)
        {
            return parameters.AddWithValue2(
                Columns.Names.AbsoluteExpiration, NpgsqlDbType.TimestampTz, utcTime.Value);
        }
        else
        {
            return parameters.AddWithValue2(
                Columns.Names.AbsoluteExpiration, NpgsqlDbType.TimestampTz, DBNull.Value);
        }
    }
    public static NpgsqlParameterCollection AddWithValue2(
        this NpgsqlParameterCollection parameters,
        string parameterName,
        NpgsqlDbType dbType,
        object? value)
    {
        var parameter = new NpgsqlParameter(parameterName, dbType);
        parameter.Value = value;
        parameters.Add(parameter);
        parameter.ResetDbType();
        return parameters;
    }

    public static NpgsqlParameterCollection AddWithValue2(
        this NpgsqlParameterCollection parameters,
        string parameterName,
        NpgsqlDbType dbType,
        int size,
        object? value)
    {
        var parameter = new NpgsqlParameter(parameterName, dbType, size);
        parameter.Value = value;
        parameters.Add(parameter);
        parameter.ResetDbType();
        return parameters;
    }

}
