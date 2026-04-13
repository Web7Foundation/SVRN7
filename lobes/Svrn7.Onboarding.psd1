@{
    # Module manifest for Svrn7.Onboarding
    # Derived from: DSA 0.24 Epoch 0 — Parchment Programming Modeling Language (PPML)
    # Protocol URIs: https://svrn7.net/protocols/onboard/1.0/*

    ModuleVersion     = '0.8.0'
    GUID              = 'c18618ac-cf6d-45f4-9bb7-5ce6e5aad1f0'
    Author            = 'Michael Herman'
    CompanyName       = 'Web 7.0 Foundation'
    Copyright         = 'Copyright (c) 2026 Michael Herman (Alberta, Canada). MIT License.'
    Description       = 'SVRN7 Onboarding LOBE — citizen registration via DIDComm onboard/1.0/* protocol.'
    PowerShellVersion = '7.0'

    RootModule        = 'Svrn7.Onboarding.psm1'

    FunctionsToExport = @(
        'ConvertFrom-TdaOnboardRequest',
        'New-TdaOnboardReceipt',
        'Send-TdaOnboardError'
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
