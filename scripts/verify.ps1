$ErrorActionPreference = 'Stop'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET 8 SDK is not installed or dotnet is not available in PATH.'
}

$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'ViberAttendance.sln'

dotnet restore $solution
dotnet build $solution --configuration Release --no-restore -warnaserror
dotnet test $solution --configuration Release --no-build --logger 'console;verbosity=normal'
