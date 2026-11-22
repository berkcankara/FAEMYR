$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$iconRoot = Split-Path $PSScriptRoot
$sourcePath = Join-Path $iconRoot 'Icon\Faemyr.png'
$iconPath = Join-Path $PSScriptRoot 'Assets\Faemyr.ico'
$source = [Drawing.Bitmap]::new($sourcePath)
$frames = [Collections.Generic.List[object]]::new()
try {
    # Remove the transparent canvas margin so the rounded tile occupies the
    # full available Windows icon area, including at taskbar sizes.
    $left = $source.Width; $top = $source.Height; $right = -1; $bottom = -1
    for ($y = 0; $y -lt $source.Height; $y++) {
        for ($x = 0; $x -lt $source.Width; $x++) {
            if ($source.GetPixel($x,$y).A -lt 32) { continue }
            if ($x -lt $left) { $left = $x }; if ($x -gt $right) { $right = $x }
            if ($y -lt $top) { $top = $y }; if ($y -gt $bottom) { $bottom = $y }
        }
    }
    if ($right -lt $left -or $bottom -lt $top) { throw 'The source icon is fully transparent.' }
    # Preserve a one-pixel antialiasing allowance around a square crop, without
    # stretching the artwork or changing its original rounded silhouette.
    $side = [Math]::Min([Math]::Min($source.Width,$source.Height),[Math]::Max($right-$left+1,$bottom-$top+1)+2)
    $cropX = [Math]::Clamp([int][Math]::Floor(($left+$right+1-$side)/2),0,$source.Width-$side)
    $cropY = [Math]::Clamp([int][Math]::Floor(($top+$bottom+1-$side)/2),0,$source.Height-$side)
    foreach ($size in @(16,20,24,32,40,48,64,128,256)) {
        $bitmap = [Drawing.Bitmap]::new($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $attributes = [Drawing.Imaging.ImageAttributes]::new()
        $png = [IO.MemoryStream]::new()
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $attributes.SetWrapMode([Drawing.Drawing2D.WrapMode]::TileFlipXY)
            $graphics.DrawImage($source,[Drawing.Rectangle]::new(0,0,$size,$size),$cropX,$cropY,$side,$side,[Drawing.GraphicsUnit]::Pixel,$attributes)
            $bitmap.Save($png,[Drawing.Imaging.ImageFormat]::Png)
            $frames.Add([pscustomobject]@{Size=$size;Bytes=$png.ToArray()})
        } finally { $png.Dispose(); $attributes.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }
} finally { $source.Dispose() }
$file = [IO.File]::Create($iconPath)
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose(); $file.Dispose() }
Copy-Item -LiteralPath $iconPath -Destination (Join-Path $iconRoot 'Icon\Faemyr.ico') -Force
Write-Output "ICO hazır: 16,20,24,32,40,48,64,128,256 px"
