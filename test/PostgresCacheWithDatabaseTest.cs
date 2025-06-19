// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Npgsql;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Internal;
using Xunit;

namespace Microsoft.Extensions.Caching.Postgres;

public class PostgresCacheWithDatabaseTest
{
    // These tests are disabled by default. To run them, run the "run-db-tests.ps1" script

    private const string ConnectionStringKey = "ConnectionString";
    private const string SchemaNameKey = "SchemaName";
    private const string TableNameKey = "TableName";
    private const string UseWALKey = "UseWAL";
    private const string CreateIfNotExistsKey = "CreateIfNotExists";
    private const string EnabledEnvVarName = "PGCACHETESTS_ENABLED";
    private readonly string _tableName;
    private readonly string _schemaName;
    private readonly string _connectionString;
    private readonly bool _useWAL;
    private readonly bool _createIfNotExists;

    public PostgresCacheWithDatabaseTest()
    {
        //TODO: where/how/why is this dictionary being used?
        /*
        var memoryConfigurationData = new Dictionary<string, string>
            {
                // If you have to use other parameters for some reason, make sure to update this!
                { ConnectionStringKey, @"Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=postgres" },
                { SchemaNameKey, "public" },
                { TableNameKey, "CacheTest" },
                { CreateIfNotExistsKey, "true" },
            };
        */

        var configurationBuilder = new ConfigurationBuilder();
        configurationBuilder
           //AddInMemoryCollection(memoryConfigurationData)
            .AddEnvironmentVariables(prefix: "PGCACHETESTS_")
            .AddJsonFile("config.json", optional: false, reloadOnChange: true);


        var configuration = configurationBuilder.Build();
        _tableName = configuration[TableNameKey];
        _schemaName = configuration[SchemaNameKey];
        _connectionString = configuration[ConnectionStringKey];
        _useWAL = Convert.ToBoolean(configuration[UseWALKey]);
        _createIfNotExists = Convert.ToBoolean(configuration[CreateIfNotExistsKey]);
    }

    [ConditionalFact]
    [EnvironmentVariableSkipCondition(EnabledEnvVarName, "1")]
    public async Task ReturnsNullValue_ForNonExistingCacheItem()
    {
        // Arrange
        var cache = GetPostgresCache();

        // Act
        var value = await cache.GetAsync("NonExisting");

        // Assert
        Assert.Null(value);
    }

