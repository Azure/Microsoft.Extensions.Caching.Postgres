// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;

namespace Microsoft.Extensions.Internal;

public class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new DateTimeOffset(2013, 1, 1, 1, 0, 0, TimeSpan.Zero);
    private TimeZoneInfo _localTimeZone = TimeZoneInfo.Utc;

    public FakeTimeProvider()
    {

    }
    public FakeTimeProvider(DateTimeOffset startDateTime)
    {
        _now = startDateTime;
    }

    public DateTimeOffset AddSeconds(int seconds)
    {
        this.Advance(new TimeSpan(0, 0, seconds));
        return _now;
    }

    public DateTimeOffset AddHours(int hours)
    {
        this.Advance(new TimeSpan(hours, 0, 0));
        return _now;
    }

    public DateTimeOffset Add(TimeSpan ts)
    {
        this.Advance(ts);
        return _now;
    }

    public void Advance(TimeSpan delta)
    {
        _now += delta;
    }

    public void SetUtcNow(DateTimeOffset value)
    {
        _now = value; 
    }

    // Sets the local time zone.
    public void SetLocalTimeZone(TimeZoneInfo localTimeZone)
    {
        _localTimeZone = localTimeZone;
    }

    public override DateTimeOffset GetUtcNow()
    {
        return _now.ToUniversalTime();
    }

}
