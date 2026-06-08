#Requires -Version 7.0

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

. "$PSScriptRoot\New-LOBEPackage.ps1"
. "$PSScriptRoot\Test-LOBEPackage.ps1"
. "$PSScriptRoot\Install-LOBEPackage.ps1"
. "$PSScriptRoot\Publish-LOBEPackage.ps1"
