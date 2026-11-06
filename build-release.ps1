[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$zipPath = Join-Path $PSScriptRoot 'bin\Release\ChatRelay.zip'
$dllPath = Join-Path $PSScriptRoot 'bin\Release\net10.0\ChatRelay.dll'

& dotnet build (Join-Path $PSScriptRoot 'ChatRelay.csproj') -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Release build failed with exit code $LASTEXITCODE."
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
        $archive, $dllPath, 'ChatRelay/ChatRelay.dll',
        [IO.Compression.CompressionLevel]::Optimal) | Out-Null
}
finally {
    $archive.Dispose()
}
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($zipPath + '.sha256',
    "$hash  $([IO.Path]::GetFileName($zipPath))`n", [Text.Encoding]::ASCII)
Write-Verbose 'Created SHA-256 sidecar for the finalized ChatRelay archive.'
Write-Host "Created $zipPath"
Write-Host "Created $zipPath.sha256"
