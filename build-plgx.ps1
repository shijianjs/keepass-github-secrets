# Build KeePassGitHubSecrets.plgx.
#
# PLGX is the "source package" plugin format: KeePass compiles the sources itself
# when it loads the plugin, which makes the plugin work on custom/unsigned KeePass
# builds. The official packer is KeePass itself (`--plgx-create <absolute dir>`).
#
# Usage:
#   pwsh .\build-plgx.ps1              # build + install into dist\KeePass-2.62\Plugins
#   pwsh .\build-plgx.ps1 -Verify      # ... and prove KeePass compiles it (cache dir)
#
# A DLL in Plugins\ always wins over the PLGX, so installing the PLGX moves an
# existing DLL to dist\dll-backup\ (use -KeepDll to skip that).

param(
	[string]$KeePass = '',
	[switch]$KeepDll,
	[switch]$Verify,
	[int]$StartupSeconds = 12
)

$ErrorActionPreference = 'Stop'

$repo = $PSScriptRoot
if([string]::IsNullOrWhiteSpace($KeePass)) {
	$KeePass = Join-Path $repo 'dist\KeePass-2.62\KeePass.exe'
}
if(-not (Test-Path -LiteralPath $KeePass)) { throw "KeePass.exe not found: $KeePass" }

$pluginName = 'KeePassGitHubSecrets'
$pluginDir = Join-Path (Split-Path -Parent $KeePass) 'Plugins'
$stageRoot = Join-Path $repo 'target\plgx'
$stage = Join-Path $stageRoot $pluginName   # directory name becomes the .plgx file name
$plgxOut = Join-Path $stageRoot ($pluginName + '.plgx')

# 1. Stage a clean source tree. Keep dist\ (100 MB KeePass) out of the package:
#    KeePass walks the whole directory, not just the .csproj <Compile> list.
if(Test-Path -LiteralPath $stageRoot) { Remove-Item -LiteralPath $stageRoot -Recurse -Force }
New-Item -ItemType Directory -Path (Join-Path $stage 'Properties') -Force | Out-Null
Copy-Item (Join-Path $repo '*.cs') $stage
Copy-Item (Join-Path $repo 'Properties\AssemblyInfo.cs') (Join-Path $stage 'Properties')

$csproj = Get-Content -LiteralPath (Join-Path $repo ($pluginName + '.csproj')) -Raw
# Drop items that point outside the staged tree (test data, deploy target).
$csproj = $csproj -replace '<Content Include="dist\\data\\test-database.md" />', ''
$csproj = $csproj -replace '(?s)<!-- Local test environment.*?</Target>', ''
# PLGX is compiled with the in-box compiler, which speaks C# 5 at most.
if($csproj -notmatch '<LangVersion>') {
	$csproj = $csproj.Replace(
		"<TargetFrameworkVersion>v4.6.2</TargetFrameworkVersion>",
		"<TargetFrameworkVersion>v4.6.2</TargetFrameworkVersion>`r`n    <LangVersion>5</LangVersion>")
}
[IO.File]::WriteAllText((Join-Path $stage ($pluginName + '.csproj')), $csproj,
	(New-Object System.Text.UTF8Encoding($true)))

# 2. Let KeePass pack it. Relative paths do not work, so pass an absolute one.
$packerArgs = @('--debug', ('--plgx-create ' + $stage))
$packer = Start-Process -FilePath $KeePass -ArgumentList $packerArgs -Wait -PassThru -WindowStyle Hidden
if($packer.ExitCode -ne 0) { throw "KeePass --plgx-create failed with exit code $($packer.ExitCode)" }
if(-not (Test-Path -LiteralPath $plgxOut)) { throw "PLGX not produced: $plgxOut" }
Write-Host ("Built : {0} ({1} bytes)" -f $plgxOut, (Get-Item -LiteralPath $plgxOut).Length)

# 3. Install it (DLL wins over PLGX, so move the DLL aside unless asked not to).
New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
$dll = Join-Path $pluginDir ($pluginName + '.dll')
if((Test-Path -LiteralPath $dll) -and (-not $KeepDll)) {
	$backup = Join-Path $repo 'dist\dll-backup'
	New-Item -ItemType Directory -Path $backup -Force | Out-Null
	Move-Item -LiteralPath $dll -Destination $backup -Force
	Write-Host ("Moved : {0} -> {1}" -f $dll, $backup)
}
Copy-Item -LiteralPath $plgxOut -Destination $pluginDir -Force
Write-Host ("Installed : {0}" -f (Join-Path $pluginDir ($pluginName + '.plgx')))

# 4. Optional proof: KeePass compiles the PLGX at load time and caches the
#    assembly in %LOCALAPPDATA%\KeePass\PluginCache\<hash>\<name>.dll.
if($Verify) {
	if(Get-Process -Name 'KeePass' -ErrorAction SilentlyContinue) {
		throw 'KeePass is already running; close it before -Verify'
	}
	$cache = Join-Path $env:LOCALAPPDATA 'KeePass\PluginCache'
	$start = Get-Date
	$proc = Start-Process -FilePath $KeePass -ArgumentList '--debug' -PassThru
	Start-Sleep -Seconds $StartupSeconds
	$compiled = @()
	if(Test-Path -LiteralPath $cache) {
		$compiled = Get-ChildItem -LiteralPath $cache -Recurse -File -Filter ($pluginName + '.dll') |
			Where-Object { $_.LastWriteTime -ge $start.AddSeconds(-1) }
	}
	if(-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
	if($compiled.Count -eq 0) {
		Write-Host 'Result : NOT COMPILED - no fresh cache entry; check for a KeePass error dialog'
		exit 1
	}
	Write-Host ('Result : COMPILED - {0} ({1} bytes, {2})' -f $compiled[0].FullName,
		$compiled[0].Length, $compiled[0].LastWriteTime)
}
