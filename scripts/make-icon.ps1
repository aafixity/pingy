# Converts the supplied artwork to a Windows icon. The artwork is not cropped or recolored.
param([string] $Source = (Join-Path $PSScriptRoot '..\src\Pingy.App\Assets\pingy.png'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$destination = Join-Path $PSScriptRoot '..\src\Pingy.App\Assets\pingy.ico'
$original = [Drawing.Image]::FromFile([IO.Path]::GetFullPath($Source))
$frames = [Collections.Generic.List[object]]::new()
try {
    foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
        $bitmap = [Drawing.Bitmap]::new($size, $size)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $memory = [IO.MemoryStream]::new()
        try {
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.DrawImage($original, [Drawing.Rectangle]::new(0, 0, $size, $size))
            $bitmap.Save($memory, [Drawing.Imaging.ImageFormat]::Png)
            $frames.Add(@{ Size = $size; Bytes = $memory.ToArray() })
        } finally { $memory.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }
    $stream = [IO.File]::Create([IO.Path]::GetFullPath($destination))
    $writer = [IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
        $offset = 6 + 16 * $frames.Count
        foreach ($frame in $frames) {
            $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
            $offset += $frame.Bytes.Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
    } finally { $writer.Dispose(); $stream.Dispose() }
} finally { $original.Dispose() }
