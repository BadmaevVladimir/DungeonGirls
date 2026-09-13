param(
    [Parameter(Mandatory = $true)][string]$FrameDirectory,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [int]$Scale = 2
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$files = Get-ChildItem -LiteralPath $FrameDirectory -Filter 'frame_*.png' | Sort-Object Name
if ($files.Count -eq 0) { throw "No frames found in $FrameDirectory" }

$first = [System.Drawing.Bitmap]::new($files[0].FullName)
$cellWidth = $first.Width * $Scale
$cellHeight = $first.Height * $Scale
$first.Dispose()
$sheet = [System.Drawing.Bitmap]::new($cellWidth * $files.Count, $cellHeight)
$graphics = [System.Drawing.Graphics]::FromImage($sheet)
$graphics.Clear([System.Drawing.Color]::FromArgb(20, 20, 26))
for ($index = 0; $index -lt $files.Count; $index++) {
    $frame = [System.Drawing.Bitmap]::new($files[$index].FullName)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $graphics.DrawImage($frame, $index * $cellWidth, 0, $cellWidth, $cellHeight)
    $frame.Dispose()
}
$graphics.Dispose()
$sheet.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()
