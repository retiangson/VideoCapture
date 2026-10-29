$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Setup-SpeechModel.ps1')
$workspace = Split-Path $PSScriptRoot -Parent
$commit = 'd1be6fde11ac6e0407606b4e42fe72d34add8037'
$vendor = Join-Path $workspace 'android\vendor'
$source = Join-Path $vendor "whisper.cpp-$commit"
if (Test-Path -LiteralPath (Join-Path $source 'include\whisper.h')) { Write-Output 'Pinned Android speech source is ready.'; return }
New-Item -ItemType Directory -Path $vendor -Force | Out-Null
$archive = Join-Path $vendor ('whisper-' + [Guid]::NewGuid().ToString('N') + '.zip')
try {
    Invoke-WebRequest -Uri "https://codeload.github.com/ggml-org/whisper.cpp/zip/$commit" -OutFile $archive -TimeoutSec 300
    if ((Get-FileHash -LiteralPath $archive).Hash -ne '6BE93B881ACB415C5A25D0EF85BB6A488099DE0296E072E22A66D50F20EC40B9') { throw 'Android native source checksum mismatch.' }
    Expand-Archive -LiteralPath $archive -DestinationPath $vendor -Force
}
finally { if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive } }
Write-Output 'Android native speech source restored and verified.'
