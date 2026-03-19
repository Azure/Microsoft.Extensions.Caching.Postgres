// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Microsoft.Extensions.Caching.Postgres;

/// <summary>
/// Distributed cache implementation using Postgres database.
/// </summary>
public class PostgresCache : IDistributedCache, IBufferDistributedCache, IAsyncDisposable {
    private static readonly TimeSpan MinimumExpiredItemsDeletionInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultExpiredItemsDeletionInterval = TimeSpan.FromMinutes(30);

    private static readonly int AUTO_PREPARE_MIN_USAGE = 2;
    private static readonly int AUTO_PREPARE_MAX_USAGE = 32;

    private readonly IDatabaseOperations _dbOperations;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _expiredItemsDeletionInterval;
    private long _lastExpirationScan;
    private int _expirationScanRunning;
    private readonly Action _deleteExpiredCachedItemsDelegate;
    private readonly TimeSpan _defaultSlidingExpiration;

    /// <summary>
    /// Initializes a new instance of <see cref="PostgresCache"/>.
    /// </summary>
    /// <param name="options">The configuration options.</param>
    public PostgresCache(IOptions<PostgresCacheOptions> options) {
        var cacheOptions = options.Value;

        ArgumentThrowHelper.ThrowIfNullOrEmpty(cacheOptions.SchemaName);
        ArgumentThrowHelper.ThrowIfNullOrEmpty(cacheOptions.TableName);

        if (cacheOptions.ConfigureDataSourceBuilder is not null || cacheOptions.DataSource is null) {
            ArgumentThrowHelper.ThrowIfNullOrEmpty(cacheOptions.ConnectionString);
        }

        if (cacheOptions.ExpiredItemsDeletionInterval.HasValue &&
            cacheOptions.ExpiredItemsDeletionInterval.Value < MinimumExpiredItemsDeletionInterval) {
            throw new ArgumentException(
                $"{nameof(PostgresCacheOptions.ExpiredItemsDeletionInterval)} cannot be less than the minimum " +
                $"value of {MinimumExpiredItemsDeletionInterval.TotalMinutes} minutes.");
        }
        if (cacheOptions.DefaultSlidingExpiration <= TimeSpan.Zero) {
#pragma warning disable CA2208 // Instantiate argument exceptions correctly
            throw new ArgumentOutOfRangeException(
                nameof(cacheOptions.DefaultSlidingExpiration),
                cacheOptions.DefaultSlidingExpiration,
                "The sliding expiration value must be positive.");
#pragma warning restore CA2208 // Instantiate argument exceptions correctly
        }

        _timeProvider = cacheOptions.TimeProvider ?? TimeProvider.System;
        _expiredItemsDeletionInterval =
            cacheOptions.ExpiredItemsDeletionInterval ?? DefaultExpiredItemsDeletionInterval;
        _deleteExpiredCachedItemsDelegate = DeleteExpiredCacheItems;
        _defaultSlidingExpiration = cacheOptions.DefaultSlidingExpiration;

        NpgsqlDataSource dataSource;
        bool isExternalDataSource;
      
        if (cacheOptions.ConfigureDataSourceBuilder is not null) {
            var builder = new NpgsqlDataSourceBuilder(cacheOptions.ConnectionString!);
          
            // if disabled or misconfigured
            // default to min usages before auto-prepare and max auto-prepared statements
            //  should be sufficient for most workloads while keeping memory usage in check
            if (builder.ConnectionStringBuilder.AutoPrepareMinUsages <= 0) {
                builder.ConnectionStringBuilder.AutoPrepareMinUsages = AUTO_PREPARE_MIN_USAGE;
            }
            if (builder.ConnectionStringBuilder.MaxAutoPrepare <= 0) {
                builder.ConnectionStringBuilder.MaxAutoPrepare = AUTO_PREPARE_MAX_USAGE;
            }
            cacheOptions.ConfigureDataSourceBuilder?.Invoke(builder);
            dataSource = builder.Build();
            isExternalDataSource = false;            
        }
        else if (cacheOptions.DataSource is not null) {
            dataSource = cacheOptions.DataSource;
            isExternalDataSource = true;
        }
        else {
            dataSource = NpgsqlDataSource.Create(cacheOptions.ConnectionString!);
            isExternalDataSource = false;
        }

        _dbOperations = new DatabaseOperations(
            dataSource,
            cacheOptions.SchemaName!,
            cacheOptions.TableName!,
            cacheOptions.UseWAL ?? false,
            cacheOptions.CreateIfNotExists ?? false,
            _timeProvider,
            isExternalDataSource);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() {
        if (_dbOperations is IAsyncDisposable asyncDisposable) {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public byte[]? Get(string key) {
        ArgumentNullThrowHelper.ThrowIfNull(key);

        var value = _dbOperations.GetCacheItem(key);

        ScanForExpiredItemsIfRequired();

        return value;
    }

    bool IBufferDistributedCache.TryGet(string key, IBufferWriter<byte> destination) {
        ArgumentNullThrowHelper.ThrowIfNull(key);
        ArgumentNullThrowHelper.ThrowIfNull(destination);

        var value = _dbOperations.TryGetCacheItem(key, destination);

        ScanForExpiredItemsIfRequired();

        return value;
    }

    /// <inheritdoc />
    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default(CancellationToken)) {
        ArgumentNullThrowHelper.ThrowIfNull(key);

        token.ThrowIfCancellationRequested();

        var value = await _dbOperations.GetCacheItemAsync(key, token).ConfigureAwait(false);

        ScanForExpiredItemsIfRequired();

        return value;
    }

    async ValueTask<bool> IBufferDistributedCache.TryGetAsync(string key, IBufferWriter<byte> destination, CancellationToken token) {
        ArgumentNullThrowHelper.ThrowIfNull(key);
        ArgumentNullThrowHelper.ThrowIfNull(destination);

        var value = await _dbOperations.TryGetCacheItemAsync(key, destination, token).ConfigureAwait(false);

        ScanForExpiredItemsIfRequired();

        return value;
    }

    /// <inheritdoc />
    public void Refresh(string key) {
        ArgumentNullThrowHelper.ThrowIfNull(key);

        _dbOperations.RefreshCacheItem(key);

        ScanForExpiredItemsIfRequired();
    }

    /// <inheritdoc />
    public async Task RefreshAsync(string key, CancellationToken token = default(CancellationToken)) {
        ArgumentNullThrowHelper.ThrowIfNull(key);

        token.ThrowIfCancellationRequested();

        await _dbOperations.RefreshCacheItemAsync(key, token).ConfigureAwait(false);

        ScanForExpiredItemsIfRequired();
    }

    /// <inheritdoc />
    public void Remove(string key) {
        ArgumentNullThrowHelper.ThrowIfNull(key);

        _dbOperations.DeleteCacheItem(key);

        ScanForExpiredItemsIfRequired();
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken token = default(CancellationToken)) {
        ArgumentNullThrowHelper.ThrowIfNull(key);

        token.ThrowIfCancellationRequested();

        await _dbOperations.DeleteCacheItemAsync(key, token).ConfigureAwait(false);

        ScanForExpiredItemsIfRequired();
    }

    /// <inheritdoc />
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) {
        ArgumentNullThrowHelper.ThrowIfNull(key);
        ArgumentNullThrowHelper.ThrowIfNull(value);
        ArgumentNullThrowHelper.ThrowIfNull(options);

        options = EnsureExpiration(options);

        _dbOperations.SetCacheItem(key, new(value), options);

        ScanForExpiredItemsIfRequired();
    }

    void IBufferDistributedCache.Set(string key, ReadOnlySequence<byte> value, DistributedCacheEntryOptions options) {
        ArgumentNullThrowHelper.ThrowIfNull(key);
        ArgumentNullThrowHelper.ThrowIfNull(options);

        options = EnsureExpiration(options);

        _dbOperations.SetCacheItem(key, Linearize(value, out var lease), options);
        Recycle(lease); // we're fine to only recycle on success

        ScanForExpiredItemsIfRequired();
    }

    /// <inheritdoc />
    public async Task SetAsync(
        string key,
        byte[] value,
        DistributedCacheEntryOptions options,
        CancellationToken token = default(CancellationToken)) {
        ArgumentNullThrowHelper.ThrowIfNull(key);
        ArgumentNullThrowHelper.ThrowIfNull(value);
        ArgumentNullThrowHelper.ThrowIfNull(options);

        token.ThrowIfCancellationRequested();

        options = EnsureExpiration(options);

        await _dbOperations.SetCacheItemAsync(key, new(value), options, token).ConfigureAwait(false);

        ScanForExpiredItemsIfRequired();
    }

    /// <summary>
    /// Gets a cached value by key, or creates it once under a transaction-scoped Postgres advisory lock when missing.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="valueFactory">Factory that creates the value if the key is missing.</param>
    /// <param name="options">The cache entry options applied when a value is created.</param>
    /// <param name="token">A cancellation token.</param>
    /// <returns>The existing or newly created cache value.</returns>
    public async Task<byte[]> GetOrCreateAsync(
        string key,
        Func<CancellationToken, Task<byte[]>> valueFactory,
        DistributedCacheEntryOptions options,
        CancellationToken token = default(CancellationToken)) {
        ArgumentNullThrowHelper.ThrowIfNull(key);
        ArgumentNullThrowHelper.ThrowIfNull(valueFactory);
        ArgumentNullThrowHelper.ThrowIfNull(options);

        token.ThrowIfCancellationRequested();

        var existingValue = await _dbOperations.GetCacheItemAsync(key, token).ConfigureAwait(false);
        if (existingValue is not null) {
            ScanForExpiredItemsIfRequired();
            return existingValue;
        }

        options = EnsureExpiration(options);

        var value = await _dbOperations.GetOrCreateCacheItemWithAdvisoryLockAsync(
            key,
            options,
            async cancellationToken => {
                var producedValue = await valueFactory(cancellationToken).ConfigureAwait(false);
                ArgumentNullThrowHelper.ThrowIfNull(producedValue);
                return new ArraySegment<byte>(producedValue);
            },
            token).ConfigureAwait(false);

        ScanForExpiredItemsIfRequired();
        return value;
    }

    async ValueTask IBufferDistributedCache.SetAsync(
        string key,
        ReadOnlySequence<byte> value,
        DistributedCacheEntryOptions options,
        CancellationToken token) {
        ArgumentNullThrowHelper.ThrowIfNull(key);
        ArgumentNullThrowHelper.ThrowIfNull(options);

        token.ThrowIfCancellationRequested();

        options = EnsureExpiration(options);

        await _dbOperations.SetCacheItemAsync(key, Linearize(value, out var lease), options, token).ConfigureAwait(false);
        Recycle(lease); // we're fine to only recycle on success

        ScanForExpiredItemsIfRequired();
    }

    private static ArraySegment<byte> Linearize(in ReadOnlySequence<byte> value, out byte[]? lease) {
        if (value.IsEmpty) {
            lease = null;
            return new([], 0, 0);
        }

        // SqlClient only supports single-segment chunks via byte[] with offset/count; this will
        // almost never be an issue, but on those rare occasions: use a leased array to harmonize things
        // TODO Postgres impact?
        if (value.IsSingleSegment && MemoryMarshal.TryGetArray(value.First, out var segment)) {
            lease = null;
            return segment;
        }
        var length = checked((int)value.Length);
        lease = ArrayPool<byte>.Shared.Rent(length);
        value.CopyTo(lease);
        return new(lease, 0, length);
    }

    private static void Recycle(byte[]? lease) {
        if (lease is not null) {
            ArrayPool<byte>.Shared.Return(lease);
        }
    }

    // Called by multiple actions to see how long it's been since we last checked for expired items.
    // If sufficient time has elapsed then a scan is initiated on a background task.
    private void ScanForExpiredItemsIfRequired() {
        var _now = _timeProvider.GetUtcNow().Ticks;
        var _last = Volatile.Read(ref _lastExpirationScan);

        if ((_now - _last) <= _expiredItemsDeletionInterval.Ticks) {
            return;
        }

        if (Interlocked.CompareExchange(ref _lastExpirationScan, _now, _last) != _last) {
            return;
        }

        if (Interlocked.Exchange(ref _expirationScanRunning, 1) == 1) {
            return;
        }

        _ = Task.Run(() => {
            try { _deleteExpiredCachedItemsDelegate(); }
            finally { Volatile.Write(ref _expirationScanRunning, 0); }
        });
    }

    private void DeleteExpiredCacheItems() {
        _dbOperations.DeleteExpiredCacheItems();
    }

    private DistributedCacheEntryOptions EnsureExpiration(DistributedCacheEntryOptions options)
    {
        return options is
        {
            AbsoluteExpiration: null,
            AbsoluteExpirationRelativeToNow: null,
            SlidingExpiration: null
        }
            ? new DistributedCacheEntryOptions { SlidingExpiration = _defaultSlidingExpiration }
            : options;
    }
}
