@{
    # Module manifest for Svrn7.Common
    # Derived from: DSA 0.24 Epoch 0 — Parchment Programming Modeling Language (PPML)
    # Protocol URIs: N/A

    ModuleVersion     = '0.8.0'
    GUID              = 'e3d228d7-7caa-45e1-a937-5e4681e7bbbb'
    Author            = 'Michael Herman'
    CompanyName       = 'Web 7.0 Foundation'
    Copyright         = 'Copyright (c) 2026 Michael Herman (Alberta, Canada). MIT License.'
    Description       = 'SVRN7 Common LOBE — shared helpers for all SVRN7 LOBE modules.'
    PowerShellVersion = '7.0'

    RootModule        = 'Svrn7.Common.psm1'

    FunctionsToExport = @(
        ''
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