    [ConditionalFact]
    [EnvironmentVariableSkipCondition(EnabledEnvVarName, "1")]
    public async Task SetWithAbsoluteExpirationSetInThePast_Throws()
    {
        // Arrange
        var testClock = new FakeTimeProvider();
        var key = Guid.NewGuid().ToString();
        var expectedValue = Encoding.UTF8.GetBytes("Hello, World!");
        var cache = GetPostgresCache(GetCacheOptions(testClock));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            return cache.SetAsync(
                key,
                expectedValue,
                new DistributedCacheEntryOptions().SetAbsoluteExpiration(testClock.GetUtcNow().AddHours(-1)));
        });
        Assert.Equal("The absolute expiration value must be in the future.", exception.Message);
    }

    [ConditionalFact]
    [EnvironmentVariableSkipCondition(EnabledEnvVarName, "1")]
    public async Task SetCacheItem_SucceedsFor_KeyEqualToMaximumSize()
    {
        // Arrange
        // Create a key with the maximum allowed key length. Here a key of length 898 bytes is created.
        var key = new string('a', NpgsqlParameterCollectionExtensions.CacheItemIdColumnWidth);
        var testClock = new FakeTimeProvider();
        var expectedValue = Encoding.UTF8.GetBytes("Hello, World!");
        var cache = GetPostgresCache(GetCacheOptions(testClock));

        // Act
        await cache.SetAsync(
            key, expectedValue,
            new DistributedCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromMinutes(30)));

        // Assert
        var cacheItem = await GetCacheItemFromDatabaseAsync(key);
        Assert.Equal(expectedValue, cacheItem.Value);

        // Act
        await cache.RemoveAsync(key);

        // Assert
        var cacheItemInfo = await GetCacheItemFromDatabaseAsync(key);
        Assert.Null(cacheItemInfo);
    }

    [ConditionalFact]
    [EnvironmentVariableSkipCondition(EnabledEnvVarName, "1")]
    public async Task SetCacheItem_SucceedsFor_NullAbsoluteAndSlidingExpirationTimes()
    {
        // Arrange
        var key = Guid.NewGuid().ToString();
        var testClock = new FakeTimeProvider();
        var expectedValue = Encoding.UTF8.GetBytes("Hello, World!");
        var cacheOptions = GetCacheOptions(testClock);
        var cache = GetPostgresCache(cacheOptions);
        var expectedExpirationTime = testClock.GetUtcNow().Add(cacheOptions.DefaultSlidingExpiration);

        // Act
        await cache.SetAsync(key, expectedValue, new DistributedCacheEntryOptions()
        {
            AbsoluteExpiration = null,
            AbsoluteExpirationRelativeToNow = null,
            SlidingExpiration = null
        });

        // Assert
        await AssertGetCacheItemFromDatabaseAsync(
                        cache,
                        key,
                        expectedValue,
                        cacheOptions.DefaultSlidingExpiration,
                        absoluteExpiration: null,
                        expectedExpirationTime: expectedExpirationTime);

        var cacheItem = await GetCacheItemFromDatabaseAsync(key);
        Assert.Equal(expectedValue, cacheItem.Value);

        // Act
        await cache.RemoveAsync(key);

        // Assert
        var cacheItemInfo = await GetCacheItemFromDatabaseAsync(key);
        Assert.Null(cacheItemInfo);
    }

    [ConditionalFact]
    [EnvironmentVariableSkipCondition(EnabledEnvVarName, "1")]
    public async Task UpdatedDefaultSlidingExpiration_SetCacheItem_SucceedsFor_NullAbsoluteAndSlidingExpirationTimes()
    {
        // Arrange
        var key = Guid.NewGuid().ToString();
        var testClock = new FakeTimeProvider();
        var expectedValue = Encoding.UTF8.GetBytes("Hello, World!");
        var cacheOptions = GetCacheOptions(testClock);
        cacheOptions.DefaultSlidingExpiration = cacheOptions.DefaultSlidingExpiration.Add(TimeSpan.FromMinutes(10));
        var cache = GetPostgresCache(cacheOptions);
        var expectedExpirationTime = testClock.GetUtcNow().Add(cacheOptions.DefaultSlidingExpiration);

        // Act
        await cache.SetAsync(key, expectedValue, new DistributedCacheEntryOptions()
        {
            AbsoluteExpiration = null,
            AbsoluteExpirationRelativeToNow = null,
            SlidingExpiration = null
        });

        // Assert
        await AssertGetCacheItemFromDatabaseAsync(
                        cache,
                        key,
                        expectedValue,
                        cacheOptions.DefaultSlidingExpiration,
                        absoluteExpiration: null,
                        expectedExpirationTime: expectedExpirationTime);

        var cacheItem = await GetCacheItemFromDatabaseAsync(key);
        Assert.Equal(expectedValue, cacheItem.Value);

        // Act
        await cache.RemoveAsync(key);

        // Assert
        var cacheItemInfo = await GetCacheItemFromDatabaseAsync(key);
        Assert.Null(cacheItemInfo);
    }

    [ConditionalFact]
    [EnvironmentVariableSkipCondition(EnabledEnvVarName, "1")]
    public async Task SetCacheItem_FailsFor_KeyGreaterThanMaximumSize()
    {
        // Arrange
        // Create a key which is greater than the maximum length.
        var key = new string('b', NpgsqlParameterCollectionExtensions.CacheItemIdColumnWidth + 1);
        var testClock = new FakeTimeProvider();
        var expectedValue = Encoding.UTF8.GetBytes("Hello, World!");
        var cache = GetPostgresCache(GetCacheOptions(testClock));

        // Act
        await cache.SetAsync(
            key, expectedValue,
            new DistributedCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromMinutes(30)));

        // Assert
        var cacheItem = await GetCacheItemFromDatabaseAsync(key);
        Assert.Null(cacheItem);
    }
    /*
        // Arrange
        [ConditionalTheory]
        [InlineData(10, 11)]
        [InlineData(10, 30)]

        public async Task SetWithSlidingExpiration_ReturnsNullValue_ForExpiredCacheItem(
            int slidingExpirationWindow, int accessItemAt)
        {
            // Arrange
            var testClock = new FakeTimeProvider();
            var key = Guid.NewGuid().ToString();
            var cache = GetPostgresCache(GetCacheOptions(testClock));
            await cache.SetAsync(
                key,
                Encoding.UTF8.GetBytes("Hello, World!"),
                new DistributedCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromSeconds(slidingExpirationWindow)));

            // set the clock's UtcNow far in future
            testClock.Add(TimeSpan.FromHours(accessItemAt));

            // Act
            var value = await cache.GetAsync(key);

            // Assert
            Assert.Null(value);
        }

        [ConditionalTheory]
        [InlineData(5, 15)]
        [InlineData(10, 20)]

        public async Task SetWithSlidingExpiration_ExtendsExpirationTime(int accessItemAt, int expected)
        {
            // Arrange
            var testClock = new FakeTimeProvider();
            var slidingExpirationWindow = TimeSpan.FromSeconds(10);
            var key = Guid.NewGuid().ToString();
            var cache = GetPostgresCache(GetCacheOptions(testClock));
            var expectedValue = Encoding.UTF8.GetBytes("Hello, World!");
            var expectedExpirationTime = testClock.GetUtcNow().AddSeconds(expected);
            await cache.SetAsync(
                key,
                expectedValue,
                new DistributedCacheEntryOptions().SetSlidingExpiration(slidingExpirationWindow));

            testClock.Add(TimeSpan.FromSeconds(accessItemAt));
            // Act
            await AssertGetCacheItemFromDatabaseAsync(
                cache,
                key,
                expectedValue,
                slidingExpirationWindow,
                absoluteExpiration: null,
                expectedExpirationTime: expectedExpirationTime);
        }

        [ConditionalTheory]
        [InlineData(8)]
        [InlineData(50)]

        public async Task SetWithSlidingExpirationAndAbsoluteExpiration_ReturnsNullValue_ForExpiredCacheItem(
            int accessItemAt)
        {
            // Arrange
            var testClock = new FakeTimeProvider();
            var utcNow = testClock.GetUtcNow();
            var slidingExpiration = TimeSpan.FromSeconds(5);
            var absoluteExpiration = utcNow.Add(TimeSpan.FromSeconds(20));
            var key = Guid.NewGuid().ToString();
            var cache = GetPostgresCache(GetCacheOptions(testClock));
            var expectedValue = Encoding.UTF8.GetBytes("Hello, World!");
            await cache.SetAsync(
                key,
                expectedValue,
                // Set both sliding and absolute expiration
                new DistributedCacheEntryOptions()
                .SetSlidingExpiration(slidingExpiration)
                .SetAbsoluteExpiration(absoluteExpiration));

            // Act
            testClock.Advance(TimeSpan.FromSeconds(accessItemAt));
            var value = await cache.GetAsync(key);

            // Assert
            Assert.Null(value);
        }
    */

    // Arrange
    [ConditionalTheory]
    [InlineData(10, 11)]
    [InlineData(10, 30)]

    public async Task SetWithSlidingExpiration_ReturnsNullValue_ForExpiredCacheItem()
    {
        int slidingExpirationWindow, accessItemAt;
        // Arrange
        var testClock = new FakeTimeProvider();
        var key = Guid.NewGuid().ToString();
        var cache = GetPostgresCache(GetCacheOptions(testClock));

        slidingExpirationWindow = 10; accessItemAt = 11;

        await cache.SetAsync(
            key,
            Encoding.UTF8.GetBytes("Hello, World!"),
            new DistributedCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromSeconds(slidingExpirationWindow)));

        // set the clock's UtcNow far in future
        testClock.Add(TimeSpan.FromHours(accessItemAt));

        // Act
        var value = await cache.GetAsync(key);

        // Assert
        Assert.Null(value);

        slidingExpirationWindow = 10; accessItemAt = 30;

        await cache.SetAsync(
            key,
            Encoding.UTF8.GetBytes("Hello, World!"),
            new DistributedCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromSeconds(slidingExpirationWindow)));

        // set the clock's UtcNow far in future
        testClock.Add(TimeSpan.FromHours(accessItemAt));

        // Act
        value = await cache.GetAsync(key);

        // Assert
        Assert.Null(value);
    }

    [ConditionalTheory]
    [InlineData(5, 15)]
    [InlineData(10, 20)]

    public async Task SetWithSlidingExpiration_ExtendsExpirationTime()
    {
        int accessItemAt, expected;
        // Arrange
        var testClock = new FakeTimeProvider();
        var slidingExpirationWindow = TimeSpan.FromSeconds(10);
        var key = Guid.NewGuid().ToString();
        var cache = GetPostgresCache(GetCacheOptions(testClock));
        var expectedValue = Encoding.UTF8.GetBytes("Hello, World!");

        accessItemAt = 5; expected = 15;
        var expectedExpirationTime = testClock.GetUtcNow().AddSeconds(expected);
        await cache.SetAsync(
            key,
            expectedValue,
            new DistributedCacheEntryOptions().SetSlidingExpiration(slidingExpirationWindow));

        testClock.Add(TimeSpan.FromSeconds(accessItemAt));
        // Act
        await AssertGetCacheItemFromDatabaseAsync(
            cache,
            key,
            expectedValue,
            slidingExpirationWindow,
            absoluteExpiration: null,
            expectedExpirationTime: expectedExpirationTime);

        accessItemAt = 10; expected = 20;
        expectedExpirationTime = testClock.GetUtcNow().AddSeconds(expected);
        await cache.SetAsync(
            key,
            expectedValue,
            new DistributedCacheEntryOptions().SetSlidingExpiration(slidingExpirationWindow));

        testClock.Add(TimeSpan.FromSeconds(accessItemAt));
        // Act
        await AssertGetCacheItemFromDatabaseAsync(
            cache,
            key,
            expectedValue,
            slidingExpirationWindow,
            absoluteExpiration: null,
            expectedExpirationTime: expectedExpirationTime);
    }

    [ConditionalTheory]
    [InlineData(8)]
    [InlineData(50)]

    public async Task SetWithSlidingExpirationAndAbsoluteExpiration_ReturnsNullValue_ForExpiredCacheItem()
    {

        int accessItemAt;

        // Arrange
        var testClock = new FakeTimeProvider();
        var utcNow = testClock.GetUtcNow();
        var slidingExpiration = TimeSpan.FromSeconds(5);
        var absoluteExpiration = utcNow.Add(TimeSpan.FromSeconds(20));
        var key = Guid.NewGuid().ToString();
        var cache = GetPostgresCache(GetCacheOptions(testClock));
        var expectedValue = Encoding.UTF8.GetBytes("Hello, World!");
        await cache.SetAsync(
            key,
            expectedValue,
            // Set both sliding and absolute expiration
            new DistributedCacheEntryOptions()
            .SetSlidingExpiration(slidingExpiration)
            .SetAbsoluteExpiration(absoluteExpiration));


        accessItemAt = 8;
        // Act
        testClock.Advance(TimeSpan.FromSeconds(accessItemAt));
        var value = await cache.GetAsync(key);

        // Assert
        Assert.Null(value);

        accessItemAt = 50;
        // Act
        testClock.Advance(TimeSpan.FromSeconds(accessItemAt));
        value = await cache.GetAsync(key);

        // Assert
        Assert.Null(value);
    }

    [ConditionalFact]
    [EnvironmentVariableSkipCondition(EnabledEnvVarName, "1")]
    public async Task SetWithAbsoluteExpirationRelativeToNow_ReturnsNullValue_ForExpiredCacheItem()
    {
        // Arrange
        var testClock = new FakeTimeProvider();
        var key = Guid.NewGuid().ToString();
        var cache = GetPostgresCache(GetCacheOptions(testClock));
        await cache.SetAsync(
            key,
            Encoding.UTF8.GetBytes("Hello, World!"),
            new DistributedCacheEntryOptions().SetAbsoluteExpiration(relative: TimeSpan.FromSeconds(10)));

        // set the clock's UtcNow far in future
        testClock.Add(TimeSpan.FromHours(10));

        // Act
        var value = await cache.GetAsync(key);

        // Assert
        Assert.Null(value);
    }

    [ConditionalFact]
    [EnvironmentVariableSkipCondition(EnabledEnvVarName, "1")]
    public async Task SetWithAbsoluteExpiration_ReturnsNullValue_ForExpiredCacheItem()
    {
        // Arrange
        var testClock = new FakeTimeProvider();
        var key = Guid.NewGuid().ToString();
        var cache = GetPostgresCache(GetCacheOptions(testClock));
        await cache.SetAsync(
            key,
            Encoding.UTF8.GetBytes("Hello, World!"),
            new DistributedCacheEntryOptions()
            .SetAbsoluteExpiration(absolute: testClock.GetUtcNow().Add(TimeSpan.FromSeconds(30))));

        // set the clock's UtcNow far in future
        testClock.Add(TimeSpan.FromHours(10));

        // Act
        var value = await cache.GetAsync(key);

        // Assert
        Assert.Null(value);
    }

    [ConditionalFact]
    [EnvironmentVariableSkipCondition(EnabledEnvVarName, "1")]
    public async Task DoesNotThrowException_WhenOnlyAbsoluteExpirationSupplied_AbsoluteExpirationRelativeToNow()
    {
        // Arrange
        var testClock = new FakeTimeProvider();
        var absoluteExpirationRelativeToUtcNow = TimeSpan.FromSeconds(10);
        var key = Guid.NewGuid().ToString();
        var cache = GetPostgresCache(GetCacheOptions(testClock));
        var expectedValue = Encoding.UTF8.GetBytes("Hello, World!");
        var expectedAbsoluteExpiration = testClock.GetUtcNow().Add(absoluteExpirationRelativeToUtcNow);
        // Act
        await cache.SetAsync(
            key,
            expectedValue,
            new DistributedCacheEntryOptions()
            .SetAbsoluteExpiration(relative: absoluteExpirationRelativeToUtcNow));

        // Assert
        await AssertGetCacheItemFromDatabaseAsync(
            cache,
            key,
            expectedValue,
            slidingExpiration: null,
            absoluteExpiration: expectedAbsoluteExpiration,
            expectedExpirationTime: expectedAbsoluteExpiration);
    }

    [ConditionalFact]
    [EnvironmentVariableSkipCondition(EnabledEnvVarName, "1")]
    public async Task DoesNotThrowException_WhenOnlyAbsoluteExpirationSupplied_AbsoluteExpiration()
    {
        // Arrange
        var testClock = new FakeTimeProvider();
        var expectedAbsoluteExpiration = new DateTimeOffset(2025, 1, 1, 1, 0, 0, TimeSpan.Zero);
        var key = Guid.NewGuid().ToString();
        var cache = GetPostgresCache();
        var expectedValue = Encoding.UTF8.GetBytes("Hello, World!");

        // Act
        await cache.SetAsync(
            key,
            expectedValue,
            new DistributedCacheEntryOptions()
            .SetAbsoluteExpiration(absolute: expectedAbsoluteExpiration));

        // Assert
        await AssertGetCacheItemFromDatabaseAsync(
            cache,
            key,
            expectedValue,
            slidingExpiration: null,
            absoluteExpiration: expectedAbsoluteExpiration,
            expectedExpirationTime: expectedAbsoluteExpiration);
    }

    [ConditionalFact]
    [EnvironmentVariableSkipCondition(EnabledEnvVarName, "1")]
    public async Task SetCacheItem_UpdatesAbsoluteExpirationTime()
    {
        // Arrange
        var testClock = new FakeTimeProvider();
        var key = Guid.NewGuid().ToString();
        var cache = GetPostgresCache(GetCacheOptions(testClock));
        var expectedValue = Encoding.UTF8.GetBytes("Hello, World!");
        var absoluteExpiration = testClock.GetUtcNow().Add(TimeSpan.FromSeconds(10));

        // Act & Assert
        // Creates a new item
        await cache.SetAsync(
            key,
            expectedValue,
            new DistributedCacheEntryOptions().SetAbsoluteExpiration(absoluteExpiration));
        await AssertGetCacheItemFromDatabaseAsync(
            cache,
            key,
            expectedValue,
            slidingExpiration: null,
            absoluteExpiration: absoluteExpiration,
            expectedExpirationTime: absoluteExpiration);

        // Updates an existing item with new absolute expiration time
        absoluteExpiration = testClock.GetUtcNow().Add(TimeSpan.FromMinutes(30));
        await cache.SetAsync(
            key,
            expectedValue,
            new DistributedCacheEntryOptions().SetAbsoluteExpiration(absoluteExpiration));
        await AssertGetCacheItemFromDatabaseAsync(
            cache,
            key,
            expectedValue,
            slidingExpiration: null,
            absoluteExpiration: absoluteExpiration,
            expectedExpirationTime: absoluteExpiration);
    }

    [ConditionalFact]
    [EnvironmentVariableSkipCondition(EnabledEnvVarName, "1")]
    public async Task SetCacheItem_WithValueLargerThan_DefaultColumnWidth()
    {
        // Arrange
        var testClock = new FakeTimeProvider();
        var key = Guid.NewGuid().ToString();
        var cache = GetPostgresCache(GetCacheOptions(testClock));
        var expectedValue = new byte[NpgsqlParameterCollectionExtensions.DefaultValueColumnWidth + 100];
        var absoluteExpiration = testClock.GetUtcNow().Add(TimeSpan.FromSeconds(10));

        // Act
        // Creates a new item
        await cache.SetAsync(
            key,
            expectedValue,
            new DistributedCacheEntryOptions().SetAbsoluteExpiration(absoluteExpiration));

        // Assert
        await AssertGetCacheItemFromDatabaseAsync(
            cache,
            key,
            expectedValue,
            slidingExpiration: null,
            absoluteExpiration: absoluteExpiration,
            expectedExpirationTime: absoluteExpiration);
    }

    [ConditionalFact]
    [EnvironmentVariableSkipCondition(EnabledEnvVarName, "1")]
    public async Task ExtendsExpirationTime_ForSlidingExpiration()
    {
        // Arrange
        var testClock = new FakeTimeProvider();
        var slidingExpiration = TimeSpan.FromSeconds(10);
        var key = Guid.NewGuid().ToString();
        var cache = GetPostgresCache(GetCacheOptions(testClock));
        var expectedValue = Encoding.UTF8.GetBytes("Hello, World!");
        // The operations Set and Refresh here extend the sliding expiration 2 times.
        var expectedExpiresAtTime = testClock.GetUtcNow().AddSeconds(15);
        await cache.SetAsync(
            key,
            expectedValue,
            new DistributedCacheEntryOptions().SetSlidingExpiration(slidingExpiration));

        // Act
        testClock.Add(TimeSpan.FromSeconds(5));
        await cache.RefreshAsync(key);

        // Assert
        // verify if the expiration time in database is set as expected
        var cacheItemInfo = await GetCacheItemFromDatabaseAsync(key);
        Assert.NotNull(cacheItemInfo);
        Assert.Equal(slidingExpiration, cacheItemInfo.SlidingExpirationInSeconds);
        Assert.Null(cacheItemInfo.AbsoluteExpiration);
        Assert.Equal(expectedExpiresAtTime, cacheItemInfo.ExpiresAtTime);
    }

    [ConditionalFact]
    [EnvironmentVariableSkipCondition(EnabledEnvVarName, "1")]
    public async Task GetItem_SlidingExpirationDoesNot_ExceedAbsoluteExpirationIfSet()
    {
        // Arrange
        var testClock = new FakeTimeProvider();
        var utcNow = testClock.GetUtcNow();
        var slidingExpiration = TimeSpan.FromSeconds(5);
        var absoluteExpiration = utcNow.Add(TimeSpan.FromSeconds(20));
        var key = Guid.NewGuid().ToString();
        var cache = GetPostgresCache(GetCacheOptions(testClock));
        var expectedValue = Encoding.UTF8.GetBytes("Hello, World!");
        await cache.SetAsync(
            key,
            expectedValue,
            // Set both sliding and absolute expiration
            new DistributedCacheEntryOptions()
            .SetSlidingExpiration(slidingExpiration)
            .SetAbsoluteExpiration(absoluteExpiration));

        // Act && Assert
        var cacheItemInfo = await GetCacheItemFromDatabaseAsync(key);
        Assert.NotNull(cacheItemInfo);
        Assert.Equal(utcNow.AddSeconds(5), cacheItemInfo.ExpiresAtTime);

        // Accessing item at time...
        utcNow = testClock.Add(TimeSpan.FromSeconds(5)).ToUniversalTime();
        await AssertGetCacheItemFromDatabaseAsync(
            cache,
            key,
            expectedValue,
            slidingExpiration,
            absoluteExpiration,
            expectedExpirationTime: utcNow.AddSeconds(5));

        // Accessing item at time...
        utcNow = testClock.Add(TimeSpan.FromSeconds(5)).ToUniversalTime();
        await AssertGetCacheItemFromDatabaseAsync(
            cache,
            key,
            expectedValue,
            slidingExpiration,
            absoluteExpiration,
            expectedExpirationTime: utcNow.AddSeconds(5));

        // Accessing item at time...
        utcNow = testClock.Add(TimeSpan.FromSeconds(5)).ToUniversalTime();
        // The expiration extension must not exceed the absolute expiration
        await AssertGetCacheItemFromDatabaseAsync(
            cache,
            key,
            expectedValue,
            slidingExpiration,
            absoluteExpiration,
            expectedExpirationTime: absoluteExpiration);
    }

    [ConditionalFact]
    [EnvironmentVariableSkipCondition(EnabledEnvVarName, "1")]
    public async Task DoestNotExtendsExpirationTime_ForAbsoluteExpiration()
    {
        // Arrange
        var testClock = new FakeTimeProvider();
        var absoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
        var expectedExpiresAtTime = testClock.GetUtcNow().Add(absoluteExpirationRelativeToNow);
        var key = Guid.NewGuid().ToString();
        var cache = GetPostgresCache(GetCacheOptions(testClock));
        var expectedValue = Encoding.UTF8.GetBytes("Hello, World!");
        await cache.SetAsync(
            key,
            expectedValue,
            new DistributedCacheEntryOptions().SetAbsoluteExpiration(absoluteExpirationRelativeToNow));
        testClock.Add(TimeSpan.FromSeconds(25));

        // Act
        var value = await cache.GetAsync(key);

        // Assert
        Assert.NotNull(value);
        Assert.Equal(expectedValue, value);

        // verify if the expiration time in database is set as expected
        var cacheItemInfo = await GetCacheItemFromDatabaseAsync(key);
        Assert.NotNull(cacheItemInfo);
        Assert.Equal(expectedExpiresAtTime, cacheItemInfo.ExpiresAtTime);
    }

    [ConditionalFact]
    [EnvironmentVariableSkipCondition(EnabledEnvVarName, "1")]
    public async Task RefreshItem_ExtendsExpirationTime_ForSlidingExpiration()
    {
        // Arrange
        var testClock = new FakeTimeProvider();
        var slidingExpiration = TimeSpan.FromSeconds(10);
        var key = Guid.NewGuid().ToString();
        var cache = GetPostgresCache(GetCacheOptions(testClock));
        var expectedValue = Encoding.UTF8.GetBytes("Hello, World!");
        // The operations Set and Refresh here extend the sliding expiration 2 times.
        var expectedExpiresAtTime = testClock.GetUtcNow().AddSeconds(15);
        await cache.SetAsync(
            key,
            expectedValue,
            new DistributedCacheEntryOptions().SetSlidingExpiration(slidingExpiration));

        // Act
        testClock.Add(TimeSpan.FromSeconds(5));
        await cache.RefreshAsync(key);

        // Assert
        // verify if the expiration time in database is set as expected
        var cacheItemInfo = await GetCacheItemFromDatabaseAsync(key);
        Assert.NotNull(cacheItemInfo);
        Assert.Equal(slidingExpiration, cacheItemInfo.SlidingExpirationInSeconds);
        Assert.Null(cacheItemInfo.AbsoluteExpiration);
        Assert.Equal(expectedExpiresAtTime, cacheItemInfo.ExpiresAtTime);
    }

    [ConditionalFact]
    [EnvironmentVariableSkipCondition(EnabledEnvVarName, "1")]
    public async Task GetCacheItem_IsCaseSensitive()
    {
        // Arrange
        var key = Guid.NewGuid().ToString().ToLower(CultureInfo.InvariantCulture); // lower case
        var cache = GetPostgresCache();
        await cache.SetAsync(
            key,
            Encoding.UTF8.GetBytes("Hello, World!"),
            new DistributedCacheEntryOptions().SetAbsoluteExpiration(relative: TimeSpan.FromHours(1)));

        // Act
        var value = await cache.GetAsync(key.ToUpper(CultureInfo.InvariantCulture)); // key made upper case

        // Assert
        Assert.Null(value);
    }

    [ConditionalFact]
    [EnvironmentVariableSkipCondition(EnabledEnvVarName, "1")]
    public async Task GetCacheItem_DoesNotTrimTrailingSpaces()
    {
        // Arrange
        var key = string.Format(CultureInfo.InvariantCulture, "  {0}  ", Guid.NewGuid()); // with trailing spaces
        var cache = GetPostgresCache();
        var expectedValue = Encoding.UTF8.GetBytes("Hello, World!");
        await cache.SetAsync(
            key,
            expectedValue,
            new DistributedCacheEntryOptions().SetAbsoluteExpiration(relative: TimeSpan.FromHours(1)));

        // Act
        var value = await cache.GetAsync(key);

        // Assert
        Assert.NotNull(value);
        Assert.Equal(expectedValue, value);
    }

    [ConditionalFact]
    [EnvironmentVariableSkipCondition(EnabledEnvVarName, "1")]
    public async Task DeletesCacheItem_OnExplicitlyCalled()
    {
        // Arrange
        var key = Guid.NewGuid().ToString();
        var cache = GetPostgresCache();
        await cache.SetAsync(
            key,
            Encoding.UTF8.GetBytes("Hello, World!"),
            new DistributedCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromSeconds(10)));

        // Act
        await cache.RemoveAsync(key);

        // Assert
        var cacheItemInfo = await GetCacheItemFromDatabaseAsync(key);
        Assert.Null(cacheItemInfo);
    }

    private IDistributedCache GetPostgresCache(PostgresCacheOptions options = null)
    {
        if (options == null)
        {
            options = GetCacheOptions();
        }

        return new PostgresCache(options);
    }

    private PostgresCacheOptions GetCacheOptions(TimeProvider testClock = null)
    {
        return new PostgresCacheOptions()
        {
            ConnectionString = _connectionString,
            SchemaName = _schemaName,
            TableName = _tableName,
            TimeProvider = testClock ?? new FakeTimeProvider(),
            ExpiredItemsDeletionInterval = TimeSpan.FromHours(2)
        };
    }

    private async Task AssertGetCacheItemFromDatabaseAsync(
        IDistributedCache cache,
        string key,
        byte[] expectedValue,
        TimeSpan? slidingExpiration,
        DateTimeOffset? absoluteExpiration,
        DateTimeOffset expectedExpirationTime)
    {
        var value = await cache.GetAsync(key);
        Assert.NotNull(value);
        Assert.Equal(expectedValue, value);
        var cacheItemInfo = await GetCacheItemFromDatabaseAsync(key);
        Assert.NotNull(cacheItemInfo);
        Assert.Equal(slidingExpiration, cacheItemInfo.SlidingExpirationInSeconds);
        Assert.Equal(absoluteExpiration, cacheItemInfo.AbsoluteExpiration);
        Assert.Equal(expectedExpirationTime, cacheItemInfo.ExpiresAtTime);
    }

    private async Task<CacheItemInfo> GetCacheItemFromDatabaseAsync(string key)
    {
        using (var connection = new NpgsqlConnection(_connectionString))
        {
            var command = new NpgsqlCommand(
                $"SELECT Id, Value, ExpiresAtTime, SlidingExpirationInSeconds, AbsoluteExpiration " +
                $"FROM {_tableName} WHERE Id = @Id",
                connection);
            command.Parameters.AddWithValue("Id", key);

            await connection.OpenAsync();

            var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow);

            // NOTE: The following code is made to run on Mono as well because of which
            // we cannot use GetFieldValueAsync etc.
            if (await reader.ReadAsync())
            {
                var cacheItemInfo = new CacheItemInfo
                {
                    Id = key,
                    Value = (byte[])reader[1],
                    ExpiresAtTime = DateTimeOffset.Parse(reader[2].ToString(), CultureInfo.InvariantCulture)
                };

                if (!await reader.IsDBNullAsync(3))
                {
                    cacheItemInfo.SlidingExpirationInSeconds = TimeSpan.FromSeconds(reader.GetInt64(3));
                }

                if (!await reader.IsDBNullAsync(4))
                {
                    cacheItemInfo.AbsoluteExpiration = DateTimeOffset.Parse(reader[4].ToString(), CultureInfo.InvariantCulture);
                }

                return cacheItemInfo;
            }
            else
            {
                return null;
            }
        }
    }
}

internal class ConditionalTheoryAttribute : ConditionalFactAttribute
{
}

internal class ConditionalFactAttribute : FactAttribute
{
}

internal class EnvironmentVariableSkipConditionAttribute : Attribute
{
    private string enabledEnvVarName;
    private string v;

    public EnvironmentVariableSkipConditionAttribute(string enabledEnvVarName, string v)
    {
        this.enabledEnvVarName = enabledEnvVarName;
        this.v = v;
    }
}