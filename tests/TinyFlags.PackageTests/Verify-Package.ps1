param([string]$RunDirectory)

$ErrorActionPreference = 'Stop'

$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$runId = [Guid]::NewGuid().ToString('N')
$version = "0.1.0-smoke.$runId"
if ([string]::IsNullOrWhiteSpace($RunDirectory)) {
    $RunDirectory = Join-Path $repository "artifacts/package-tests/$runId"
}
$runDirectory = [System.IO.Path]::GetFullPath($RunDirectory)
$feed = Join-Path $runDirectory 'feed'
$packages = Join-Path $runDirectory 'packages'
$consumer = Join-Path $runDirectory 'consumer'
New-Item -ItemType Directory -Path $feed, $consumer -Force | Out-Null

dotnet pack (Join-Path $repository 'src/TinyFlags/TinyFlags.csproj') -c Release -o $feed "-p:PackageVersion=$version" -warnaserror
if ($LASTEXITCODE -ne 0) { throw 'Package creation failed.' }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$package = Join-Path $feed "TinySuite.TinyFlags.$version.nupkg"
$archive = [System.IO.Compression.ZipFile]::OpenRead($package)
try {
    foreach ($expected in @('lib/net8.0/TinyFlags.dll', 'analyzers/dotnet/cs/TinyFlags.SourceGen.dll', 'README.md')) {
        if ($null -eq $archive.GetEntry($expected)) { throw "Missing package asset: $expected" }
    }
    $reader = [System.IO.StreamReader]::new($archive.GetEntry('TinySuite.TinyFlags.nuspec').Open())
    try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $dependencies = @($manifest.SelectNodes("//*[local-name()='dependency']") | ForEach-Object { $_.id })
    $expectedDependencies = @('Microsoft.Extensions.DependencyInjection.Abstractions', 'Microsoft.Extensions.Hosting.Abstractions')
    if (@(Compare-Object ($dependencies | Sort-Object) ($expectedDependencies | Sort-Object)).Count -ne 0) {
        throw "Unexpected runtime package dependencies: $dependencies"
    }
} finally {
    $archive.Dispose()
}

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Library'), (Join-Path $PSScriptRoot 'Host') -Destination $consumer -Recurse
foreach ($relativeProject in @('Library/Library.csproj', 'Host/Host.csproj')) {
    $projectPath = Join-Path $consumer $relativeProject
    $projectText = [System.IO.File]::ReadAllText($projectPath).Replace('Version="0.1.0-dev"', "Version=`"$version`"")
    [System.IO.File]::WriteAllText($projectPath, $projectText)
}
$configuration = Join-Path $runDirectory 'NuGet.Config'
$escapedFeed = [System.Security.SecurityElement]::Escape($feed)
@"
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$escapedFeed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="local"><package pattern="TinySuite.TinyFlags" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
"@ | Set-Content -LiteralPath $configuration -Encoding UTF8

$hostProject = Join-Path $consumer 'Host/Host.csproj'
$properties = @("-p:RestorePackagesPath=$packages")
dotnet restore $hostProject --configfile $configuration @properties -warnaserror
if ($LASTEXITCODE -ne 0) { throw 'Consumer restore failed.' }
dotnet build $hostProject -c Release --no-restore @properties -warnaserror
if ($LASTEXITCODE -ne 0) { throw 'Consumer build failed.' }
dotnet (Join-Path $consumer 'Host/bin/Release/net8.0/Host.dll')
if ($LASTEXITCODE -ne 0) { throw 'Consumer behavior failed.' }

$diagnostics = & dotnet build $hostProject -c Release --no-restore @properties -p:IncludeInvalidFlags=true -warnaserror 2>&1
$diagnosticExitCode = $LASTEXITCODE
$diagnostics | Set-Content -LiteralPath (Join-Path $runDirectory 'invalid-build.log')
if ($diagnosticExitCode -eq 0 -or ($diagnostics -join "`n") -notmatch 'error TFG002:') {
    throw "Expected the packaged generator to reject the invalid declaration with TFG002.`n$diagnostics"
}

Write-Host "Package verification passed: runtime behavior and TFG002. Artifacts: $runDirectory"
exit 0
