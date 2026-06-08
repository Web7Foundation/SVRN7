#Requires -Version 7.2
<#
.SYNOPSIS
    Sets 6 static secp256k1 keypair variables for SVRN7 development and testing.

.DESCRIPTION
    Dot-source this script to populate $kp1..$kp6 in the caller's scope.
    Each variable is a PSCustomObject matching the shape returned by New-Svrn7KeyPair:
        PublicKeyHex    [string]   33-byte compressed secp256k1 public key (hex)
        PrivateKeyBytes [byte[]]   32-byte raw private key
        PrivateKeyHex   [string]   32-byte private key (hex)
        Algorithm       [string]   'Secp256k1'

.EXAMPLE
    . .\Invoke-SetStaticKeypairs.ps1
    $kp1.PublicKeyHex
    $kp3 | New-Svrn7Did -MethodName 'alpha'
#>

function script:HexToBytes([string]$hex) {
    $bytes = [byte[]]::new($hex.Length / 2)
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        $bytes[$i] = [Convert]::ToByte($hex.Substring($i * 2, 2), 16)
    }
    $bytes
}

function script:MakeKp([string]$pubHex, [string]$privHex) {
    [PSCustomObject]@{
        PSTypeName      = 'Svrn7.KeyPair'
        PublicKeyHex    = $pubHex
        PrivateKeyBytes = HexToBytes $privHex
        PrivateKeyHex   = $privHex
        Algorithm       = 'Secp256k1'
    }
}

$kp1 = MakeKp `
    '03395a7cd757d468e65abf2835b657705cb6b23577f0d71c0b89c8381ae759d045' `
    '3b6c300f1a0933a1b69216a9b3c2d607b68edd4a1d4ff6a6f0774566dff327e4'

$kp2 = MakeKp `
    '03fcdcbb04ca4fa99e4d391b68a1fbacf73485f4610c36e808f38d06cfced0f3eb' `
    'fccfd257f27799d0532e4a7659c7bf8277980effbc7721f6e62a381ab01e242f'

$kp3 = MakeKp `
    '033c8fd2619526825b15e05d7fe0341cf96a942e6e93697d6a94dd9ba0bb723e3e' `
    'c16c23365d8f711c2627c103f33a214d4c975f4d321fc26b9ca3660ec11ca9ec'

$kp4 = MakeKp `
    '0319cc4133ed7ace27b8e3a3ff954e5610bd50f2b9a8d181a2dd09c8a06529a068' `
    'd9f046c1a05af257b8fa2898ea46bfc8df9b485151e4080dd6c6894b79d46a7d'

$kp5 = MakeKp `
    '03962d909ae720f7a6872966748980b800f188e1d688d8faba4dcdb08ed7d8a751' `
    '347a7fd8ab8126b09df2c7fed2a7ac21fa372a336db3fbc9d58708301d1d1cf9'

$kp6 = MakeKp `
    '0313dd89abf88b7874624497ae2ec2cb6e39884def66a131e0288b10283b51261d' `
    'a48600ade98d5373978477b5f673a5ef875edefbb2b2ae83652994bdff5e6ed9'

$kp7 = MakeKp `
    '03edb978c825d2a16b57e3713ad834ed07538f627f4270dae525edb5fe22bc990a' `
    'd263b2407ee20f6079701d53e6fbbfea0d43d9a6e4e7572177df7cd65bcb7fb3'

$kp8 = MakeKp `
    '028248e9c4ff00eb437e6368203e3e2f19aa344c4b84754049bf32871267149fe9' `
    '913bca456ae02b108ba2a34a009ad506439ece000bc2d321335c4f8fdd3691bd'

$kp9 = MakeKp `
    '036717dbb9bc316d23ddebbc3af8825d168cb2ebfdd6521c3c62d26fc1808b8ad2' `
    '065cbe15f2f7d6f09b13e16f14da163800fa9c45a0a01eed90d453008105f901'

$kp10 = MakeKp `
    '035f9e94789d0c20d5c712c7bacaa87ac619fbc6869e5d4790d61ee16b4d9c9f8f' `
    'da65f8204f05ad1a536a4af7f7696c6840eb3ff0ada56f27e2df03b9c768315d'

$i = 1
foreach ($kp in @($kp1, $kp2, $kp3, $kp4, $kp5, $kp6, $kp7, $kp8, $kp9, $kp10)) {
    Write-Host "`$kp$i" -ForegroundColor Cyan -NoNewline
    Write-Host "  pub  $($kp.PublicKeyHex)"
    Write-Host "       priv $($kp.PrivateKeyHex)"
    $i++
}
