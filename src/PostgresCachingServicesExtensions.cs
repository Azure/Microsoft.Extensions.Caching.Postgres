// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Postgres;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for setting up Postgres distributed cache services in an <see cref="IServiceCollection" />.
/// </summary>
public static class PostgresCachingServicesExtensions {
    /// <summary>
    /// Adds Postgres distributed caching services to the specified <see cref="IServiceCollection" />.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
    /// <param name="setupAction">An <see cref="Action{PostgresCacheOptions}"/> to configure the provided <see cref="PostgresCacheOptions"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddDistributedPostgresCache(this IServiceCollection services, Action<PostgresCacheOptions> setupAction) {
        ArgumentNullThrowHelper.ThrowIfNull(services);
        ArgumentNullThrowHelper.ThrowIfNull(setupAction);

        services.AddOptions();
        AddPostgresCacheServices(services);
        services.Configure(setupAction);

        return services;
    }

    /// <summary>
    /// Adds Postgres distributed caching services allowing configuration of <see cref="NpgsqlDataSourceBuilder"/>
    /// for advanced scenarios (e.g., Azure Entra authentication, plugins, custom pooling).
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Options configure action.</param>
    /// <param name="configureDataSourceBuilder">Optional callback to customize the data source builder.</param>
    public static IServiceCollection AddDistributedPostgresCache(this IServiceCollection services,
        Action<PostgresCacheOptions> configure,
        Action<NpgsqlDataSourceBuilder> configureDataSourceBuilder) {
        ArgumentNullThrowHelper.ThrowIfNull(services);
        ArgumentNullThrowHelper.ThrowIfNull(configure);
        ArgumentNullThrowHelper.ThrowIfNull(configureDataSourceBuilder);

        services.AddOptions();
        AddPostgresCacheServices(services);
        services.Configure<PostgresCacheOptions>(opts => {
            configure(opts);
            opts.ConfigureDataSourceBuilder = configureDataSourceBuilder;
        });

        return services;
    }

    /// <summary>
    /// Adds Postgres distributed caching services using an already configured <see cref="NpgsqlDataSource"/>.
    /// The provided data source takes precedence over ConnectionString and ConfigureDataSourceBuilder.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="dataSource">Pre-configured NpgsqlDataSource to use for the cache.</param>
    public static IServiceCollection AddDistributedPostgresCache(this IServiceCollection services, NpgsqlDataSource dataSource) {
        ArgumentNullThrowHelper.ThrowIfNull(services);
        ArgumentNullThrowHelper.ThrowIfNull(dataSource);

        services.AddOptions();
        AddPostgresCacheServices(services);
        // Register the data source in DI so consumers can resolve it if needed
        services.AddSingleton<NpgsqlDataSource>(dataSource);
        services.Configure<PostgresCacheOptions>(opts => {
            opts.DataSource = dataSource;
        });

        return services;
    }

    /// <summary>
    /// Adds Postgres distributed caching services allowing configuration of <see cref="PostgresCacheOptions"/>
    /// and using a pre-configured <see cref="NpgsqlDataSource"/>. The provided data source takes precedence
    /// over ConnectionString and ConfigureDataSourceBuilder.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Options configure action.</param>
    /// <param name="dataSource">Pre-configured NpgsqlDataSource to use for the cache.</param>
    public static IServiceCollection AddDistributedPostgresCache(this IServiceCollection services,
        Action<PostgresCacheOptions> configure,
        NpgsqlDataSource dataSource) {
        ArgumentNullThrowHelper.ThrowIfNull(services);
        ArgumentNullThrowHelper.ThrowIfNull(configure);
        ArgumentNullThrowHelper.ThrowIfNull(dataSource);

        services.AddOptions();
        AddPostgresCacheServices(services);
        services.AddSingleton<NpgsqlDataSource>(dataSource);
        services.Configure<PostgresCacheOptions>(opts => {
            configure(opts);
            opts.DataSource = dataSource;
        });

        return services;
    }

    /// <summary>
    /// Adds Postgres distributed caching services using a factory to create the <see cref="NpgsqlDataSource"/>.
    /// The factory will be registered as a singleton and the resulting data source will be used to configure
    /// the cache options.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="dataSourceFactory">Factory that creates the NpgsqlDataSource using the service provider.</param>
    public static IServiceCollection AddDistributedPostgresCache(this IServiceCollection services, Func<IServiceProvider, NpgsqlDataSource> dataSourceFactory) {
        ArgumentNullThrowHelper.ThrowIfNull(services);
        ArgumentNullThrowHelper.ThrowIfNull(dataSourceFactory);

        services.AddOptions();
        AddPostgresCacheServices(services);
        // Register the data source as a singleton resolved by the provided factory
        services.AddSingleton<NpgsqlDataSource>(dataSourceFactory);
        // Configure PostgresCacheOptions to pull the data source from DI when options are constructed
        services.AddSingleton<IConfigureOptions<PostgresCacheOptions>>(sp => new ConfigureOptions<PostgresCacheOptions>(opts => {
            opts.DataSource = sp.GetRequiredService<NpgsqlDataSource>();
        }));

        return services;
    }

    /// <summary>
    /// Adds Postgres distributed caching services allowing configuration of <see cref="PostgresCacheOptions"/>
    /// and using a factory to create the <see cref="NpgsqlDataSource"/>. The factory will be registered as a singleton
    /// and the resulting data source will be used to configure the cache options.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Options configure action.</param>
    /// <param name="dataSourceFactory">Factory that creates the NpgsqlDataSource using the service provider.</param>
    public static IServiceCollection AddDistributedPostgresCache(this IServiceCollection services,
        Action<PostgresCacheOptions> configure,
        Func<IServiceProvider, NpgsqlDataSource> dataSourceFactory) {
        ArgumentNullThrowHelper.ThrowIfNull(services);
        ArgumentNullThrowHelper.ThrowIfNull(configure);
        ArgumentNullThrowHelper.ThrowIfNull(dataSourceFactory);

        services.AddOptions();
        AddPostgresCacheServices(services);
        services.AddSingleton<NpgsqlDataSource>(dataSourceFactory);
        services.AddSingleton<IConfigureOptions<PostgresCacheOptions>>(sp => new ConfigureOptions<PostgresCacheOptions>(opts => {
            configure(opts);
            opts.DataSource = sp.GetRequiredService<NpgsqlDataSource>();
        }));

        return services;
    }

    // to enable unit testing
    internal static void AddPostgresCacheServices(IServiceCollection services) {
        services.Add(ServiceDescriptor.Singleton<IDistributedCache, PostgresCache>());
    }
}
