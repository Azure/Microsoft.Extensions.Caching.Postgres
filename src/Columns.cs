// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Extensions.Caching.Postgres;

internal static class Columns
{
    public static class Names
    {
        public const string CacheItemId = "@id";
        public const string CacheItemValue = "@value";
        public const string ExpiresAtTime = "@expiresAtTime";
        public const string SlidingExpirationInSeconds = "@slidingExpirationInSeconds";
        public const string AbsoluteExpiration = "@absoluteExpiration";
    }

    public static class Indexes
    {
        // The value of the following index positions is dependent on how the SQL queries
        // are selecting the columns.
        public const int CacheItemValueIndex = 0;
    }
}
