@{
    # Module manifest for Svrn7.Calendar
    # Derived from: DSA 0.24 Epoch 0 — Parchment Programming Modeling Language (PPML)
    # Protocol URIs: https://svrn7.net/protocols/calendar/1.0/*

    ModuleVersion     = '0.8.0'
    GUID              = '2fef5054-93c4-4b82-aab4-f83835613224'
    Author            = 'Michael Herman'
    CompanyName       = 'Web 7.0 Foundation'
    Copyright         = 'Copyright (c) 2026 Michael Herman (Alberta, Canada). MIT License.'
    Description       = 'SVRN7 Calendar LOBE — DIDComm-native calendar using iCalendar (RFC 5545) tunneling.'
    PowerShellVersion = '7.0'

    RootModule        = 'Svrn7.Calendar.psm1'

    FunctionsToExport = @(
        'Import-TdaCalendarEvent',
        'Receive-TdaMeetingRequest',
        'New-TdaCalendarResponse'
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
