param(
    [Parameter(Mandatory = $true)][string]$FirstFrame,
    [Parameter(Mandatory = $true)][string]$LastFrame,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

# A deliberate two-frame phase snap: preserves supplied source pixels exactly,
# avoiding generative interpolation artifacts in silhouette-critical boss art.
$sequence = @($FirstFrame, $FirstFrame, $FirstFrame, $LastFrame, $LastFrame)
for ($index = 0; $index -lt $sequence.Count; $index++) {
    Copy-Item -LiteralPath $sequence[$index] -Destination (Join-Path $OutputDirectory ("frame_{0:D2}.png" -f $index)) -Force
}
