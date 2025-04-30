// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Postgres;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for setting up Postgres distributed cache services in an <see cref="IServiceCollection" />.
/// </summary>
public static class PostgresCachingServicesExtensions
{
    /// <summary>
    /// Adds Postgres distributed caching services to the specified <see cref="IServiceCollection" />.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
    /// <param name="setupAction">An <see cref="Action{PostgresCacheOptions}"/> to configure the provided <see cref="PostgresCacheOptions"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddDistributedPostgresCache(this IServiceCollection services, Action<PostgresCacheOptions> setupAction)
    {
        ArgumentNullThrowHelper.ThrowIfNull(services);
        ArgumentNullThrowHelper.ThrowIfNull(setupAction);

        services.AddOptions();
        AddPostgresCacheServices(services);
        services.Configure(setupAction);

        return services;
    }

    // to enable unit testing
    internal static void AddPostgresCacheServices(IServiceCollection services)
    {
        services.Add(ServiceDescriptor.Singleton<IDistributedCache, PostgresCache>());
    }
}
