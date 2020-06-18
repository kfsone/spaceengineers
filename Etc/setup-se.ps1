#! powershell

# This script grabs steamcmd and space engineers.

Param(
  [Parameter(HelpMessage="Installation path for the application")]
  [String] $InstallPath="${HOME}/SpaceEngineers",

  [Parameter(HelpMessage="Specify the appID (e.g for betas)")]
  [Int] $SteamAppID=298740,

  [Parameter(HelpMessage="Specify the steamcmd executable to use")]
  [String] $SteamCmdPath="${HOME}/SpaceEngineers"
)

$VCRedist2013 = "https://download.microsoft.com/download/2/E/6/2E61CFA4-993B-4DD4-91DA-3737CD5CD6E3/vcredist_x64.exe"
$VCRedist2015 = "https://download.microsoft.com/download/9/3/F/93FCF1E7-E6A4-478B-96E7-D4B285925B00/vc_redist.x64.exe"
$VCRedist2019 = "https://download.visualstudio.microsoft.com/download/pr/d60aa805-26e9-47df-b4e3-cd6fcc392333/7D7105C52FCD6766BEEE1AE162AA81E278686122C1E44890712326634D0B055E/VC_redist.x64.exe"

Function Install-Redistributable {
	Param(
		[String] $URL,
		[String] $Name
	)

	$dlpath = Join-Path $HOME Downloads
	$dlfile = Join-Path $dlpath $Name

	if (!(Test-Path $dlfile)) {
		Write-Output "Fetching: $name"
		Invoke-WebRequest $url -outfile $dlfile -erroraction stop
	}

	& $dlfile /install /passive /norestart
}

Install-Redistributable -Name "vc-redist-2013.exe" -Url $VCRedist2013
Install-Redistributable -Name "vc-redist-2015.exe" -Url $VCRedist2015
Install-Redistributable -Name "vc-redist-2019.exe" -Url $VCRedist2019

$SteamCmdURL = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip"

if (!(Test-Path $SteamCmdPath)) {
	mkdir $SteamCmdPath -ErrorAction stop
}
$SteamCmd = Join-Path $SteamCmdPath "steamcmd.exe"
if (!(Test-Path $SteamCmd)) {
	$archive = Join-Path $SteamCmdPath "steamcmd.zip"
	if (!(Test-Path $archive)) {
		Write-Output "Fetching steamcmd from $SteamCmdUrl"
		Invoke-WebRequest $steamcmdurl -outfile $archive -erroraction stop
	}

	Write-Output "Unzipping steamcmd"
	Expand-Archive $archive -Destination $SteamCmdPath -erroraction stop
}

if (!(Test-Path $InstallPath)) {
	mkdir $InstallPath -ErrorAction stop
}

Write-Output "Fetching game"
& $SteamCmd +login anonymous +force_install_dir $InstallPath +app_update $SteamAppID +quit

# The game should be here:
$ServerPath = Join-Path $installpath steamapps/common/spaceenginersdedicatedserver/dedicatedserver64
$ServerExe = Join-Path $ServerPath SpaceEngineersDedicated.exe
if (!(Test-Path $ServerExe)) {
	Write-Error "Seem to be missing $ServerExe"
} else {
	# Grant firewall permissions
	New-NetFirewallRule -DisplayName "SpaceEngineers" -Direction Inbound -Program $ServerExe
}

cd $ServerPath
