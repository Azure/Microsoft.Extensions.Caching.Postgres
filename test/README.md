# Microsoft.Extensions.Caching.Postgres Tests

These tests include functional tests that run against a real Postgres database. Since these are flaky on CI, they should be run manually when changing this code.

## Pre-requisites

1. A functional Postgres database.

## Running the tests

1. Install the latest version of the `dotnet-postgres-cache` too: `dotnet tool install --global dotnet-postgres-cache` (make sure to specify a version if it's a pre-release tool you want!)
1. Run `dotnet postgres-cache [connectionstring] public CacheTest false`
    * `[connectionstring]` must be a SQL Connection String **for a database that already exists**
1. Unskip the tests by changing the `PostgresCacheWithDatabaseTest.SkipReason` field to `null`.
1. Run the tests.
