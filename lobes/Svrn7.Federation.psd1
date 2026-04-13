#
# Module manifest — Svrn7.Federation
# Wraps ISvrn7Driver (44 members): key generation, DID construction,
# Society/citizen lifecycle, DID method governance, balance, transfers,
# DID Document registry, VC registry, Merkle audit log, Federation supply,
# and GDPR Article 17 erasure.
#
# SOVRONA (SVRN7) — Web 7.0 Shared Reserve Currency (SRC)
# Web 7.0 Foundation | https://github.com/web7foundation/svrn7
#

@{
    RootModule        = 'Svrn7.Federation.psm1'
    ModuleVersion     = '0.7.0'
    GUID              = 'f1e2d3c4-b5a6-7890-fedc-ba9876543210'
    Author            = 'Michael Herman'
    CompanyName       = 'Web 7.0 Foundation'
    Copyright         = 'Copyright (c) 2026 Michael Herman (Alberta, Canada). MIT License.'
    Description       = @'
PowerShell script module exposing ISvrn7Driver as idiomatic cmdlets.

Covers all Federation-level operations: secp256k1/Ed25519 key generation,
DID string construction, DID resolution, Society and citizen registration
and lifecycle, SVRN7/grana balance queries, single and batch signed transfer
submission, DID method namespace governance, DID Document registry, Verifiable
Credential registry, RFC 6962 Merkle audit log management, Federation supply
updates, and GDPR Article 17 erasure.

Requires compiled Svrn7 .NET 8 assemblies in bin/ adjacent to the module,
or the SVRN7_BIN_PATH environment variable pointing to the assembly folder.
Call Initialize-Svrn7Federation before using any other cmdlet.

See also: Svrn7.Society — Society-level cmdlets (ISvrn7SocietyDriver).
'@

    PowerShellVersion    = '7.2'
    CompatiblePSEditions = @('Core')

    FunctionsToExport = @(
        # Initialisation
        'Initialize-Svrn7Federation'

        # Cryptography
        'New-Svrn7KeyPair'
        'New-Svrn7Ed25519KeyPair'
        'Invoke-Svrn7SignSecp256k1'
        'Test-Svrn7SignatureSecp256k1'

        # DID construction and resolution
        'New-Svrn7Did'
        'Resolve-Svrn7Did'
        'Test-Svrn7DidActive'

        # Citizen lifecycle
        'Register-Svrn7Citizen'
        'Get-Svrn7Citizen'
        'Test-Svrn7CitizenActive'
        'Get-Svrn7CitizenDids'
        'Resolve-Svrn7CitizenPrimaryDid'

        # Society lifecycle
        'Register-Svrn7Society'
        'Get-Svrn7Society'
        'Test-Svrn7SocietyActive'
        'Disable-Svrn7Society'

        # Epoch
        'Get-Svrn7CurrentEpoch'

        # DID method governance
        'Register-Svrn7DidMethod'
        'Unregister-Svrn7DidMethod'
        'Get-Svrn7DidMethodStatus'
        'Get-Svrn7DidMethods'

        # Balance
        'Get-Svrn7Balance'

        # Transfers
        'Invoke-Svrn7Transfer'
        'Invoke-Svrn7BatchTransfer'

        # Federation supply
        'Get-Svrn7Federation'
        'Update-Svrn7FederationSupply'

        # VC registry
        'Get-Svrn7VcById'
        'Get-Svrn7VcsBySubject'
        'Revoke-Svrn7Vc'

        # Merkle log
        'Get-Svrn7MerkleRoot'
        'Get-Svrn7MerkleLogSize'
        'Get-Svrn7MerkleTreeHead'
        'Invoke-Svrn7SignMerkleTreeHead'

        # GDPR
        'Invoke-Svrn7GdprErasure'
    )

    CmdletsToExport   = @()
    VariablesToExport = @()
    AliasesToExport   = @()

    PrivateData = @{
        PSData = @{
            Tags       = @('SVRN7','SOVRONA','Web7','DID','SRC','Federation',
                           'DecentralisedIdentity','DigitalSociety','Merkle','GDPR','VC')
            ProjectUri = 'https://github.com/web7foundation/svrn7'
            LicenseUri = 'https://opensource.org/licenses/MIT'
        }
    }
}
