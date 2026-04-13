@{
    # Module manifest for Svrn7.Presence
    # Derived from: DSA 0.24 Epoch 0 — Parchment Programming Modeling Language (PPML)
    # Protocol URIs: https://svrn7.net/protocols/presence/1.0/*

    ModuleVersion     = '0.8.0'
    GUID              = 'f859fb4a-22fa-43fd-8a89-dcaaf54409e6'
    Author            = 'Michael Herman'
    CompanyName       = 'Web 7.0 Foundation'
    Copyright         = 'Copyright (c) 2026 Michael Herman (Alberta, Canada). MIT License.'
    Description       = 'SVRN7 Presence LOBE — net-new DIDComm presence/1.0/* protocol.'
    PowerShellVersion = '7.0'

    RootModule        = 'Svrn7.Presence.psm1'

    FunctionsToExport = @(
        'Update-TdaPresence',
        'Add-TdaPresenceSubscription',
        'Publish-TdaPresence',
        'Get-TdaPresence'
    )

    CmdletsToExport   = @()
    VariablesToExport = @()
    AliasesToExport   = @()

    PrivateData = @{
        PSData = @{
            Tags       = @('SVRN7', 'Web70', 'DIDComm', 'TDA', 'LOBE', 'ParchmentProgramming')
            ProjectUri = 'https://svrn7.net'
            LicenseUri = 'https://opensource.org/licenses/MIT'
        }
    }
}
