$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$assetDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\StickyTodo.App\Assets'
[xml]$svg = Get-Content -LiteralPath (Join-Path $assetDirectory 'StickyTodo.svg') -Raw
$frames = @()
foreach ($size in @(16, 20, 24, 32, 40, 48, 64, 128, 256)) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $drawing = $visual.RenderOpen()
    $drawing.PushTransform((New-Object System.Windows.Media.ScaleTransform ($size / 24.0), ($size / 24.0)))
    $pen = New-Object System.Windows.Media.Pen ([System.Windows.Media.BrushConverter]::new().ConvertFromString($svg.svg.stroke)), ([double]$svg.svg.'stroke-width')
    $pen.StartLineCap = $pen.EndLineCap = [System.Windows.Media.PenLineCap]::Round
    $pen.LineJoin = [System.Windows.Media.PenLineJoin]::Round
    foreach ($path in $svg.svg.path) {
        $brush = [System.Windows.Media.BrushConverter]::new().ConvertFromString($path.fill)
        $drawing.DrawGeometry($brush, $pen, [System.Windows.Media.Geometry]::Parse($path.d))
    }
    $drawing.Pop()
    $drawing.Close()
    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object System.IO.MemoryStream
    $encoder.Save($stream)
    $frames += [pscustomobject]@{Size=$size; Bytes=$stream.ToArray()}
    $stream.Dispose()
}
$output = [System.IO.File]::Create((Join-Path $assetDirectory 'StickyTodo.ico'))
$writer = [System.IO.BinaryWriter]::new($output)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose() }
Write-Output 'Generated StickyTodo.ico: 16, 20, 24, 32, 40, 48, 64, 128, 256 px.'
