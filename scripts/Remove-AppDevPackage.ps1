# Remove-AppDevPackage.ps1
#
# Undoes what Add-AppDevPackage.ps1 (from the release zip) did: uninstalls
# Scanstack and removes the self-signed "Img2PDF Dev" certificate that
# Add-AppDevPackage.ps1 added to Local Machine -> Trusted People.
#
# Add-AppDevPackage.ps1 does NOT do this automatically on app uninstall —
# uninstalling the app via Settings/Remove-AppxPackage leaves the certificate
# trusted indefinitely. Run this script if you want to remove that trust too.
#
# To run: right-click this file -> "Run with PowerShell". It will prompt for
# admin elevation, since removing a Local Machine certificate requires it.

param(
    [switch]$Force = $false
)

$ErrorActionPreference = "Stop"
$PackageIdentityName = "Img2PDF"
$CertificateSubject = "CN=Img2PDF Dev"

function Test-IsAdmin
{
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)
}

# Relaunch elevated if needed — certificate removal from Local Machine\Trusted
# People requires admin rights, even though app uninstall alone would not.
if (-not (Test-IsAdmin))
{
    Write-Host "Requesting administrator privileges..."
    $scriptPath = $MyInvocation.MyCommand.Path
    $relaunchArgs = @('-ExecutionPolicy', 'Unrestricted', '-File', "`"$scriptPath`"")
    if ($Force) { $relaunchArgs += '-Force' }

    try
    {
        $powerShellExePath = (Get-Process -Id $PID).Path
        $adminProcess = Start-Process $powerShellExePath -Verb RunAs -ArgumentList $relaunchArgs -PassThru -Wait
    }
    catch
    {
        Write-Host "Could not obtain administrator privileges. Removal cancelled." -ForegroundColor Red
        exit 1
    }
    exit $adminProcess.ExitCode
}

$exitCode = 0

# --- Uninstall the app ---
Write-Host "Looking for installed Scanstack package..."
$installedPackages = Get-AppxPackage -Name $PackageIdentityName -ErrorAction SilentlyContinue

if ($installedPackages)
{
    foreach ($pkg in $installedPackages)
    {
        Write-Host "Removing $($pkg.PackageFullName)..."
        try
        {
            Remove-AppxPackage -Package $pkg.PackageFullName -ErrorAction Stop
            Write-Host "  Done." -ForegroundColor Green
        }
        catch
        {
            Write-Host "  Failed to remove: $($_.Exception.Message)" -ForegroundColor Red
            $exitCode = 1
        }
    }
}
else
{
    Write-Host "Scanstack is not currently installed for this user — skipping."
}

# --- Remove the trusted certificate ---
# Prefer matching by exact thumbprint against the .cer shipped next to this
# script (if present), so we only ever remove the certificate this installer
# actually added. Fall back to matching by subject name if the .cer isn't
# available (e.g. this script was copied out of the release folder).
Write-Host ""
Write-Host "Looking for the trusted Scanstack signing certificate..."

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$cerFile = Get-ChildItem -Path $scriptDir -Filter "*.cer" -ErrorAction SilentlyContinue | Select-Object -First 1

$certsToRemove = $null
if ($cerFile)
{
    $shippedCert = Get-PfxCertificate -FilePath $cerFile.FullName
    $certsToRemove = Get-ChildItem Cert:\LocalMachine\TrustedPeople |
        Where-Object { $_.Thumbprint -eq $shippedCert.Thumbprint }
}
else
{
    Write-Host "  (No .cer found next to this script — matching by subject name instead.)"
    $certsToRemove = Get-ChildItem Cert:\LocalMachine\TrustedPeople |
        Where-Object { $_.Subject -eq $CertificateSubject }
}

if ($certsToRemove)
{
    foreach ($cert in $certsToRemove)
    {
        Write-Host "Removing certificate $($cert.Thumbprint) ($($cert.Subject))..."
        try
        {
            Remove-Item -Path "Cert:\LocalMachine\TrustedPeople\$($cert.Thumbprint)" -ErrorAction Stop
            Write-Host "  Done." -ForegroundColor Green
        }
        catch
        {
            Write-Host "  Failed to remove: $($_.Exception.Message)" -ForegroundColor Red
            $exitCode = 1
        }
    }
}
else
{
    Write-Host "No matching trusted certificate found — skipping."
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
if (-not $Force)
{
    Read-Host "Press Enter to exit"
}
exit $exitCode
