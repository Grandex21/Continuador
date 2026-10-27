$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$build = Join-Path $root 'build'
$iconPath = Join-Path $build 'Continuador.ico'
New-Item -ItemType Directory -Force -Path $build | Out-Null

$bitmap = New-Object System.Drawing.Bitmap 256, 256
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$graphics.Clear([System.Drawing.Color]::Transparent)

$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$radius = 52
$path.AddArc(8, 8, $radius, $radius, 180, 90)
$path.AddArc(200, 8, $radius, $radius, 270, 90)
$path.AddArc(200, 200, $radius, $radius, 0, 90)
$path.AddArc(8, 200, $radius, $radius, 90, 90)
$path.CloseFigure()

$background = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Point 22, 18),
    (New-Object System.Drawing.Point 228, 238),
    ([System.Drawing.Color]::FromArgb(37, 99, 235)),
    ([System.Drawing.Color]::FromArgb(79, 70, 229)))
$graphics.FillPath($background, $path)

$ringPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(225, 255, 255, 255)), 22
$ringPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$ringPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$graphics.DrawArc($ringPen, 57, 57, 142, 142, 48, 276)

$arrowBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
$arrow = [System.Drawing.Point[]]@(
    (New-Object System.Drawing.Point 184, 49),
    (New-Object System.Drawing.Point 210, 92),
    (New-Object System.Drawing.Point 160, 90)
)
$graphics.FillPolygon($arrowBrush, $arrow)

$pngStream = New-Object System.IO.MemoryStream
$bitmap.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
$pngBytes = $pngStream.ToArray()
$file = [System.IO.File]::Create($iconPath)
$writer = New-Object System.IO.BinaryWriter $file
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]1)
$writer.Write([Byte]0)
$writer.Write([Byte]0)
$writer.Write([Byte]0)
$writer.Write([Byte]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]32)
$writer.Write([UInt32]$pngBytes.Length)
$writer.Write([UInt32]22)
$writer.Write($pngBytes)
$writer.Flush()
$writer.Dispose()

$pngStream.Dispose()
$arrowBrush.Dispose()
$ringPen.Dispose()
$background.Dispose()
$path.Dispose()
$graphics.Dispose()
$bitmap.Dispose()

Write-Host "Icono generado: $iconPath"
