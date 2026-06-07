#Requires -Version 7.2
$modPath = Join-Path $PSScriptRoot 'src\Svrn7.TDA\bin\Debug\net8.0\lobes\Svrn7.Federation\Svrn7.Federation.psd1'
Import-Module $modPath -Force
1..10 | ForEach-Object {
    $kp = New-Svrn7KeyPair
    "${_}|$($kp.PublicKeyHex)|$($kp.PrivateKeyHex)"
}
