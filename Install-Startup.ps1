$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'MouseFinder.csproj'
$publish = Join-Path $PSScriptRoot 'app'

# Close either app name during an in-place upgrade.
Get-Process -Name 'MouseFinder','MouseJiggle' -ErrorAction SilentlyContinue | Stop-Process
dotnet publish $project -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$startup = [Environment]::GetFolderPath('Startup')
$oldShortcutPath = Join-Path $startup 'Mouse Jiggle.lnk'
if (Test-Path $oldShortcutPath) { Remove-Item -LiteralPath $oldShortcutPath }
$shortcutPath = Join-Path $startup 'Mouse Finder.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $publish 'MouseFinder.exe'
$shortcut.WorkingDirectory = $publish
$shortcut.Description = 'Mouse Finder — shake to enlarge the pointer'
$shortcut.IconLocation = "$($shortcut.TargetPath),0"
$shortcut.Save()

Start-Process $shortcut.TargetPath
Write-Host "Mouse Finder is running and will start with Windows."
