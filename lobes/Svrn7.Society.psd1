#
# Module manifest — Svrn7.Society
# Wraps ISvrn7SocietyDriver: all Society-specific operations.
# SOVRONA (SVRN7) — Web 7.0 Shared Reserve Currency (SRC)
#

@{
    RootModule        = 'Svrn7.Society.psm1'
    ModuleVersion     = '0.7.0'
    GUID              = 'c3d4e5f6-a7b8-9012-cdef-012345678901'
    Author            = 'Michael Herman'
    CompanyName       = 'Web 7.0 Foundation'
    Copyright         = 'Copyright (c) 2026 Michael Herman (Alberta, Canada). MIT License.'
    Description       = @'
Exposes ISvrn7SocietyDriver as PowerShell cmdlets.

ISvrn7SocietyDriver extends ISvrn7Driver with Society-specific operations not
in Svrn7.Federation: citizen registration with automatic 1,000 SVRN7 endowment,
additional DID issuance for existing citizens, incoming DIDComm transfer handling,
cross-Society Epoch 1 transfers via TransferOrderCredential, Federation-directed
transfers, overdraft status and record queries, Society membership enumeration and
testing, self-service DID method name governance, and cross-Society VC resolution
via DIDComm fan-out with partial-result manifests.

Requires Svrn7.Federation to be imported first. Call Connect-Svrn7Society to
create the ISvrn7SocietyDriver singleton before using any cmdlet here.
'@

    PowerShellVersion    = '7.2'
    CompatiblePSEditions = @('Core')

    RequiredModules = @(
        @{ ModuleName = 'Svrn7.Federation'; ModuleVersion = '0.7.0' }
    )

    FunctionsToExport = @(
        # Initialisation
        'Connect-Svrn7Society'

        # Society identity
        'Get-Svrn7OwnSociety'

        # Citizen registration with endowment
        'Register-Svrn7CitizenInSociety'

        # Multi-DID citizen management
        'Add-Svrn7CitizenDid'

        # DIDComm transfer entry point
        'Invoke-Svrn7IncomingTransfer'

        # Cross-Society transfers
        'Invoke-Svrn7ExternalTransfer'
        'Invoke-Svrn7FederationTransfer'

        # Overdraft management
        'Get-Svrn7OverdraftStatus'
        'Get-Svrn7OverdraftRecord'

        # Society membership
        'Get-Svrn7SocietyMembers'
        'Test-Svrn7SocietyMember'

        # Self-service DID method governance
        'Register-Svrn7SocietyDidMethod'
        'Unregister-Svrn7SocietyDidMethod'
        'Get-Svrn7SocietyDidMethods'

        # Cross-Society VC resolution
        'Find-Svrn7VcsBySubject'
    )

    CmdletsToExport   = @()
    VariablesToExport = @()
    AliasesToExport   = @()

    PrivateData = @{
        PSData = @{
            Tags       = @('SVRN7','SOVRONA','Web7','DID','SRC','Society',
                           'DIDComm','DecentralisedIdentity','DigitalSociety')
            ProjectUri = 'https://github.com/web7foundation/svrn7'
            LicenseUri = 'https://opensource.org/licenses/MIT'
        }
    }
}
