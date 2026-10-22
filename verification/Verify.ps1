param([switch]$SkipPackage, [switch]$Interactive, [switch]$IsolatedChild)

$ErrorActionPreference = 'Stop'
$solutionRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$configuration = 'Release'

function Invoke-DotNet

{
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0)
    {
        throw "dotnet failed with exit code $LASTEXITCODE"
    }
}

if (-not $Interactive -and -not $IsolatedChild)
{
    $verificationProject = Join-Path $PSScriptRoot 'Automated\HenLib.WinFormsTheming.Verification.csproj'
    Invoke-DotNet -Arguments @('build', $verificationProject, '-c', $configuration, '--nologo')
    $launcher = Join-Path $PSScriptRoot 'Automated\bin\Release\net10.0-windows\HenLib.WinFormsTheming.Verification.dll'
    $launchArguments = @($launcher, 'isolated', $PSCommandPath)
    if ($SkipPackage)
    {
        $launchArguments += '--skip-package'
    }
    Invoke-DotNet -Arguments $launchArguments
    exit 0
}

Invoke-DotNet -Arguments @('build', (Join-Path $solutionRoot 'HenLib.WinFormsTheming.sln'), '-c', $configuration, '--nologo')
$checks = Join-Path $PSScriptRoot 'Automated\bin\Release\net10.0-windows\HenLib.WinFormsTheming.Verification.dll'
$consumer = Join-Path $PSScriptRoot 'ConsumerSmoke\bin\Release\net10.0-windows\HenLib.WinFormsTheming.ConsumerSmoke.dll'
Invoke-DotNet -Arguments @($checks, 'lifecycle')
foreach ($startupMode in @('--light', '--dark'))
{
    foreach ($scenario in @('progress', 'visibility', 'startup', 'controls', 'setup', 'colors', 'disabled'))
    {
        Invoke-DotNet -Arguments @($checks, $scenario, $startupMode)
    }
    Invoke-DotNet -Arguments @($consumer, $startupMode)
}
Invoke-DotNet -Arguments @($checks, 'progress', '--no-motion')
Invoke-DotNet -Arguments @($checks, 'progress', '--native')

if (-not $SkipPackage)

{
    $packageTemp = Join-Path ([IO.Path]::GetTempPath()) ('HenLib.WinFormsTheming-' + [Guid]::NewGuid().ToString('N'))
    $packageTemp = [IO.Path]::GetFullPath($packageTemp)
    $tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $packageTemp.StartsWith($tempParent, [StringComparison]::OrdinalIgnoreCase))
    {
        throw 'Package verification output must be inside the temporary directory.'
    }
    New-Item -ItemType Directory -Path $packageTemp | Out-Null
    try
    {
        $feed = Join-Path $packageTemp 'feed'
        Invoke-DotNet -Arguments @('pack', (Join-Path $solutionRoot 'src\HenLib.WinFormsTheming\HenLib.WinFormsTheming.csproj'),
            '-c', $configuration, '-o', $feed, '--no-build', '--nologo')
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $packageFiles = @(Get-ChildItem -LiteralPath $feed -Filter '*.nupkg')
        if ($packageFiles.Count -ne 1)
        {
            throw 'Expected one library package.'
        }
        $packageFile = $packageFiles[0]
        $archive = [IO.Compression.ZipFile]::OpenRead($packageFile.FullName)
        try
        {
            $paths = @($archive.Entries | ForEach-Object { $_.FullName })
            $assemblies = @($paths | Where-Object { $_ -match '^lib/net10\.0-windows(?:[0-9.]+)?/HenLib\.WinFormsTheming\.dll$' })
            if ($assemblies.Count -ne 1 -or $paths -notcontains 'README.md')
            {
                throw 'The package is missing its assembly or README.'
            }
            if ($paths | Where-Object { $_ -match '(^verification/|\.(cs|png|json|exe)$)' })
            {
                throw 'The library package contains verification sources, captures, or executable fixtures.'
            }
            $manifests = @($archive.Entries | Where-Object { $_.FullName -like '*.nuspec' })
            if ($manifests.Count -ne 1)
            {
                throw 'Expected one package manifest.'
            }
            $manifest = $manifests[0]
            $reader = [IO.StreamReader]::new($manifest.Open())
            try
            {
                [xml]$metadata = $reader.ReadToEnd()
                if ($metadata.SelectNodes("//*[local-name()='dependency']").Count -ne 0)
                {
                    throw 'The standalone library unexpectedly declares package dependencies.'
                }
            }
            finally
            {
                $reader.Dispose()
            }
        }
        finally
        {
            $archive.Dispose()
        }
        $packageProject = Join-Path $PSScriptRoot 'PackageConsumer\HenLib.WinFormsTheming.PackageConsumer.csproj'
        Invoke-DotNet -Arguments @('build', $packageProject, '-c', $configuration, '--nologo',
            "-p:RestoreSources=$feed", "-p:RestorePackagesPath=$(Join-Path $packageTemp 'packages')")
        $packageConsumer = Join-Path $PSScriptRoot 'PackageConsumer\bin\Release\net10.0-windows\HenLib.WinFormsTheming.PackageConsumer.dll'
        foreach ($startupMode in @('--light', '--dark'))
        {
            Invoke-DotNet -Arguments @($packageConsumer, $startupMode)
        }
    }
    finally
    {
        Remove-Item -LiteralPath $packageTemp -Recurse -Force
    }
}

Write-Host 'All verification scenarios passed.'
