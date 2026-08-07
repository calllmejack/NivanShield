[CmdletBinding()]
param([switch]$Quiet)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceRoot = Join-Path $root 'src'
$appRoot = Join-Path $root 'app'
$xamlPath = Join-Path $appRoot 'MainWindow.xaml'
$mainOutput = Join-Path $root 'NivanShield.exe'
$legacyAskPassOutput = Join-Path $appRoot 'NivanAskPass.exe'
$versionOutput = Join-Path $appRoot 'build-version.txt'
$manifestPath = Join-Path $appRoot 'NivanShield.manifest'
$iconPath = Join-Path $appRoot 'NivanShield.ico'
$bundledNekoCore = Join-Path $root 'tools\nekoray\nekobox_core.exe'
$zxingDll = Join-Path $root 'tools\qrcode\zxing.dll'
$bundledXrayCore = Join-Path $root 'tools\xray\xray.exe'
$integrityManifest = Join-Path $root 'tools\integrity.sha256'
$trustedNekoCoreSha256 = 'B365388EE5F53EDD453A7A461E3F58E05C63EEBCC39C681070F05F2EACFD4C6D'
$trustedZxingSha256 = '643A5A3DB0AE02998B507BEB82DBC362A2D5593B429963DB17EB78089AABB95A'
$trustedXrayCoreSha256 = '15C2D007954AC53BA69B80EC91242786B3C0B71D52649165B4CA1D5CC96EF8F1'

function Resolve-Compiler {
    $candidates = @(
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
    throw '.NET Framework C# compiler was not found on this Windows installation.'
}

function Resolve-AssemblyPath {
    param([string]$Name)
    Add-Type -AssemblyName $Name -ErrorAction Stop
    $assembly = [AppDomain]::CurrentDomain.GetAssemblies() |
        Where-Object { $_.GetName().Name -eq $Name } |
        Select-Object -First 1
    if ($null -eq $assembly -or [string]::IsNullOrWhiteSpace($assembly.Location)) {
        throw "Required .NET Framework assembly was not found: $Name"
    }
    return $assembly.Location
}

function Assert-BundledIntegrity {
    if (-not (Test-Path -LiteralPath $integrityManifest -PathType Leaf)) {
        throw 'The bundled runtime integrity manifest is missing.'
    }
    $manifestRoot = $root
    foreach ($line in Get-Content -LiteralPath $integrityManifest) {
        $entry = $line.Trim()
        if ([String]::IsNullOrWhiteSpace($entry) -or $entry.StartsWith('#')) { continue }
        if ($entry -notmatch '^([0-9a-fA-F]{64})\s+(.+)$') {
            throw "Invalid integrity manifest entry: $entry"
        }
        $expected = $matches[1].ToUpperInvariant()
        $relative = $matches[2].Replace('/', '\')
        if ([IO.Path]::IsPathRooted($relative) -or $relative.Contains('..')) {
            throw "Unsafe integrity manifest path: $relative"
        }
        $target = Join-Path $manifestRoot $relative
        if (-not (Test-Path -LiteralPath $target -PathType Leaf)) {
            throw "Bundled runtime file is missing: $relative"
        }
        $actual = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToUpperInvariant()
        if ($actual -ne $expected) {
            throw "Bundled runtime integrity check failed: $relative"
        }
    }
}

try {
    $csc = Resolve-Compiler
    New-Item -ItemType Directory -Path $appRoot -Force | Out-Null
    if (-not (Test-Path -LiteralPath $bundledNekoCore -PathType Leaf)) {
        throw 'The bundled Neko core is missing. Extract the complete Nivan Shield package again.'
    }
    if (-not (Test-Path -LiteralPath $xamlPath -PathType Leaf)) {
        throw 'The application interface source is missing.'
    }
    if (-not (Test-Path -LiteralPath $iconPath -PathType Leaf)) {
        throw 'The Nivan Shield application icon is missing.'
    }
    if (-not (Test-Path -LiteralPath $zxingDll -PathType Leaf)) {
        throw 'The embedded offline QR decoder is missing. Extract the complete source package again.'
    }
    if (-not (Test-Path -LiteralPath $bundledXrayCore -PathType Leaf)) {
        throw 'The bundled Xray-core executable is missing. Extract the complete source package again.'
    }
    if ((Get-FileHash -LiteralPath $bundledNekoCore -Algorithm SHA256).Hash.ToUpperInvariant() -ne $trustedNekoCoreSha256) {
        throw 'The bundled Neko core does not match the fingerprint compiled into this release.'
    }
    if ((Get-FileHash -LiteralPath $zxingDll -Algorithm SHA256).Hash.ToUpperInvariant() -ne $trustedZxingSha256) {
        throw 'The bundled offline QR decoder does not match the reviewed release fingerprint.'
    }
    if ((Get-FileHash -LiteralPath $bundledXrayCore -Algorithm SHA256).Hash.ToUpperInvariant() -ne $trustedXrayCoreSha256) {
        throw 'The bundled Xray-core executable does not match the reviewed release fingerprint.'
    }
    Assert-BundledIntegrity

    $referenceNames = @(
        'System',
        'System.Core',
        'System.Drawing',
        'System.Runtime.Serialization',
        'System.Security',
        'System.Web.Extensions',
        'System.Windows.Forms',
        'System.Xml',
        'System.Xaml',
        'WindowsBase',
        'PresentationCore',
        'PresentationFramework'
    )
    $references = @($referenceNames | ForEach-Object { '/reference:{0}' -f (Resolve-AssemblyPath $_) })

    $mainSources = @(
        Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -Recurse |
            Sort-Object FullName |
            ForEach-Object { $_.FullName }
    )
    if ($mainSources.Count -eq 0) { throw 'No application source files were found.' }

    Remove-Item -LiteralPath $mainOutput -Force -ErrorAction SilentlyContinue
    $mainArguments = @(
        '/noconfig', '/nologo', '/utf8output', '/codepage:65001', '/target:winexe', '/platform:anycpu', '/optimize+',
        '/main:Nivan.Shield.Program',
        ('/win32manifest:{0}' -f $manifestPath),
        ('/win32icon:{0}' -f $iconPath),
        ('/resource:{0},Nivan.Shield.MainWindow.xaml' -f $xamlPath),
        ('/resource:{0},Nivan.Shield.ZXing.dll' -f $zxingDll),
        ('/out:{0}' -f $mainOutput)
    ) + $references + $mainSources
    $mainCompilerOutput = @(& $csc @mainArguments 2>&1)
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $mainOutput)) {
        throw "Nivan Shield compilation failed.`n`n$($mainCompilerOutput -join [Environment]::NewLine)"
    }

    # SSH_ASKPASS is handled inside NivanShield.exe. Remove the obsolete
    # helper left by older local builds so it cannot be mistaken for a dependency.
    Remove-Item -LiteralPath $legacyAskPassOutput -Force -ErrorAction SilentlyContinue

    Set-Content -LiteralPath $versionOutput -Value '6.0.5' -Encoding ASCII

    if (-not $Quiet) {
        Write-Host ''
        Write-Host 'Nivan Shield was built successfully.' -ForegroundColor Green
        Write-Host $mainOutput -ForegroundColor Cyan
    }
    exit 0
}
catch {
    try {
        Add-Type -AssemblyName PresentationFramework
        [System.Windows.MessageBox]::Show(
            $_.Exception.Message,
            'Nivan Shield build failed',
            [System.Windows.MessageBoxButton]::OK,
            [System.Windows.MessageBoxImage]::Error
        ) | Out-Null
    }
    catch {
        Write-Error $_.Exception.Message
    }
    exit 1
}
