param([Parameter(Mandatory=$true)][string]$MakeNSIS)
$ErrorActionPreference = 'Stop'
$taskPortable = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\portable'))
$taskRepository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$taskDist = Join-Path $taskRepository 'dist'
New-Item -ItemType Directory -Path $taskDist -Force | Out-Null
[xml]$taskProject = Get-Content -LiteralPath (Join-Path $taskRepository 'source\SideDock.csproj')
$taskVersion = [string]$taskProject.Project.PropertyGroup.Version
if ($taskVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid project version' }
$taskOutput = Join-Path $taskDist ("SideDock-" + $taskVersion + "-Setup.exe")
if (!(Test-Path -LiteralPath (Join-Path $taskPortable 'SideDock.exe'))) { throw 'Publish the application to portable/ first' }
foreach ($taskDocument in @('LICENSE','NOTICE','THIRD_PARTY_NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $taskRepository $taskDocument) -Destination (Join-Path $taskPortable $taskDocument) -Force
}
Copy-Item -LiteralPath (Join-Path $taskRepository 'third-party') -Destination $taskPortable -Recurse -Force
$taskLines = [Collections.Generic.List[string]]::new()
Get-ChildItem -LiteralPath $taskPortable -File -Recurse | Where-Object { $_.FullName -notmatch '\\smoke-' -and $_.Name -ne 'startup-result.json' } | ForEach-Object {
    $taskRelative = $_.FullName.Substring($taskPortable.Length + 1)
    $taskLines.Add('Delete "$INSTDIR\' + $taskRelative.Replace('$','$$') + '"')
}
Get-ChildItem -LiteralPath $taskPortable -Directory -Recurse | Where-Object { $_.FullName -notmatch '\\smoke-' } | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object {
    $taskRelative = $_.FullName.Substring($taskPortable.Length + 1)
    $taskLines.Add('RMDir "$INSTDIR\' + $taskRelative.Replace('$','$$') + '"')
}
[IO.File]::WriteAllLines((Join-Path $PSScriptRoot 'uninstall-files.nsh'), $taskLines, [Text.UTF8Encoding]::new($false))
& $MakeNSIS /V2 "/DAPP_VERSION=$taskVersion" "/DOUTPUT_FILE=$taskOutput" (Join-Path $PSScriptRoot 'SideDock.nsi')
if ($LASTEXITCODE -ne 0) { throw 'NSIS build failed' }
