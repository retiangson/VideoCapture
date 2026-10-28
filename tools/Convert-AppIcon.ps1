param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$Destination
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

$sourcePath = (Resolve-Path -LiteralPath $Source).Path
$frame = [System.Windows.Media.Imaging.BitmapFrame]::Create(
    [Uri]::new($sourcePath),
    [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
    [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
if ($frame.PixelWidth -ne $frame.PixelHeight) {
    throw 'The app icon source must be square to avoid distortion.'
}

# ICO supports PNG frames on supported Windows versions. Preserve source alpha
# and provide native sizes for the title bar, taskbar, Explorer, and high DPI.
$sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
$frames = @()
foreach ($size in $sizes) {
    $scale = [System.Windows.Media.ScaleTransform]::new(
        $size / [double]$frame.PixelWidth, $size / [double]$frame.PixelHeight)
    $bitmap = [System.Windows.Media.Imaging.TransformedBitmap]::new($frame, $scale)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $memory = [System.IO.MemoryStream]::new()
    try {
        $encoder.Save($memory)
        $frames += ,$memory.ToArray()
    }
    finally { $memory.Dispose() }
}

$output = [System.IO.File]::Create([System.IO.Path]::GetFullPath($Destination))
$writer = [System.IO.BinaryWriter]::new($output)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length)
        $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($bytes in $frames) { $writer.Write([byte[]]$bytes) }
}
finally { $writer.Dispose() }
Write-Output "Created $Destination with sizes: $($sizes -join ', ')"
