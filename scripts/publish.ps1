#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-x86', 'win-arm64')]
    [string] $Runtime = 'win-x64'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Install the .NET 10 SDK before publishing pingy.'
}

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $projectRoot 'src\Pingy.App\Pingy.App.csproj'
$publishDirectory = Join-Path $projectRoot "artifacts\$Runtime"

Push-Location -LiteralPath $projectRoot
try {
    $publishArguments = @(
        'publish', $projectPath,
        '--configuration', 'Release',
        '--runtime', $Runtime,
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:EnableCompressionInSingleFile=true',
        '-p:PublishTrimmed=false',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:DebugType=embedded',
        '--output', $publishDirectory,
        '--nologo'
    )
    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    $executablePath = Join-Path $publishDirectory 'pingy.exe'
    if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
        throw "Publishing finished without the expected file: $executablePath"
    }
    Write-Host "Portable build ready: $executablePath"
}
finally {
    Pop-Location
}
