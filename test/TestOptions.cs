// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.Caching.Postgres;

internal class TestPostgresCacheOptions : IOptions<PostgresCacheOptions>
{
    private readonly PostgresCacheOptions _innerOptions;

    public TestPostgresCacheOptions(PostgresCacheOptions innerOptions)
    {
        _innerOptions = innerOptions;
    }

    public PostgresCacheOptions Value
    {
        get
        {
            return _innerOptions;
        }
    }
}
