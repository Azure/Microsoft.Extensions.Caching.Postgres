// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Data;
using System.Data.Common;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using Microsoft.Extensions.Caching.Distributed;
using Npgsql;
using NpgsqlTypes;

namespace Microsoft.Extensions.Caching.Postgres;

internal sealed class DatabaseOperations : IDatabaseOperations, IAsyncDisposable {
    private const string UtcNowParameterName = "@utcNow";

    private volatile bool ddlExecuted;
    private readonly object ddlLock = new object();

    private readonly NpgsqlDataSource ds;

    private static byte[] CopyArraySegment(ArraySegment<byte> value) {
        if (value.Array is null || value.Count == 0) {
            return [];
        }

        if (value.Offset == 0 && value.Count == value.Array.Length) {
            return value.Array;
        }

        var copied = new byte[value.Count];
        Buffer.BlockCopy(value.Array, value.Offset, copied, 0, value.Count);
        return copied;
    }

    public DatabaseOperations(NpgsqlDataSource dataSource, string schemaName, string tableName, bool useWAL, bool createIfNotExists, TimeProvider timeProvider) {
        ds = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        SchemaName = schemaName;
        TableName = tableName;
        UseWAL = useWAL;
        CreateIfNotExists = createIfNotExists;
        TimeProvider = timeProvider;
        SqlQueries = new SqlQueries(schemaName, tableName, useWAL);
    }

    internal SqlQueries SqlQueries { get; }

    internal string? ConnectionString { get; }

    internal string SchemaName { get; }

    internal string TableName { get; }

    internal bool UseWAL { get; }
    internal bool CreateIfNotExists { get; }

    private TimeProvider TimeProvider { get; }

    private NpgsqlConnection InitializeConnection() {
        var conn = ds.CreateConnection();

        lock (ddlLock) {

            if (!ddlExecuted && CreateIfNotExists) {
                var sql = string.Join(";", SqlQueries.CreateSchema, SqlQueries.CreateTable, SqlQueries.CreateIndex);
                conn.Open();
                using (var command = new NpgsqlCommand(sql, conn)) { command.ExecuteNonQuery(); }
                conn.Close();
                ddlExecuted = true;
            }
        }

        return conn;
    }

    public void DeleteCacheItem(string key) {
        using (var connection = InitializeConnection())
        using (var command = new NpgsqlCommand(SqlQueries.DeleteCacheItem, connection)) {
            command.Parameters.AddCacheItemId(key);

            connection.Open();

            command.ExecuteNonQuery();
        }
    }

