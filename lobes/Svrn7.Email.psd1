@{
    # Module manifest for Svrn7.Email
    # Derived from: DSA 0.24 Epoch 0 — Parchment Programming Modeling Language (PPML)
    # Protocol URIs: https://svrn7.net/protocols/email/1.0/*

    ModuleVersion     = '0.8.0'
    GUID              = '8c388524-c1ce-498f-9fef-e8e6bf5d0d94'
    Author            = 'Michael Herman'
    CompanyName       = 'Web 7.0 Foundation'
    Copyright         = 'Copyright (c) 2026 Michael Herman (Alberta, Canada). MIT License.'
    Description       = 'SVRN7 Email LOBE — DIDComm-native email using RFC 5322 tunneling.'
    PowerShellVersion = '7.0'

    RootModule        = 'Svrn7.Email.psm1'

    FunctionsToExport = @(
        'Receive-TdaEmail',
        'Send-TdaEmail'
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
