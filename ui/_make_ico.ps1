Add-Type -AssemblyName System.Drawing
$srcPath = Join-Path $PSScriptRoot "app.png"
if (-not (Test-Path $srcPath)) { throw "missing app.png" }
$src = [System.Drawing.Image]::FromFile((Resolve-Path $srcPath).Path)
$sizes = @(16, 32, 48, 256)
$pngs = New-Object System.Collections.Generic.List[byte[]]
foreach ($sz in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $sz, $sz
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($src, 0, 0, $sz, $sz)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs.Add($ms.ToArray())
    $bmp.Dispose()
    $ms.Dispose()
}
$src.Dispose()

$count = $sizes.Length
$offset = 6 + 16 * $count
$outPath = Join-Path $PSScriptRoot "app.ico"
$fs = [System.IO.File]::Create($outPath)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$count)
for ($i = 0; $i -lt $count; $i++) {
    $dim = $sizes[$i]
    $b = 0
    if ($dim -lt 256) { $b = $dim }
    $bw.Write([byte]$b)
    $bw.Write([byte]$b)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]$pngs[$i].Length)
    $bw.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush()
$fs.Close()
Write-Output ("wrote " + $outPath + " " + (Get-Item $outPath).Length)
