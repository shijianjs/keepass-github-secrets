# check-plugin-load.ps1 - did KeePass actually load this plugin build?
#
# KeePass records the SHA-256 of every plugin assembly it loads into
# <Application><PluginCompatibility> of its config file. That write happens on a
# normal exit only, so a killed process proves nothing either way.
#
#   .\check-plugin-load.ps1            # compare the deployed DLL with the recorded hashes
#   .\check-plugin-load.ps1 -Run       # start + close KeePass once, then compare
#
# Exit code: 0 = recorded (loaded), 1 = not recorded, 2 = could not check.
#
# -Run temporarily drops <LastUsedFile> so KeePass opens without the master
# password dialog. Afterwards the original config is restored and, if the run did
# load the plugin, the recorded entry is kept, so a later plain check still sees it.
# It refuses to run while another KeePass instance is open: closing that one is
# your call, not this script's.

[CmdletBinding()]
param(
	[string] $Dll = '',
	[string] $Config = '',
	[string] $KeePass = '',
	[switch] $Run,
	[int] $StartupSeconds = 8,
	[int] $ExitTimeoutSeconds = 20
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if([string]::IsNullOrWhiteSpace($Dll)) {
	$Dll = Join-Path $repoRoot 'dist\KeePass-2.62\Plugins\SamplePlugin.dll'
}
if([string]::IsNullOrWhiteSpace($Config)) {
	$Config = Join-Path $repoRoot 'dist\KeePass-2.62\KeePass.config.xml'
}
if([string]::IsNullOrWhiteSpace($KeePass)) {
	$KeePass = Join-Path $repoRoot 'dist\KeePass-2.62\KeePass.exe'
}

function Get-Sha256Base64([string] $path) {
	$sha = [System.Security.Cryptography.SHA256]::Create()
	try {
		$bytes = [System.IO.File]::ReadAllBytes($path)
		return [Convert]::ToBase64String($sha.ComputeHash($bytes))
	}
	finally { $sha.Dispose() }
}

function Get-CompatibilityBlock([string] $configPath) {
	if(-not (Test-Path -LiteralPath $configPath)) { return $null }
	$text = [System.IO.File]::ReadAllText($configPath)
	$block = [regex]::Match($text, '(?s)<PluginCompatibility>(.*?)</PluginCompatibility>')
	if(-not $block.Success) { return $null }
	return $block.Groups[1].Value
}

function Test-HashRecorded([string] $configPath, [string] $hash) {
	$block = Get-CompatibilityBlock $configPath
	if($null -eq $block) { return $false }
	return $block.Contains($hash)
}

function Remove-LastUsedFile([string] $configPath) {
	$text = [System.IO.File]::ReadAllText($configPath)
	$text = [regex]::Replace($text, '(?s)\s*<LastUsedFile>.*?</LastUsedFile>', '')
	[System.IO.File]::WriteAllText($configPath, $text)
}

# Puts the entry KeePass would have written back into the restored config, so the
# record survives the restore. No-op when the block or the entry is already there.
function Add-CompatibilityItem([string] $configPath, [string] $hash) {
	if(-not (Test-Path -LiteralPath $configPath)) { return }
	$text = [System.IO.File]::ReadAllText($configPath)
	if($text.Contains($hash)) { return }

	$suffix = '@2.62.0.0@1'
	$existing = [regex]::Match($text, '@[A-Za-z0-9+/=]{43,44}(@[^@<]+@[^@<]+)')
	if($existing.Success) { $suffix = $existing.Groups[1].Value }

	$anchor = '<PluginCompatibility>'
	$index = $text.IndexOf($anchor)
	if($index -lt 0) { return }

	$item = '<Item>@' + $hash + $suffix + '</Item>'
	$text = $text.Insert($index + $anchor.Length, "`r`n`t`t`t$item")
	[System.IO.File]::WriteAllText($configPath, $text)
}

function Invoke-KeePassOnce([string] $exe, [int] $startupSeconds, [int] $exitTimeoutSeconds) {
	$proc = Start-Process -FilePath $exe -PassThru
	Start-Sleep -Seconds $startupSeconds

	if($proc.HasExited) {
		throw "KeePass exited during startup (exit code $($proc.ExitCode)); check it manually."
	}

	$null = $proc.CloseMainWindow()
	if(-not $proc.WaitForExit($exitTimeoutSeconds * 1000)) {
		Stop-Process -Id $proc.Id -Force
		throw "KeePass did not exit within $exitTimeoutSeconds s, so it wrote no config."
	}
}

# ---------------------------------------------------------------- preflight

if(-not (Test-Path -LiteralPath $Dll)) {
	Write-Output "ERROR: plugin DLL not found: $Dll"
	Write-Output 'Build first (msbuild SamplePlugin.csproj), or pass -Dll <path>.'
	exit 2
}
if(-not (Test-Path -LiteralPath $Config)) {
	Write-Output "ERROR: KeePass config not found: $Config"
	Write-Output 'Start KeePass once so it creates the config.'
	exit 2
}

$dllItem = Get-Item -LiteralPath $Dll
$hash = Get-Sha256Base64 $dllItem.FullName

Write-Output "Plugin : $($dllItem.FullName) ($($dllItem.Length) bytes, $($dllItem.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')))"
Write-Output "Hash   : $hash"

# An unchanged DLL keeps its hash, so a recorded entry may come from an earlier
# run; only a hash that was not recorded before proves this run loaded something new.
$recordedBefore = Test-HashRecorded $Config $hash
if($recordedBefore) {
	Write-Output 'Before : already recorded by an earlier run - rebuild to make -Run meaningful'
}
else {
	Write-Output 'Before : not recorded yet'
}

# ------------------------------------------------------------------- run it

$recorded = $false
if($Run) {
	$others = @(Get-Process -Name KeePass -ErrorAction SilentlyContinue)
	if($others.Count -gt 0) {
		Write-Output "ERROR: KeePass is already running (pid $($others[0].Id)). Close it first."
		exit 2
	}
	if(-not (Test-Path -LiteralPath $KeePass)) {
		Write-Output "ERROR: KeePass.exe not found: $KeePass"
		exit 2
	}

	$backup = Join-Path $env:TEMP ('keepass-config-' + [guid]::NewGuid().ToString('N') + '.xml')
	Copy-Item -LiteralPath $Config -Destination $backup -Force
	try {
		Remove-LastUsedFile $Config
		Write-Output 'Running: starting KeePass (no database auto-open) ...'
		Invoke-KeePassOnce $KeePass $StartupSeconds $ExitTimeoutSeconds
		Write-Output 'Running: KeePass exited normally'
		$recorded = Test-HashRecorded $Config $hash
	}
	finally {
		Copy-Item -LiteralPath $backup -Destination $Config -Force
		Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
		if($recorded) { Add-CompatibilityItem $Config $hash }
	}
}
else {
	$recorded = Test-HashRecorded $Config $hash
}

# ------------------------------------------------------------------ verdict

Write-Output "Config : $Config"
if($recorded) {
	Write-Output 'Result : LOADED - KeePass has recorded this exact build'
	if($recordedBefore) { Write-Output '         (the entry was already there before this run)' }
	exit 0
}

Write-Output 'Result : NOT LOADED - this build is not in <PluginCompatibility>'
Write-Output '         Either KeePass never loaded it, or it was never closed normally.'
Write-Output '         Re-run with -Run to start and close KeePass once.'
exit 1
