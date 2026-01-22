// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Microsoft.Extensions.Caching.Postgres;

/// <summary>
/// Configuration options for <see cref="PostgresCache"/>.
/// </summary>
public class PostgresCacheOptions : IOptions<PostgresCacheOptions> {
    /// <summary>
    /// An abstraction to represent the clock of a machine in order to enable unit testing.
    /// </summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>
    /// The periodic interval to scan and delete expired items in the cache. Default is 30 minutes.
    /// </summary>
    public TimeSpan? ExpiredItemsDeletionInterval { get; set; }

    /// <summary>
    /// The connection string to the database.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Optional callback to configure an <see cref="NpgsqlDataSourceBuilder"/> created from <see cref="ConnectionString"/>.
    /// This allows callers to supply custom authentication (e.g., Entra ID via periodic password provider),
    /// plugins, or other data source settings.
    /// </summary>
    public Action<NpgsqlDataSourceBuilder>? ConfigureDataSourceBuilder { get; set; }

    /// <summary>
    /// Optional pre-configured <see cref="NpgsqlDataSource"/>. When provided, this takes precedence over
    /// <see cref="ConnectionString"/> and <see cref="ConfigureDataSourceBuilder"/>.
    /// </summary>
    public NpgsqlDataSource? DataSource { get; set; }

    /// <summary>
    /// The schema name of the table.
    /// </summary>
    public string? SchemaName { get; set; }

    /// <summary>
    /// Name of the table where the cache items are stored.
    /// </summary>
    public string? TableName { get; set; }

    /// <summary>
    /// Boolean value indicating whether to use a write-ahead log (WAL) for the table.
    /// </summary>
    public bool? UseWAL { get; set; }

    /// <summary>
    /// Boolean value indicating whether to create the table and index if it does not exist.
    /// </summary>
    public bool? CreateIfNotExists { get; set; }

    /// <summary>
    /// The default sliding expiration set for a cache entry if neither Absolute or SlidingExpiration has been set explicitly.
    /// By default, its 20 minutes.
    /// </summary>
    public TimeSpan DefaultSlidingExpiration { get; set; } = TimeSpan.FromMinutes(20);

    PostgresCacheOptions IOptions<PostgresCacheOptions>.Value {
        get {
            return this;
        }
    }
}
