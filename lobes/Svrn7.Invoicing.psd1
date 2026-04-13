@{
    # Module manifest for Svrn7.Invoicing
    # Derived from: DSA 0.24 Epoch 0 — Parchment Programming Modeling Language (PPML)
    # Protocol URIs: https://svrn7.net/protocols/invoice/1.0/*

    ModuleVersion     = '0.8.0'
    GUID              = '55998ff7-7e2e-4f52-8ebd-279a7b0e5a01'
    Author            = 'Michael Herman'
    CompanyName       = 'Web 7.0 Foundation'
    Copyright         = 'Copyright (c) 2026 Michael Herman (Alberta, Canada). MIT License.'
    Description       = 'SVRN7 Invoicing LOBE — invoice processing via DIDComm invoice/1.0/* protocol.'
    PowerShellVersion = '7.0'

    RootModule        = 'Svrn7.Invoicing.psm1'

    FunctionsToExport = @(
        'ConvertFrom-TdaInvoiceRequest',
        'Resolve-InvoiceAmount',
        'New-TdaInvoiceReceipt',
        'Send-TdaInvoiceError'
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
