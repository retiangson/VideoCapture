[CmdletBinding()]
param(
    [string] $FFmpegDirectory
)

$ErrorActionPreference = 'Stop'
$expectedHashes = [ordered]@{
    'ffmpeg.exe' = '09948D4CDD0650DA6FF5A87577469F2A218DC2615AE379F8F734D24C49DE0F73'
    'ffprobe.exe' = 'A6618E99BB58869DED3C6F37B53AA1A8D701C3591DBB7B5B317D47369C112BE2'
}
$destination = Join-Path $PSScriptRoot '../src/VideoCapture/Tools/Media'

# Keep local builds reproducible without storing >200 MB binaries in Git.
# Check an existing destination first so rerunning setup is harmless.
$alreadyInstalled = $true
foreach ($name in $expectedHashes.Keys) {
    $target = Join-Path $destination $name
    if (!(Test-Path -LiteralPath $target -PathType Leaf) -or
        (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $expectedHashes[$name]) {
        $alreadyInstalled = $false
        break
    }
}
if ($alreadyInstalled) {
    Write-Output 'The pinned FFmpeg/ffprobe tools are already installed and verified.'
    return
}

if (!$FFmpegDirectory) {
    $installed = Get-Command ffmpeg.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($installed) { $FFmpegDirectory = Split-Path -Parent $installed.Source }
}
if (!$FFmpegDirectory) {
    throw 'Extract the Gyan FFmpeg 8.1.1 full Windows build and pass its bin folder with -FFmpegDirectory. See README.md.'
}

$sourceDirectory = (Resolve-Path -LiteralPath $FFmpegDirectory).Path
# Verify both files before changing either destination. These hashes pin the
# original Gyan 8.1.1 full static build; version strings alone are insufficient.
foreach ($name in $expectedHashes.Keys) {
    $source = Join-Path $sourceDirectory $name
    if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing media tool: $source" }
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $expectedHashes[$name]) {
        throw "$name does not match the pinned Gyan FFmpeg 8.1.1 full build. Use the exact archive linked in README.md."
    }
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach ($name in $expectedHashes.Keys) {
    $source = Join-Path $sourceDirectory $name
    $target = Join-Path $destination $name
    if ([IO.Path]::GetFullPath($source) -ne [IO.Path]::GetFullPath($target)) {
        Copy-Item -LiteralPath $source -Destination $target -Force
    }
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $expectedHashes[$name]) {
        throw "Copied media tool failed verification: $target"
    }
}
Write-Output 'Installed and SHA-256 verified FFmpeg/ffprobe 8.1.1.'
