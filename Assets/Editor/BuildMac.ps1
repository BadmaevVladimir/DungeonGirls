param([string]$UnityPath)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$versionText = Get-Content -LiteralPath (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt') -Raw
if ($versionText -notmatch 'm_EditorVersion: (\S+)') { throw 'Unity version is missing in ProjectVersion.txt' }
$editorVersion = $Matches[1]
if (-not $UnityPath) {
    $UnityPath = Join-Path $env:ProgramFiles "Unity/Hub/Editor/$editorVersion/Editor/Unity.exe"
}
if (-not (Test-Path -LiteralPath $UnityPath)) { throw "Unity $editorVersion not found. Pass -UnityPath with the path to Unity.exe." }
$logFolder = Join-Path $projectRoot 'Logs'
New-Item -ItemType Directory -Force -Path $logFolder | Out-Null
$logPath = Join-Path $logFolder 'MacBuildPipeline.log'
Write-Host 'Close this project in Unity before running the command. Building macOS Universal...'
$arguments = "-batchmode -nographics -quit -projectPath `"$projectRoot`" -buildTarget OSXUniversal -executeMethod MacBuildPipeline.Build -logFile `"$logPath`""
$buildProcess = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
if ($buildProcess.ExitCode -ne 0) { throw "macOS build failed (exit $($buildProcess.ExitCode)). See $logPath" }
if (-not (Select-String -LiteralPath $logPath -SimpleMatch 'macOS build SUCCEEDED' -Quiet)) {
    throw "Unity exited without a completed package. See $logPath"
}
Write-Host "Build ready: $(Join-Path $projectRoot 'Builds/macOS')"
Write-Host "Log: $logPath"
