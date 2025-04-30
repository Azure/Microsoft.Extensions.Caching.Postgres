// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Microsoft.Extensions.Caching.Postgres;

public class PostgresCacheServicesExtensionsTest
{
    [Fact]
    public void AddDistributedPostgresCache_AddsAsSingleRegistrationService()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        PostgresCachingServicesExtensions.AddPostgresCacheServices(services);

        // Assert
        var serviceDescriptor = Assert.Single(services);
        Assert.Equal(typeof(IDistributedCache), serviceDescriptor.ServiceType);
        Assert.Equal(typeof(PostgresCache), serviceDescriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, serviceDescriptor.Lifetime);
    }

    [Fact]
    public void AddDistributedPostgresCache_ReplacesPreviouslyUserRegisteredServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped(typeof(IDistributedCache), sp => Mock.Of<IDistributedCache>());

        // Act
        services.AddDistributedPostgresCache(options =>
        {
            options.ConnectionString = "Fake";
            options.SchemaName = "Fake";
            options.TableName = "Fake";
        });

        // Assert
        var serviceProvider = services.BuildServiceProvider();

        var distributedCache = services.FirstOrDefault(desc => desc.ServiceType == typeof(IDistributedCache));

        Assert.NotNull(distributedCache);
        Assert.Equal(ServiceLifetime.Scoped, distributedCache.Lifetime);
        Assert.IsType<PostgresCache>(serviceProvider.GetRequiredService<IDistributedCache>());
    }

    [Fact]
    public void AddDistributedPostgresCache_allows_chaining()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddDistributedPostgresCache(_ => { }));
    }
}
