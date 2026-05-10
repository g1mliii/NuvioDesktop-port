Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$project = 'src/Nuvio.Desktop/Nuvio.Desktop.csproj'
$start = Get-Date
$process = Start-Process dotnet -ArgumentList @('run', '--project', $project, '--configuration', 'Release') -PassThru
Start-Sleep -Seconds 5
$process.Refresh()
$workingSetMb = [Math]::Round($process.WorkingSet64 / 1MB, 1)
$elapsed = [Math]::Round(((Get-Date) - $start).TotalSeconds, 2)
Stop-Process -Id $process.Id -ErrorAction SilentlyContinue

[PSCustomObject]@{
    ElapsedSeconds = $elapsed
    WorkingSetMB = $workingSetMb
}
