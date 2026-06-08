@{
    ModuleVersion     = '0.8.0'
    GUID              = '1c9f9fc4-f60c-42da-8ea2-5c577ca58532'
    Author            = 'Michael Herman'
    CompanyName       = 'Web 7.0 Foundation'
    Copyright         = 'Copyright (c) 2026 Michael Herman (Alberta, Canada). MIT License.'
    Description       = 'LOBE packaging pipeline for SVRN7 — build, validate, install, and publish LOBE NuGet packages.'
    PowerShellVersion = '7.0'

    RootModule        = 'Pando.Packaging.psm1'

    FunctionsToExport = @(
        'New-LOBEPackage',
        'Test-LOBEPackage',
        'Install-LOBEPackage',
        'Publish-LOBEPackage'
    )

    CmdletsToExport   = @()
    VariablesToExport = @()
    AliasesToExport   = @()

    PrivateData = @{
        PSData = @{
            Tags       = @('SVRN7', 'Web70', 'LOBE', 'NuGet', 'Packaging', 'ParchmentProgramming')
            ProjectUri = 'https://svrn7.net'
            LicenseUri = 'https://opensource.org/licenses/MIT'
        }
    }
}
