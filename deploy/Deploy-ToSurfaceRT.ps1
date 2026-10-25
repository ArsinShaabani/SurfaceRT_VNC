<#
.SYNOPSIS
    Installs the SurfaceVnc Windows Store package on a Surface RT (Windows RT 8.1).

.DESCRIPTION
    Run this *on the Surface RT*, in the folder that contains the .appx built by Visual Studio
    (Project -> Store -> Create App Packages, ARM only).

    It
      1. imports the test certificate into LocalMachine\TrustedPeople (so the package is trusted),
      2. registers the package with Add-AppxPackage,
      3. prints the installed package.

    Windows RT 8.1 allows trusted apps to be installed without a developer license, but the
    certificate that signed the package must be trusted by the device first - which is what step 1
    does.  It needs an elevated PowerShell window.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Deploy-ToSurfaceRT.ps1 -Package .\SurfaceVnc.Store_1.0.0.0_ARM.appx
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Package,

    # Certificate to trust; by default the .cer next to the package.
    [string] $Certificate,

    # Skips the certificate import (use when the certificate is already trusted).
    [switch] $SkipCertificate
)

$ErrorActionPreference = 'Stop'

$packagePath = (Resolve-Path -Path $Package).Path
Write-Host "Package: $packagePath"

if (-not $SkipCertificate) {
    if (-not $Certificate) {
        $certificatePath = [System.IO.Path]::ChangeExtension($packagePath, '.cer')
    } else {
        $certificatePath = (Resolve-Path -Path $Certificate).Path
    }

    if (-not (Test-Path -Path $certificatePath)) {
        throw "Certificate not found: $certificatePath (pass -Certificate or -SkipCertificate)"
    }

    Write-Host "Trusting certificate: $certificatePath"
    Import-Certificate -FilePath $certificatePath -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
}

Write-Host 'Registering the package ...'
Add-AppxPackage -Path $packagePath

$installed = Get-AppxPackage -Name 'SurfaceVnc.Store'
if ($installed) {
    Write-Host ''
    Write-Host 'Installed:' -ForegroundColor Green
    $installed | Format-List Name, Version, Architecture, PackageFullName, InstallLocation
    Write-Host 'Open "SurfaceVnc" from the Start screen.'
} else {
    Write-Warning 'The package was registered but could not be found by name.'
}
