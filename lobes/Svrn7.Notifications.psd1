@{
    # Module manifest for Svrn7.Notifications
    # Derived from: DSA 0.24 Epoch 0 — Parchment Programming Modeling Language (PPML)
    # Protocol URIs: https://svrn7.net/protocols/notification/1.0/*

    ModuleVersion     = '0.8.0'
    GUID              = 'f48cf634-24ad-45ac-a538-11e142abacaf'
    Author            = 'Michael Herman'
    CompanyName       = 'Web 7.0 Foundation'
    Copyright         = 'Copyright (c) 2026 Michael Herman (Alberta, Canada). MIT License.'
    Description       = 'SVRN7 Notifications LOBE — net-new DIDComm notification/1.0/* protocol.'
    PowerShellVersion = '7.0'

    RootModule        = 'Svrn7.Notifications.psm1'

    FunctionsToExport = @(
        'Invoke-TdaNotification',
        'Send-TdaAlert',
        'Test-TdaInboxDepth'
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
