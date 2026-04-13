@{
    ModuleVersion     = '0.8.0'
    GUID              = 'fb5779b8-36e1-4e80-8660-046c7b0d0d10'
    Author            = 'Michael Herman'
    CompanyName       = 'Web 7.0 Foundation'
    Copyright         = 'Copyright (c) 2026 Michael Herman (Alberta, Canada). MIT License.'
    Description       = 'SVRN7 Identity LOBE — DID Document and Verifiable Credential resolution via DIDComm. Handles did/1.0/* and vc/1.0/* protocol families.'
    PowerShellVersion = '7.0'

    RootModule        = 'Svrn7.Identity.psm1'

    FunctionsToExport = @(
        'Resolve-Svrn7Did',
        'Get-Svrn7VcById',
        'Resolve-Svrn7CitizenIdentity'
    )

    CmdletsToExport   = @()
    VariablesToExport = @()
    AliasesToExport   = @()

    PrivateData = @{
        PSData = @{
            Tags       = @('SVRN7', 'Web70', 'DIDComm', 'TDA', 'LOBE', 'Identity', 'DID', 'VC', 'ParchmentProgramming')
            ProjectUri = 'https://svrn7.net'
            LicenseUri = 'https://opensource.org/licenses/MIT'
        }
    }
}
