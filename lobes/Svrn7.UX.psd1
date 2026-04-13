@{
    ModuleVersion     = '0.8.0'
    GUID              = '2a49ba9d-ede7-44c5-b664-9ba778d3b0e7'
    Author            = 'Michael Herman'
    CompanyName       = 'Web 7.0 Foundation'
    Copyright         = 'Copyright (c) 2026 Michael Herman (Alberta, Canada). MIT License.'
    Description       = 'SVRN7 UX LOBE — Platform-specific user interface adapter. Translates TDA events into citizen-facing UI notifications and relays UX-initiated actions into DIDComm pipelines.'
    PowerShellVersion = '7.0'

    RootModule        = 'Svrn7.UX.psm1'

    FunctionsToExport = @(
        'Render-TdaBalanceUpdate',
        'Render-TdaNotification',
        'Render-TdaRegistrationComplete',
        'New-TdaTransferIntent'
    )

    CmdletsToExport   = @()
    VariablesToExport = @()
    AliasesToExport   = @()

    PrivateData = @{
        PSData = @{
            Tags       = @('SVRN7', 'Web70', 'DIDComm', 'TDA', 'LOBE', 'UX', 'ParchmentProgramming')
            ProjectUri = 'https://svrn7.net'
            LicenseUri = 'https://opensource.org/licenses/MIT'
        }
    }
}
