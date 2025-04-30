param(
    [Parameter(Mandatory = $false)][string]$ConnectionString = "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=postgres;Pooling=true;Timeout=0;Command Timeout=0;Maximum Pool Size=50;",
    [Parameter(Mandatory = $false)][string]$SchemaName = "public",
    [Parameter(Mandatory = $false)][string]$TableName = "CacheTest")

function ExecuteScalar($Connection, $Script) {
    $cmd = New-Object Npgsql.NpgsqlCommand
    $cmd.Connection = $Connection
    $cmd.CommandText = $Script
    $cmd.ExecuteScalar()
}

Add-Type -AssemblyName Npgsql

# Check if the database exists
Write-Host "Checking for database..."
$ServerConnectionBuilder = New-Object Npgsql.NpgsqlConnectionStringBuilder $ConnectionString

if (!$ServerConnectionBuilder.Database) {
    throw "A 'Database' value must be provided in the connection string!"
}
$DatabaseName = $ServerConnectionBuilder.Database

if ($ServerConnectionBuilder.Host -ne "localhost" -and $ServerConnectionBuilder.Host -ne "127.0.0.1") {
    Write-Warning "This script is really only designed for running against your local instance of Postgres DB. Continue at your own risk!"
}

$ServerConnection = New-Object Npgsql.NpgsqlConnection $ServerConnectionBuilder.ConnectionString
$ServerConnection.Open();

# Yes, this is SQL Injectable, but you're using it on your local machine with the intent of connecting to your local db.
$dbid = ExecuteScalar $ServerConnection "SELECT oid FROM pg_database WHERE datname = '$DatabaseName'"
if (!$dbid) {
    Write-Host "Database not found, creating..."

    # Create the database
    ExecuteScalar $ServerConnection "CREATE DATABASE $DatabaseName"
}

# Close the server connection
$ServerConnection.Close()

# Check for the table
$DbConnection = New-Object Npgsql.NpgsqlConnection $ConnectionString
$DbConnection.Open();
$tableid = ExecuteScalar $DbConnection "SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = '$TableName'"
if ($tableid) {
    Write-Host "Table exists, dropping it..."
    ExecuteScalar $DbConnection "DROP TABLE $TableName"
}

$DbConnection.Close()

# Fill the database with sql cache goodies
dotnet postgres-cache create $ConnectionString $SchemaName $TableName

# Set environment variables and launch tests
$oldConnectionString = $env:PGCACHETESTS_ConnectionString
$oldSchemaName = $env:PGCACHETESTS_SchemaName
$oldTableName = $env:PGCACHETESTS_TableName
$oldEnabled = $env:PGCACHETESTS_ENABLED
try {
    $env:PGCACHETESTS_ConnectionString = $ConnectionString
    $env:PGCACHETESTS_SchemaName = $SchemaName
    $env:PGCACHETESTS_TableName = $TableName
    $env:PGCACHETESTS_ENABLED = "1"

    Write-Host "Launching Tests..."
    dotnet test "$PSScriptRoot/Microsoft.Extensions.Caching.Postgres.Tests.csproj"
}
finally {
    if ($oldConnectionString) {
        $env:PGCACHETESTS_ConnectionString = $oldConnectionString
    }
    else {
        Remove-Item env:\PGCACHETESTS_ConnectionString
    }

    if ($oldSchemaName) {
        $env:PGCACHETESTS_SchemaName = $oldSchemaName
    }
    else {
        Remove-Item env:\PGCACHETESTS_SchemaName
    }

    if ($oldTableName) {
        $env:PGCACHETESTS_TableName = $oldTableName
    }
    else {
        Remove-Item env:\PGCACHETESTS_TableName
    }

    if ($oldEnabled) {
        $env:PGCACHETESTS_ENABLED = $oldEnabled
    }
    else {
        Remove-Item env:\PGCACHETESTS_ENABLED
    }
}
