// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;
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
            options.ConnectionString = "Host=Fake;Username=Fake;Password=Fake;Database=Fake;";
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

    [Fact]
    public void AddDistributedPostgresCache_WithDataSource_RegistersDataSourceDescriptor()
    {
        var services = new ServiceCollection();
        var dataSource = NpgsqlDataSource.Create("Host=Fake;Username=Fake;Password=Fake;Database=Fake;");

        services.AddDistributedPostgresCache(dataSource);

        var dsDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(NpgsqlDataSource));
        Assert.NotNull(dsDescriptor);
        Assert.Equal(ServiceLifetime.Singleton, dsDescriptor.Lifetime);
        Assert.Same(dataSource, dsDescriptor.ImplementationInstance);
    }

    [Fact]
    public void AddDistributedPostgresCache_WithConfigureAndDataSource_BuildsPostgresCache()
    {
        var services = new ServiceCollection();
        var dataSource = NpgsqlDataSource.Create("Host=Fake;Username=Fake;Password=Fake;Database=Fake;");

        services.AddDistributedPostgresCache(opts =>
        {
            opts.SchemaName = "Fake";
            opts.TableName = "Fake";
        }, dataSource);

        var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IDistributedCache>();

        Assert.IsType<PostgresCache>(cache);
    }

    [Fact]
    public void AddDistributedPostgresCache_WithFactory_ResolvesDataSourceAndCreatesCache()
    {
        var services = new ServiceCollection();

        services.AddDistributedPostgresCache(opts =>
        {
            opts.SchemaName = "Fake";
            opts.TableName = "Fake";
        }, sp => NpgsqlDataSource.Create("Host=Fake;Username=Fake;Password=Fake;Database=Fake;"));

        var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IDistributedCache>();

        Assert.IsType<PostgresCache>(cache);

        // verify the data source is registered as singleton
        var ds = provider.GetRequiredService<NpgsqlDataSource>();
        Assert.NotNull(ds);
    }

    [Fact]
    public void AddDistributedPostgresCache_NullArguments_Throw()
    {
        // Arrange
        var services = new ServiceCollection();
        var dataSource = NpgsqlDataSource.Create("Host=Fake;Username=Fake;Password=Fake;Database=Fake;");

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => PostgresCachingServicesExtensions.AddDistributedPostgresCache(null as IServiceCollection, (Action<PostgresCacheOptions>)(_ => { })));
        Assert.Throws<ArgumentNullException>(() => services.AddDistributedPostgresCache((Action<PostgresCacheOptions>)null));

        Assert.Throws<ArgumentNullException>(() => PostgresCachingServicesExtensions.AddDistributedPostgresCache(null as IServiceCollection, (Action<PostgresCacheOptions>)(o => { }), (Action<NpgsqlDataSourceBuilder>)(b => { })));
        Assert.Throws<ArgumentNullException>(() => services.AddDistributedPostgresCache((Action<PostgresCacheOptions>)(_ => { }), (Action<NpgsqlDataSourceBuilder>)null));

        Assert.Throws<ArgumentNullException>(() => PostgresCachingServicesExtensions.AddDistributedPostgresCache(null as IServiceCollection, (NpgsqlDataSource)null));
        Assert.Throws<ArgumentNullException>(() => services.AddDistributedPostgresCache((NpgsqlDataSource)null));

        Assert.Throws<ArgumentNullException>(() => PostgresCachingServicesExtensions.AddDistributedPostgresCache(null as IServiceCollection, (Func<IServiceProvider, NpgsqlDataSource>)null));
        Assert.Throws<ArgumentNullException>(() => services.AddDistributedPostgresCache((Func<IServiceProvider, NpgsqlDataSource>)null));

        Assert.Throws<ArgumentNullException>(() => PostgresCachingServicesExtensions.AddDistributedPostgresCache(null as IServiceCollection, (Action<PostgresCacheOptions>)(o => { }), dataSource));
        Assert.Throws<ArgumentNullException>(() => services.AddDistributedPostgresCache((Action<PostgresCacheOptions>)null, dataSource));
        Assert.Throws<ArgumentNullException>(() => services.AddDistributedPostgresCache(opts => { }, (NpgsqlDataSource)null));

        Assert.Throws<ArgumentNullException>(() => PostgresCachingServicesExtensions.AddDistributedPostgresCache(null as IServiceCollection, (Action<PostgresCacheOptions>)(o => { }), (Func<IServiceProvider, NpgsqlDataSource>)(sp => dataSource)));
        Assert.Throws<ArgumentNullException>(() => services.AddDistributedPostgresCache((Action<PostgresCacheOptions>)null, sp => dataSource));
        Assert.Throws<ArgumentNullException>(() => services.AddDistributedPostgresCache(opts => { }, (Func<IServiceProvider, NpgsqlDataSource>)null));
    }

    [Fact]
    public void AddDistributedPostgresCache_Chaining_ForOverloads()
    {
        var services = new ServiceCollection();
        var dataSource = NpgsqlDataSource.Create("Host=Fake;Username=Fake;Password=Fake;Database=Fake;");

        Assert.Same(services, services.AddDistributedPostgresCache(dataSource));
        Assert.Same(services, services.AddDistributedPostgresCache(opts => { }, dataSource));
        Assert.Same(services, services.AddDistributedPostgresCache(sp => dataSource));
        Assert.Same(services, services.AddDistributedPostgresCache(opts => { }, sp => dataSource));
    }

    [Fact]
    public void AddDistributedPostgresCache_OptionsContainDataSource_InstanceAndFactory()
    {
        var services = new ServiceCollection();
        var dataSource = NpgsqlDataSource.Create("Host=Fake;Username=Fake;Password=Fake;Database=Fake;");

        services.AddDistributedPostgresCache(dataSource);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<PostgresCacheOptions>>();
        Assert.Same(dataSource, options.Value.DataSource);

        // factory
        services = new ServiceCollection();
        services.AddDistributedPostgresCache(sp => dataSource);
        provider = services.BuildServiceProvider();
        options = provider.GetRequiredService<IOptions<PostgresCacheOptions>>();
        Assert.Same(provider.GetRequiredService<NpgsqlDataSource>(), options.Value.DataSource);
    }

}