    public async Task DeleteCacheItemAsync(string key, CancellationToken token = default(CancellationToken)) {
        token.ThrowIfCancellationRequested();

        using (var connection = InitializeConnection())
        using (var command = new NpgsqlCommand(SqlQueries.DeleteCacheItem, connection)) {
            command.Parameters.AddCacheItemId(key);

            await connection.OpenAsync(token).ConfigureAwait(false);

            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
    }

    public byte[]? GetCacheItem(string key) {
        return GetCacheItem(key, includeValue: true);
    }

    public bool TryGetCacheItem(string key, IBufferWriter<byte> destination) {
        return GetCacheItem(key, includeValue: true, destination: destination) is not null;
    }

    public Task<byte[]?> GetCacheItemAsync(string key, CancellationToken token = default(CancellationToken)) {
        token.ThrowIfCancellationRequested();

        return GetCacheItemAsync(key, includeValue: true, token: token);
    }

    public async Task<bool> TryGetCacheItemAsync(string key, IBufferWriter<byte> destination, CancellationToken token = default(CancellationToken)) {
        token.ThrowIfCancellationRequested();

        var arr = await GetCacheItemAsync(key, includeValue: true, destination: destination, token: token).ConfigureAwait(false);
        return arr is not null;
    }

    public void RefreshCacheItem(string key) {
        GetCacheItem(key, includeValue: false);
    }

    public Task RefreshCacheItemAsync(string key, CancellationToken token = default(CancellationToken)) {
        token.ThrowIfCancellationRequested();

        return GetCacheItemAsync(key, includeValue: false, token: token);
    }

    public void DeleteExpiredCacheItems() {
        var utcNow = TimeProvider.GetUtcNow();

        using (var connection = InitializeConnection())
        using (var command = new NpgsqlCommand(SqlQueries.DeleteExpiredCacheItems, connection)) {
            command.Parameters.AddWithValue(UtcNowParameterName, NpgsqlDbType.TimestampTz, utcNow);

            connection.Open();

            var effectedRowCount = command.ExecuteNonQuery();
        }
    }

    public void SetCacheItem(string key, ArraySegment<byte> value, DistributedCacheEntryOptions options) {
        var utcNow = TimeProvider.GetUtcNow();

        var absoluteExpiration = DatabaseOperations.GetAbsoluteExpiration(utcNow, options);
        DatabaseOperations.ValidateOptions(options.SlidingExpiration, absoluteExpiration);

        using (var connection = InitializeConnection())
        using (var upsertCommand = new NpgsqlCommand(SqlQueries.SetCacheItem, connection)) {
            upsertCommand.Parameters
                .AddCacheItemId(key)
                .AddCacheItemValue(value)
                .AddSlidingExpirationInSeconds(options.SlidingExpiration)
                .AddAbsoluteExpiration(absoluteExpiration)
                .AddWithValue(UtcNowParameterName, NpgsqlDbType.TimestampTz, utcNow);

            connection.Open();
            upsertCommand.ExecuteNonQuery();
        }
    }

    public async Task SetCacheItemAsync(string key, ArraySegment<byte> value, DistributedCacheEntryOptions options, CancellationToken token = default(CancellationToken)) {
        token.ThrowIfCancellationRequested();

        var utcNow = TimeProvider.GetUtcNow();

        var absoluteExpiration = DatabaseOperations.GetAbsoluteExpiration(utcNow, options);
        DatabaseOperations.ValidateOptions(options.SlidingExpiration, absoluteExpiration);

        using (var connection = InitializeConnection())
        using (var upsertCommand = new NpgsqlCommand(SqlQueries.SetCacheItem, connection)) {
            upsertCommand.Parameters
                .AddCacheItemId(key)
                .AddCacheItemValue(value)
                .AddSlidingExpirationInSeconds(options.SlidingExpiration)
                .AddAbsoluteExpiration(absoluteExpiration)
                .AddWithValue(UtcNowParameterName, NpgsqlDbType.TimestampTz, utcNow);

            await connection.OpenAsync(token).ConfigureAwait(false);
            await upsertCommand.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
    }

    private byte[]? GetCacheItem(string key, bool includeValue, IBufferWriter<byte>? destination = null) {
        var utcNow = TimeProvider.GetUtcNow();

        string query;
        if (includeValue) {
            query = SqlQueries.GetCacheItem;
        }
        else {
            query = SqlQueries.GetCacheItemWithoutValue;
        }

        byte[]? value = null;
        using (var connection = InitializeConnection())
        using (var command = new NpgsqlCommand(query, connection)) {
            command.Parameters
                .AddCacheItemId(key)
                .AddWithValue(UtcNowParameterName, NpgsqlDbType.TimestampTz, utcNow);

            connection.Open();

            if (includeValue) {
                using var reader = command.ExecuteReader(
                    CommandBehavior.SequentialAccess | CommandBehavior.SingleRow | CommandBehavior.SingleResult);

                if (reader.Read()) {
                    if (destination is null) {
                        value = reader.GetFieldValue<byte[]>(Columns.Indexes.CacheItemValueIndex);
                    }
                    else {
                        StreamOut(reader, Columns.Indexes.CacheItemValueIndex, destination);
                        value = []; // use non-null here as a sentinel to say "we got one"
                    }
                }
            }
            else {
                command.ExecuteNonQuery();
            }
        }

        return value;
    }

    private async Task<byte[]?> GetCacheItemAsync(string key, bool includeValue, IBufferWriter<byte>? destination = null, CancellationToken token = default(CancellationToken)) {
        token.ThrowIfCancellationRequested();

        var utcNow = TimeProvider.GetUtcNow();

        string query;
        if (includeValue) {
            query = SqlQueries.GetCacheItem;
        }
        else {
            query = SqlQueries.GetCacheItemWithoutValue;
        }

        byte[]? value = null;
        using (var connection = InitializeConnection())
        using (var command = new NpgsqlCommand(query, connection)) {
            command.Parameters
                .AddCacheItemId(key)
                .AddWithValue(UtcNowParameterName, NpgsqlDbType.TimestampTz, utcNow);

            await connection.OpenAsync(token).ConfigureAwait(false);

            if (includeValue) {
                using var reader = await command.ExecuteReaderAsync(
                    CommandBehavior.SequentialAccess | CommandBehavior.SingleRow | CommandBehavior.SingleResult, token).ConfigureAwait(false);

                if (await reader.ReadAsync(token).ConfigureAwait(false)) {
                    if (destination is null) {
                        value = await reader.GetFieldValueAsync<byte[]>(Columns.Indexes.CacheItemValueIndex, token).ConfigureAwait(false);
                    }
                    else {
                        StreamOut(reader, Columns.Indexes.CacheItemValueIndex, destination);
                        value = []; // use non-null here as a sentinel to say "we got one"
                    }
                }
            }
            else {
                await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }

        return value;
    }

    private async Task<byte[]?> GetCacheItemAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string key, CancellationToken token) {
        token.ThrowIfCancellationRequested();

        var utcNow = TimeProvider.GetUtcNow();
        byte[]? value = null;

        using (var command = new NpgsqlCommand(SqlQueries.GetCacheItem, connection, transaction)) {
            command.Parameters
                .AddCacheItemId(key)
                .AddWithValue(UtcNowParameterName, NpgsqlDbType.TimestampTz, utcNow);

            using var reader = await command.ExecuteReaderAsync(
                CommandBehavior.SequentialAccess | CommandBehavior.SingleRow | CommandBehavior.SingleResult, token).ConfigureAwait(false);

            if (await reader.ReadAsync(token).ConfigureAwait(false)) {
                value = await reader.GetFieldValueAsync<byte[]>(Columns.Indexes.CacheItemValueIndex, token).ConfigureAwait(false);
            }
        }

        return value;
    }

    public async Task<byte[]> GetOrCreateCacheItemWithAdvisoryLockAsync(
        string key,
        DistributedCacheEntryOptions options,
        Func<CancellationToken, Task<ArraySegment<byte>>> valueFactory,
        CancellationToken token = default(CancellationToken)) {
        token.ThrowIfCancellationRequested();

        var utcNow = TimeProvider.GetUtcNow();

        var absoluteExpiration = DatabaseOperations.GetAbsoluteExpiration(utcNow, options);
        DatabaseOperations.ValidateOptions(options.SlidingExpiration, absoluteExpiration);

        using (var connection = InitializeConnection()) {
            await connection.OpenAsync(token).ConfigureAwait(false);

            using var transaction = connection.BeginTransaction();
            try {
                using (var lockCommand = new NpgsqlCommand(SqlQueries.AcquireAdvisoryTransactionLock, connection, transaction)) {
                    lockCommand.Parameters.AddCacheItemId(key);
                    await lockCommand.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                var existingValue = await GetCacheItemAsync(connection, transaction, key, token).ConfigureAwait(false);
                if (existingValue is not null) {
                    transaction.Commit();
                    return existingValue;
                }

                var createdValue = await valueFactory(token).ConfigureAwait(false);

                using (var upsertCommand = new NpgsqlCommand(SqlQueries.SetCacheItem, connection, transaction)) {
                    upsertCommand.Parameters
                    .AddCacheItemId(key)
                    .AddCacheItemValue(createdValue)
                    .AddSlidingExpirationInSeconds(options.SlidingExpiration)
                    .AddAbsoluteExpiration(absoluteExpiration)
                    .AddWithValue(UtcNowParameterName, NpgsqlDbType.TimestampTz, utcNow);

                    await upsertCommand.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                transaction.Commit();
                return CopyArraySegment(createdValue);
            }
            catch {
                try {
                    transaction.Rollback();
                }
                catch {
                    // Best effort rollback. Original exception is more actionable.
                }

                throw;
            }
        }
    }

    private static long StreamOut(NpgsqlDataReader source, int ordinal, IBufferWriter<byte> destination) {
        long dataIndex = 0;
        int read = 0;
        byte[]? lease = null;
        do {
            dataIndex += read; // increment offset

            const int DefaultPageSize = 8192;

            var memory = destination.GetMemory(DefaultPageSize); // start from the page size
            if (MemoryMarshal.TryGetArray<byte>(memory, out var segment)) {
                // avoid an extra copy by writing directly to the target array when possible
                read = (int)source.GetBytes(ordinal, dataIndex, segment.Array, segment.Offset, segment.Count);
                if (read > 0) {
                    destination.Advance(read);
                }
            }
            else {
                lease ??= ArrayPool<byte>.Shared.Rent(DefaultPageSize);
                read = (int)source.GetBytes(ordinal, dataIndex, lease, 0, lease.Length);

                if (read > 0) {
                    if (new ReadOnlySpan<byte>(lease, 0, read).TryCopyTo(memory.Span)) {
                        destination.Advance(read);
                    }
                    else {
                        // multi-chunk write (utility method)
                        destination.Write(new(lease, 0, read));
                    }
                }
            }
        }
        while (read > 0);

        if (lease is not null) {
            ArrayPool<byte>.Shared.Return(lease);
        }
        return dataIndex;
    }

    private static DateTimeOffset? GetAbsoluteExpiration(DateTimeOffset utcNow, DistributedCacheEntryOptions options) {
        // calculate absolute expiration
        DateTimeOffset? absoluteExpiration = null;
        if (options.AbsoluteExpirationRelativeToNow.HasValue) {
            absoluteExpiration = utcNow.Add(options.AbsoluteExpirationRelativeToNow.Value);
        }
        else if (options.AbsoluteExpiration.HasValue) {
            if (options.AbsoluteExpiration.Value <= utcNow) {
                throw new InvalidOperationException("The absolute expiration value must be in the future.");
            }

            absoluteExpiration = options.AbsoluteExpiration.Value;
        }
        return absoluteExpiration;
    }

    private static void ValidateOptions(TimeSpan? slidingExpiration, DateTimeOffset? absoluteExpiration) {
        if (!slidingExpiration.HasValue && !absoluteExpiration.HasValue) {
            throw new InvalidOperationException("Either absolute or sliding expiration needs to be provided.");
        }
    }

    public ValueTask DisposeAsync() {
        return ds.DisposeAsync();
    }
}
