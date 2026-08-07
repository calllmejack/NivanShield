[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $projectRoot 'src\Services\SessionMarkerFile.cs'
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'nivan-session-marker-test-' + [Guid]::NewGuid().ToString('N')
)
$markerPath = Join-Path $temporaryRoot 'active-session.lock'

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )
    if (-not $Condition) { throw $Message }
}

try {
    New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null

    $compiledTypes = Add-Type -TypeDefinition (Get-Content -LiteralPath $sourcePath -Raw) `
        -Language CSharp `
        -PassThru
    $markerType = $compiledTypes |
        Where-Object { $_.FullName -eq 'Nivan.Shield.Services.SessionMarkerFile' } |
        Select-Object -First 1
    Assert-True ($null -ne $markerType) 'SessionMarkerFile could not be compiled for the regression test.'

    $writeMethod = $markerType.GetMethod(
        'Write',
        [Reflection.BindingFlags]::Static -bor [Reflection.BindingFlags]::NonPublic
    )
    Assert-True ($null -ne $writeMethod) 'SessionMarkerFile.Write could not be found.'

    [IO.File]::WriteAllText($markerPath, 'abandoned-session')
    [IO.File]::SetAttributes(
        $markerPath,
        [IO.FileAttributes]::Hidden -bor [IO.FileAttributes]::ReadOnly
    )

    # This is the exact restart scenario that failed in 6.0.5.
    $writeMethod.Invoke($null, @($markerPath)) | Out-Null
    $firstTimestamp = [DateTime]::Parse(
        [IO.File]::ReadAllText($markerPath),
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind
    )
    Assert-True ($firstTimestamp.Kind -eq [DateTimeKind]::Utc) 'The refreshed marker is not a UTC timestamp.'
    Assert-True (
        ([IO.File]::GetAttributes($markerPath) -band [IO.FileAttributes]::Hidden) -ne 0
    ) 'The refreshed marker should remain hidden.'

    # Verify another abandoned-session restart can refresh the hidden marker too.
    $writeMethod.Invoke($null, @($markerPath)) | Out-Null
    [void][DateTime]::Parse(
        [IO.File]::ReadAllText($markerPath),
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind
    )

    Write-Host 'Session marker crash-restart regression test passed.' -ForegroundColor Green
}
finally {
    if ([IO.File]::Exists($markerPath)) {
        [IO.File]::SetAttributes($markerPath, [IO.FileAttributes]::Normal)
    }
    Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
}
