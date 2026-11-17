$ErrorActionPreference = 'Stop'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET 8 SDK is not installed or dotnet is not available in PATH.'
}

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\ViberAttendance.App\ViberAttendance.App.csproj'
dotnet run --project $project --configuration Debug
