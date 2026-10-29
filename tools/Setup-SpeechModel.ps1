$ErrorActionPreference = 'Stop'
$directory = Join-Path $PSScriptRoot '..\src\VideoCapture\Models'
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$target = Join-Path $directory 'ggml-small-q5_1.bin'
$expected = 'AE85E4A935D7A567BD102FE55AFC16BB595BDB618E11B2FC7591BC08120411BB'
if ((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target).Hash -eq $expected) { Write-Output 'Speech model is ready.'; return }
$temporary = $target + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
try {
    Invoke-WebRequest -Uri 'https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/ggml-small-q5_1.bin?download=true' -TimeoutSec 300 -OutFile $temporary
    if ((Get-FileHash -LiteralPath $temporary).Hash -ne $expected) { throw 'Speech model checksum mismatch.' }
    Move-Item -LiteralPath $temporary -Destination $target -Force
    Write-Output 'Speech model downloaded and verified (190 MB).'
}
finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
