#Requires -Version 7.2
#Requires -PSEdition Core

<#
.SYNOPSIS
    Svrn7.Society — PowerShell cmdlets for ISvrn7SocietyDriver.

.DESCRIPTION
    Script module exposing every Society-scoped operation of the SOVRONA (SVRN7)
    ISvrn7SocietyDriver as PowerShell-idiomatic cmdlets with full comment-based help,
    pipeline support, -WhatIf/-Confirm where state is mutated, and structured
    PSCustomObject output.

    ISvrn7SocietyDriver extends ISvrn7Driver.  This module covers only the methods
    that ISvrn7SocietyDriver adds on top of ISvrn7Driver.  All inherited methods
    (balance queries, transfers, DID document registry, VC registry, Merkle log,
    GDPR erasure, etc.) are exposed by the Svrn7.Federation module.

    DEPENDENCY
    Svrn7.Federation must be imported and Initialize-Svrn7Federation called before
    importing this module.  Call Connect-Svrn7Society after import to create the
    ISvrn7SocietyDriver singleton.

    INTERFACES COVERED
    ISvrn7SocietyDriver (Svrn7.Society assembly) — all 15 Society-native members:
      GetOwnSocietyAsync, RegisterCitizenInSocietyAsync, AddCitizenDidAsync,
      HandleIncomingTransferMessageAsync, TransferToExternalCitizenAsync,
      TransferToFederationAsync, GetOverdraftStatusAsync, GetOverdraftRecordAsync,
      GetMemberCitizenDidsAsync, IsMemberAsync, RegisterSocietyDidMethodAsync,
      DeregisterSocietyDidMethodAsync, GetSocietyDidMethodsAsync,
      FindVcsBySubjectAcrossSocietiesAsync, SocietyDid (property)

    MODULE VERSION: 0.7.0
    AUTHOR:         Michael Herman, Web 7.0 Foundation
    COPYRIGHT:      (c) 2026 Michael Herman (Alberta, Canada). MIT License.
    SPECS:          draft-herman-web7-society-architecture-00
                    draft-herman-svrn7-monetary-protocol-00
                    draft-herman-didcomm-svrn7-transfer-00
                    draft-herman-svrn7-overdraft-protocol-00
                    draft-herman-did-method-governance-00
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Dot-source shared helpers (driver singletons, type names, helpers)
. (Join-Path $PSScriptRoot 'Svrn7.Common.psm1')

# $Script:SocietyDriver is declared in Svrn7.Common.psm1

###############################################################################
#region INITIALISATION
###############################################################################

function Connect-Svrn7Society {
    <#
    .SYNOPSIS
        Creates the ISvrn7SocietyDriver singleton for this Society deployment.

    .DESCRIPTION
        Builds a Svrn7SocietyOptions configuration and resolves an
        ISvrn7SocietyDriver from the Microsoft.Extensions.DependencyInjection
        container via AddSvrn7Society(). Must be called once — after
        Initialize-Svrn7Federation — before any other Society cmdlet.

        The Society driver wraps the Federation-level ISvrn7Driver singleton and
        adds Society-scoped state: SocietyDid, FederationDid, DIDComm messaging
        keys, owned DID method names, and overdraft configuration.

        Calling Connect-Svrn7Society when a driver already exists is a no-op
        unless -Force is specified.

    .PARAMETER SocietyDid
        The DID of this Society deployment (e.g. 'did:sovronia:my-society').

    .PARAMETER FederationDid
        The DID of the Federation this Society is registered with.

    .PARAMETER DidMethodNames
        One or more DID method names this Society owns. At least one is required.
        All names must match [a-z0-9]+. The first element is the primary method
        name; additional elements are secondary names.

    .PARAMETER SocietyMessagingKeyEd25519Hex
        Hex-encoded Ed25519 private key (32 bytes) for DIDComm message signing and
        decryption. Used for SignThenEncrypt packing of all cross-Society DIDComm
        messages. Omit to generate an ephemeral key (scripting only — not suitable
        for production DIDComm, as overdraft facility will not function correctly).

    .PARAMETER FederationMessagingPublicKeyEd25519Hex
        Hex-encoded Ed25519 public key (32 bytes) of the Federation DIDComm
        messaging endpoint. Used to address OverdraftDrawRequest messages to the
        Federation. Omit to disable the overdraft facility.

    .PARAMETER DrawAmountGrana
        Fixed grana drawn per overdraft event.
        Default: 1,000,000,000,000 (1,000 SVRN7).

    .PARAMETER OverdraftCeilingGrana
        Maximum cumulative outstanding overdraft before citizen registration is
        blocked by SocietyEndowmentDepletedException.
        Default: 10,000,000,000,000 (10,000 SVRN7).

    .PARAMETER DbPath
        Root folder for LiteDB files. Must match the path used by the Federation
        module. Defaults to SVRN7_DB_PATH environment variable or the system temp
        directory under svrn7-ps/.

    .PARAMETER Force
        Dispose the existing Society driver (if any) and reinitialise.

    .INPUTS
        None. This cmdlet does not accept pipeline input.

    .OUTPUTS
        None. The ISvrn7SocietyDriver is stored as a module-level singleton.

    .EXAMPLE
        PS> Initialize-Svrn7Federation
        PS> Connect-Svrn7Society `
                -SocietyDid    'did:sovronia:my-society' `
                -FederationDid 'did:drn:the-federation' `
                -DidMethodNames @('sovronia')

    .EXAMPLE
        PS> Connect-Svrn7Society `
                -SocietyDid                         'did:sovronia:my-society' `
                -FederationDid                      'did:drn:the-federation' `
                -DidMethodNames                     @('sovronia','sovroniamed') `
                -SocietyMessagingKeyEd25519Hex      $myEd25519PrivHex `
                -FederationMessagingPublicKeyEd25519Hex $fedEd25519PubHex

        Initialises with DIDComm keys so the overdraft facility can communicate
        with the Federation.

    .NOTES
        C# API: ISvrn7SocietyDriver / Svrn7SocietyOptions (Svrn7.Society)
        Spec:   draft-herman-web7-society-architecture-00 §4.2, §9.2
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string] $SocietyDid,

        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string] $FederationDid,

        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string[]] $DidMethodNames,

        [Parameter()]
        [string] $SocietyMessagingKeyEd25519Hex = '',

        [Parameter()]
        [string] $FederationMessagingPublicKeyEd25519Hex = '',

        [Parameter()]
        [ValidateRange(1L, [long]::MaxValue)]
        [long] $DrawAmountGrana = 1_000_000_000_000L,

        [Parameter()]
        [ValidateRange(1L, [long]::MaxValue)]
        [long] $OverdraftCeilingGrana = 10_000_000_000_000L,

        [Parameter()]
        [string] $DbPath = '',

        [switch] $Force
    )

    if ($Script:SocietyDriver -and -not $Force) {
        Write-Verbose 'Svrn7.Society already connected. Use -Force to reconnect.'
        return
    }

    if ($Script:SocietyDriver -and $Force) {
        try { $Script:SocietyDriver.DisposeAsync().GetAwaiter().GetResult() } catch {}
        $Script:SocietyDriver = $null
    }

    $dbRoot = if ($DbPath) { $DbPath }
              elseif ($env:SVRN7_DB_PATH) { $env:SVRN7_DB_PATH }
              else { Join-Path ([System.IO.Path]::GetTempPath()) 'svrn7-ps' }

    [System.IO.Directory]::CreateDirectory($dbRoot) | Out-Null

    $msgPriv = if ($SocietyMessagingKeyEd25519Hex) {
        [System.Convert]::FromHexString($SocietyMessagingKeyEd25519Hex)
    } else {
        [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
    }

    $fedPub = if ($FederationMessagingPublicKeyEd25519Hex) {
        [System.Convert]::FromHexString($FederationMessagingPublicKeyEd25519Hex)
    } else { [byte[]]@() }

    $services = [Microsoft.Extensions.DependencyInjection.ServiceCollection]::new()

    $services.AddSvrn7Society([Action[Svrn7.Society.Svrn7SocietyOptions]] {
        param($o)
        $o.SocietyDid                         = $SocietyDid
        $o.FederationDid                      = $FederationDid
        $o.DidMethodNames                     = [System.Collections.Generic.List[string]]$DidMethodNames
        $o.SocietyMessagingPrivateKeyEd25519  = $msgPriv
        $o.FederationMessagingPublicKeyEd25519 = $fedPub
        $o.DrawAmountGrana                    = $DrawAmountGrana
        $o.OverdraftCeilingGrana              = $OverdraftCeilingGrana
        $o.Svrn7DbPath = Join-Path $dbRoot 'svrn7.db'
        $o.DidsDbPath  = Join-Path $dbRoot 'svrn7-dids.db'
        $o.VcsDbPath   = Join-Path $dbRoot 'svrn7-vcs.db'
    }) | Out-Null

    $Script:SocietyDriver = $services.BuildServiceProvider()
        .GetRequiredService([Svrn7.Society.ISvrn7SocietyDriver])

    Write-Verbose "Svrn7.Society connected: $SocietyDid"
}

#endregion

###############################################################################
#region SOCIETY IDENTITY — ISvrn7SocietyDriver.SocietyDid / GetOwnSocietyAsync
###############################################################################

function Get-Svrn7OwnSociety {
    <#
    .SYNOPSIS
        Retrieves this Society's own SocietyRecord from the registry.

    .DESCRIPTION
        Wraps ISvrn7SocietyDriver.GetOwnSocietyAsync(). Returns the SocietyRecord
        for this deployment, including the Society name, primary DID method name,
        epoch, overdraft configuration, and registration timestamp.

        The SocietyDid property of the driver is also available directly as the
        string returned by the SocietyDid property of the returned object.

    .INPUTS
        None. This cmdlet does not accept pipeline input.

    .OUTPUTS
        [Svrn7.Core.Models.SocietyRecord] or $null if not yet registered.

    .EXAMPLE
        PS> Get-Svrn7OwnSociety | Select-Object SocietyName, PrimaryDidMethodName

    .EXAMPLE
        PS> $soc = Get-Svrn7OwnSociety
        PS> "This is '$($soc.SocietyName)' running at epoch $($soc.CurrentEpoch)"

    .NOTES
        C# API: ISvrn7SocietyDriver.GetOwnSocietyAsync()
        C# API: ISvrn7SocietyDriver.SocietyDid (property)
        Spec:   draft-herman-web7-society-architecture-00 §4.2
    #>
    [CmdletBinding()]
    param()

    Assert-SocietyDriver
    Write-Verbose "Retrieving own Society record: $($Script:SocietyDriver.SocietyDid)"
    $Script:SocietyDriver.GetOwnSocietyAsync().GetAwaiter().GetResult()
}

#endregion

###############################################################################
#region CITIZEN REGISTRATION — ISvrn7SocietyDriver.RegisterCitizenInSocietyAsync
###############################################################################

function Register-Svrn7CitizenInSociety {
    <#
    .SYNOPSIS
        Registers a new Citizen as a member of this Society, including the
        1,000 SVRN7 endowment UTXO transfer.

    .DESCRIPTION
        Wraps ISvrn7SocietyDriver.RegisterCitizenInSocietyAsync(). Onboards the
        citizen into this Society by:
          1. Creating a CitizenRecord and SocietyMembershipRecord.
          2. Creating the citizen's wallet.
          3. Transferring exactly 1,000 SVRN7 (10^9 grana) from the Society wallet
             to the citizen wallet as the endowment (real UTXO — not synthetic).
          4. Issuing a Svrn7EndowmentCredential VC to the citizen.
          5. Appending a CitizenRegistration entry to the Merkle audit log.

        If the Society wallet balance is below 1,000 SVRN7 at the start of this
        call, the overdraft facility is invoked automatically: a DIDComm
        OverdraftDrawRequest is sent to the Federation (synchronous, 30-second
        timeout by default). Registration fails with SocietyEndowmentDepletedException
        if the overdraft ceiling would be exceeded. Check Get-Svrn7OverdraftStatus
        before bulk registration to avoid unexpected failures.

    .PARAMETER CitizenDid
        The DID string for the new citizen. Derive with New-Svrn7Did using a method
        name owned by this Society.

    .PARAMETER KeyPair
        The Svrn7.KeyPair (secp256k1) for the new citizen. PublicKeyHex is stored in
        the citizen record and used for transfer signature verification.

    .PARAMETER PreferredMethodName
        Optional. If specified, the citizen's DID is issued under this method name
        rather than the Society's primary method name. Must be Active and owned by
        this Society.

    .INPUTS
        None. This cmdlet does not accept pipeline input.

    .OUTPUTS
        PSCustomObject [Svrn7.CitizenRegistration]
            CitizenDid      [string]   The registered citizen DID.
            SocietyDid      [string]   This Society's DID.
            EndowmentSvrn7  [decimal]  Always 1000.000000.
            EndowmentGrana  [long]     Always 1,000,000,000.
            MethodName      [string]   The method name used (empty = Society primary).
            Success         [bool]     Always $true (throws on failure).

    .EXAMPLE
        PS> $kp  = New-Svrn7KeyPair
        PS> $did = (New-Svrn7Did -KeyPair $kp -MethodName 'sovronia').Did
        PS> Register-Svrn7CitizenInSociety -CitizenDid $did -KeyPair $kp

    .EXAMPLE
        PS> Register-Svrn7CitizenInSociety -CitizenDid $did -KeyPair $kp `
                -PreferredMethodName 'sovroniamed'

        Registers the citizen under a secondary method name.

    .NOTES
        C# API: ISvrn7SocietyDriver.RegisterCitizenInSocietyAsync(RegisterCitizenInSocietyRequest)
        Spec:   draft-herman-web7-society-architecture-00 §4.3
                draft-herman-svrn7-monetary-protocol-00 §8
                draft-herman-svrn7-overdraft-protocol-00 §5–§7
    #>
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium')]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string] $CitizenDid,

        [Parameter(Mandatory)]
        [PSCustomObject] $KeyPair,

        [Parameter()]
        [ValidatePattern('^[a-z0-9]+$')]
        [string] $PreferredMethodName = ''
    )

    Assert-SocietyDriver

    $societyDid = $Script:SocietyDriver.SocietyDid
    if (-not $PSCmdlet.ShouldProcess("$CitizenDid into $societyDid", 'RegisterCitizenInSociety')) { return }

    Write-Verbose "Registering citizen '$CitizenDid' in Society '$societyDid'..."

    $request = [Svrn7.Core.Models.RegisterCitizenInSocietyRequest]@{
        Did                 = $CitizenDid
        PublicKeyHex        = $KeyPair.PublicKeyHex
        PrivateKeyBytes     = $KeyPair.PrivateKeyBytes
        SocietyDid          = $societyDid
        PreferredMethodName = if ($PreferredMethodName) { $PreferredMethodName } else { $null }
    }

    $result = $Script:SocietyDriver.RegisterCitizenInSocietyAsync($request).GetAwaiter().GetResult()
    Resolve-OperationResult -Result $result -Operation 'RegisterCitizenInSociety' | Out-Null

    Write-Verbose "Citizen registered: $CitizenDid"

    [PSCustomObject]@{
        PSTypeName     = $Script:TypeCitizenReg
        CitizenDid     = $CitizenDid
        SocietyDid     = $societyDid
        EndowmentSvrn7 = 1000.0M
        EndowmentGrana = 1_000_000_000L
        MethodName     = $PreferredMethodName
        Success        = $true
    }
}

#endregion

###############################################################################
#region MULTI-DID MANAGEMENT — ISvrn7SocietyDriver.AddCitizenDidAsync
###############################################################################

function Add-Svrn7CitizenDid {
    <#
    .SYNOPSIS
        Issues a secondary DID under an additional method name for an existing citizen.

    .DESCRIPTION
        Wraps ISvrn7SocietyDriver.AddCitizenDidAsync(). The secondary DID is derived
        from the same secp256k1 public key as the primary DID so the identifier
        component is identical — only the method name changes:

            Primary:   did:{primaryMethod}:{base58PubKey}
            Secondary: did:{additionalMethod}:{base58PubKey}

        Both DIDs resolve to the same CitizenRecord. Wallet balances and all
        transfer validation (Step 0 normalisation) continue to use the primary DID.
        Secondary DIDs enable context-specific identity presentation: a citizen
        may reveal their health-domain DID without disclosing their general DID.

        The -MethodName must be Active and owned by this Society. Register it first
        with Register-Svrn7SocietyDidMethod if needed.

    .PARAMETER CitizenPrimaryDid
        The primary DID of the citizen. Must already be registered in this Society.

    .PARAMETER MethodName
        The Active method name under which to issue the secondary DID.
        Must match [a-z0-9]+.

    .INPUTS
        None. This cmdlet does not accept pipeline input.

    .OUTPUTS
        PSCustomObject [Svrn7.CitizenDid]
            CitizenPrimaryDid [string]  The citizen's existing primary DID.
            SecondaryDid      [string]  The newly issued secondary DID.
            MethodName        [string]  The method name used.
            Success           [bool]    Always $true (throws on failure).

    .EXAMPLE
        PS> Add-Svrn7CitizenDid `
                -CitizenPrimaryDid 'did:sovronia:3J98...' `
                -MethodName        'sovroniamed'

    .EXAMPLE
        # Register method then issue secondary DID via pipeline
        PS> Register-Svrn7SocietyDidMethod -MethodName 'sovroniamed' |
                ForEach-Object {
                    Add-Svrn7CitizenDid -CitizenPrimaryDid $citizen -MethodName $_.MethodName
                }

    .NOTES
        C# API: ISvrn7SocietyDriver.AddCitizenDidAsync(string citizenPrimaryDid, string methodName)
        Spec:   draft-herman-web7-society-architecture-00 §5.5
                draft-herman-did-method-governance-00 §8
    #>
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Low')]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string] $CitizenPrimaryDid,

        [Parameter(Mandatory)]
        [ValidatePattern('^[a-z0-9]+$')]
        [string] $MethodName
    )

    Assert-SocietyDriver

    if (-not $PSCmdlet.ShouldProcess("$CitizenPrimaryDid under '$MethodName'", 'AddCitizenDid')) { return }

    Write-Verbose "Adding secondary DID under '$MethodName' for '$CitizenPrimaryDid'..."

    $result = $Script:SocietyDriver.AddCitizenDidAsync($CitizenPrimaryDid, $MethodName).GetAwaiter().GetResult()
    Resolve-OperationResult -Result $result -Operation 'AddCitizenDid' | Out-Null

    $identifier   = ($CitizenPrimaryDid -split ':')[-1]
    $secondaryDid = "did:${MethodName}:${identifier}"

    Write-Verbose "Secondary DID issued: $secondaryDid"

    [PSCustomObject]@{
        PSTypeName        = $Script:TypeCitizenDid
        CitizenPrimaryDid = $CitizenPrimaryDid
        SecondaryDid      = $secondaryDid
        MethodName        = $MethodName
        Success           = $true
    }
}

#endregion

###############################################################################
#region DIDCOMM TRANSFER ENTRY — ISvrn7SocietyDriver.HandleIncomingTransferMessageAsync
###############################################################################

function Invoke-Svrn7IncomingTransfer {
    <#
    .SYNOPSIS
        Processes an incoming packed DIDComm transfer message and returns the
        packed DIDComm receipt.

    .DESCRIPTION
        Wraps ISvrn7SocietyDriver.HandleIncomingTransferMessageAsync(). This is
        the DIDComm entry point for all inbound transfers — both same-Society
        (routed internally) and cross-Society TransferOrder messages from remote
        Societies.

        The method:
          1. Unpacks the SignThenEncrypt DIDComm message using the Society's
             Ed25519 messaging private key.
          2. Verifies the JWS signature against the sender's Ed25519 public key.
          3. Checks idempotency on TransferId (Blake3 hex of canonical JSON).
             Returns a cached receipt if the transfer was already processed.
          4. Validates and commits the UTXO transfer.
          5. Issues a TransferReceiptCredential VC.
          6. Appends a CrossSocietyTransferCredit Merkle log entry.
          7. Returns a packed DIDComm SignThenEncrypt receipt message addressed
             to the originating Society.

        The packed receipt should be returned to the DIDComm transport layer for
        delivery to the originating Society.

    .PARAMETER PackedMessage
        The raw packed DIDComm message string as received from the transport layer
        (HTTPS POST body, WebSocket frame, etc.). Must be a JWE compact serialisation.

    .INPUTS
        System.String — packed DIDComm message strings piped directly.

    .OUTPUTS
        PSCustomObject [Svrn7.IncomingTransferResult]
            PackedReceipt [string]  The packed DIDComm receipt to return to sender.
            Success       [bool]    Always $true (throws on failure).

    .EXAMPLE
        PS> $receipt = Invoke-Svrn7IncomingTransfer -PackedMessage $inbound
        PS> # Return $receipt.PackedReceipt to the transport layer

    .EXAMPLE
        PS> $inboundMessages | Invoke-Svrn7IncomingTransfer |
                ForEach-Object { Send-DIDCommReceipt $_.PackedReceipt }

    .NOTES
        C# API: ISvrn7SocietyDriver.HandleIncomingTransferMessageAsync(string)
        Spec:   draft-herman-didcomm-svrn7-transfer-00 §8.3, §12
    #>
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory, ValueFromPipeline)]
        [ValidateNotNullOrEmpty()]
        [string] $PackedMessage
    )

    process {
        Assert-SocietyDriver

        Write-Verbose "Processing incoming DIDComm transfer message ($($PackedMessage.Length) chars)..."

        $receipt = $Script:SocietyDriver.HandleIncomingTransferMessageAsync(
            $PackedMessage).GetAwaiter().GetResult()

        Write-Verbose 'Incoming transfer processed. Receipt packed.'

        [PSCustomObject]@{
            PSTypeName    = 'Svrn7.IncomingTransferResult'
            PackedReceipt = $receipt
            Success       = $true
        }
    }
}

#endregion

###############################################################################
#region CROSS-SOCIETY TRANSFERS
# ISvrn7SocietyDriver.TransferToExternalCitizenAsync / TransferToFederationAsync
###############################################################################

function Invoke-Svrn7ExternalTransfer {
    <#
    .SYNOPSIS
        Initiates a cross-Society Epoch 1 transfer from a citizen in this Society
        to a citizen in another Society via DIDComm SignThenEncrypt.

    .DESCRIPTION
        Wraps ISvrn7SocietyDriver.TransferToExternalCitizenAsync(). Validates
        through the nine-step Society transfer validator (which adds Society
        membership verification as Step 8 to the standard eight-step pipeline),
        debits the payer UTXO, issues a TransferOrderCredential VC, appends a
        CrossSocietyTransferDebit Merkle log entry, and dispatches the credential
        via DIDComm SignThenEncrypt to the target Society.

        The transfer is fire-and-forget: the originating Society debits
        immediately and the receipt arrives asynchronously via the DIDComm inbox
        processing service. Idempotency is guaranteed by TransferId (Blake3 hex
        of the canonical JSON): if the DIDComm delivery is retried, the receiving
        Society returns the cached receipt without double-crediting the payee.

        Requires the ecosystem to be in Epoch 1 or higher. In Epoch 0 use
        Invoke-Svrn7FederationTransfer.

    .PARAMETER PayerDid
        DID of the payer. Must be an active citizen of this Society.

    .PARAMETER PayerKeyPair
        Svrn7.KeyPair (secp256k1) for the payer, used to sign the canonical JSON.

    .PARAMETER PayeeDid
        DID of the payee. Must be an active citizen of the target Society.

    .PARAMETER TargetSocietyDid
        DID of the Society where the payee is registered.

    .PARAMETER AmountSvrn7
        Transfer amount in SVRN7. Mutually exclusive with -AmountGrana.

    .PARAMETER AmountGrana
        Transfer amount in grana. Mutually exclusive with -AmountSvrn7.

    .PARAMETER Memo
        Optional memo, maximum 256 characters.

    .PARAMETER Nonce
        Optional idempotency nonce. A UUID is auto-generated if omitted.

    .INPUTS
        None. This cmdlet does not accept pipeline input.

    .OUTPUTS
        PSCustomObject [Svrn7.ExternalTransferResult]
            TransferId       [string]   Blake3 hex of the canonical JSON.
            PayerDid         [string]
            PayeeDid         [string]
            TargetSocietyDid [string]
            AmountGrana      [long]
            AmountSvrn7      [decimal]
            Nonce            [string]
            Timestamp        [string]
            Memo             [string]
            Status           [string]   'OrderSent' (receipt arrives asynchronously).
            Success          [bool]     Always $true (throws on failure).

    .EXAMPLE
        PS> Invoke-Svrn7ExternalTransfer `
                -PayerDid         $myDid `
                -PayerKeyPair     $kp `
                -PayeeDid         'did:othersoc:alice...' `
                -TargetSocietyDid 'did:othersoc:their-society' `
                -AmountSvrn7      50

    .EXAMPLE
        PS> Invoke-Svrn7ExternalTransfer `
                -PayerDid $d -PayerKeyPair $kp -PayeeDid $p `
                -TargetSocietyDid $t -AmountGrana 50_000_000 `
                -Memo 'Q1 contribution' -WhatIf

    .NOTES
        C# API: ISvrn7SocietyDriver.TransferToExternalCitizenAsync(TransferRequest, string)
        Spec:   draft-herman-didcomm-svrn7-transfer-00 §8
                draft-herman-svrn7-monetary-protocol-00 §7.2
    #>
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium',
                   DefaultParameterSetName = 'BySvrn7')]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory)] [string]        $PayerDid,
        [Parameter(Mandatory)] [PSCustomObject] $PayerKeyPair,
        [Parameter(Mandatory)] [string]        $PayeeDid,
        [Parameter(Mandatory)] [string]        $TargetSocietyDid,

        [Parameter(Mandatory, ParameterSetName = 'BySvrn7')]
        [ValidateRange(0.000001, 1e15)]
        [double] $AmountSvrn7,

        [Parameter(Mandatory, ParameterSetName = 'ByGrana')]
        [ValidateRange(1L, [long]::MaxValue)]
        [long] $AmountGrana,

        [Parameter()]
        [ValidateLength(0, 256)]
        [string] $Memo = '',

        [Parameter()]
        [string] $Nonce = ''
    )

    Assert-SocietyDriver

    $grana = if ($PSCmdlet.ParameterSetName -eq 'BySvrn7') {
        [long][Math]::Round($AmountSvrn7 * 1_000_000)
    } else { $AmountGrana }
    $svrn7          = [decimal]$grana / 1_000_000M
    $effectiveNonce = if ($Nonce) { $Nonce } else { [Guid]::NewGuid().ToString('N') }
    $timestamp      = [DateTimeOffset]::UtcNow.ToString('O')
    $memo           = if ($Memo) { $Memo } else { $null }

    $json    = Build-CanonicalTransferJson $PayerDid $PayeeDid $grana $effectiveNonce $timestamp $memo
    $payload = [System.Text.Encoding]::UTF8.GetBytes($json)
    $sig     = $Script:SocietyDriver.SignSecp256k1($payload, $PayerKeyPair.PrivateKeyBytes)

    Write-Verbose "Cross-Society transfer: $svrn7 SVRN7 → $PayeeDid via $TargetSocietyDid"

    if (-not $PSCmdlet.ShouldProcess($PayerDid,
            "Transfer $svrn7 SVRN7 to $PayeeDid (Society: $TargetSocietyDid)")) { return }

    $request = [Svrn7.Core.Models.TransferRequest]@{
        PayerDid    = $PayerDid
        PayeeDid    = $PayeeDid
        AmountGrana = $grana
        Nonce       = $effectiveNonce
        Timestamp   = [DateTimeOffset]::Parse($timestamp)
        Signature   = $sig
        Memo        = $memo
    }

    $result = $Script:SocietyDriver.TransferToExternalCitizenAsync(
        $request, $TargetSocietyDid).GetAwaiter().GetResult()
    Resolve-OperationResult -Result $result -Operation 'ExternalTransfer' | Out-Null

    $txId = $Script:SocietyDriver.Blake3HexAsync($payload).GetAwaiter().GetResult()
    Write-Verbose "Cross-Society order sent. TransferId: $txId"

    [PSCustomObject]@{
        PSTypeName       = 'Svrn7.ExternalTransferResult'
        TransferId       = $txId
        PayerDid         = $PayerDid
        PayeeDid         = $PayeeDid
        TargetSocietyDid = $TargetSocietyDid
        AmountGrana      = $grana
        AmountSvrn7      = $svrn7
        Nonce            = $effectiveNonce
        Timestamp        = $timestamp
        Memo             = $Memo
        Status           = 'OrderSent'
        Success          = $true
    }
}

function Invoke-Svrn7FederationTransfer {
    <#
    .SYNOPSIS
        Transfers SVRN7 from a citizen in this Society to the Federation wallet.

    .DESCRIPTION
        Wraps ISvrn7SocietyDriver.TransferToFederationAsync(). Permitted in both
        Epoch 0 (where citizen-to-Federation is one of only two allowed payees)
        and Epoch 1. This makes it the only cross-boundary transfer available
        before the ecosystem reaches Epoch 1.

        The payer signs the canonical JSON (field order: PayerDid, PayeeDid,
        AmountGrana, Nonce, Timestamp, Memo) with their secp256k1 private key.
        The transfer is validated through the standard eight-step pipeline.

    .PARAMETER PayerDid
        DID of the payer. Must be an active citizen of this Society.

    .PARAMETER PayerKeyPair
        Svrn7.KeyPair (secp256k1) for the payer.

    .PARAMETER AmountSvrn7
        Amount in SVRN7. Mutually exclusive with -AmountGrana.

    .PARAMETER AmountGrana
        Amount in grana. Mutually exclusive with -AmountSvrn7.

    .PARAMETER Memo
        Optional memo, maximum 256 characters.

    .PARAMETER Nonce
        Optional idempotency nonce. Auto-generated if omitted.

    .INPUTS
        None. This cmdlet does not accept pipeline input.

    .OUTPUTS
        PSCustomObject [Svrn7.FederationTransferResult]
            PayerDid    [string]
            PayeeDid    [string]   The Federation wallet DID.
            AmountGrana [long]
            AmountSvrn7 [decimal]
            Nonce       [string]
            Timestamp   [string]
            Memo        [string]
            Success     [bool]

    .EXAMPLE
        PS> Invoke-Svrn7FederationTransfer `
                -PayerDid     $citizen `
                -PayerKeyPair $kp `
                -AmountSvrn7  10 `
                -Memo         'Monthly Federation dues'

    .NOTES
        C# API: ISvrn7SocietyDriver.TransferToFederationAsync(string, long, string, string, string?)
        Spec:   draft-herman-svrn7-monetary-protocol-00 §6 Step 2 (Epoch 0 permitted payees)
    #>
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium',
                   DefaultParameterSetName = 'BySvrn7')]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory)] [string]        $PayerDid,
        [Parameter(Mandatory)] [PSCustomObject] $PayerKeyPair,

        [Parameter(Mandatory, ParameterSetName = 'BySvrn7')]
        [ValidateRange(0.000001, 1e15)]
        [double] $AmountSvrn7,

        [Parameter(Mandatory, ParameterSetName = 'ByGrana')]
        [ValidateRange(1L, [long]::MaxValue)]
        [long] $AmountGrana,

        [Parameter()]
        [ValidateLength(0, 256)]
        [string] $Memo = '',

        [Parameter()]
        [string] $Nonce = ''
    )

    Assert-SocietyDriver

    $grana          = if ($PSCmdlet.ParameterSetName -eq 'BySvrn7') {
        [long][Math]::Round($AmountSvrn7 * 1_000_000) } else { $AmountGrana }
    $svrn7          = [decimal]$grana / 1_000_000M
    $effectiveNonce = if ($Nonce) { $Nonce } else { [Guid]::NewGuid().ToString('N') }
    $timestamp      = [DateTimeOffset]::UtcNow.ToString('O')
    $memo           = if ($Memo) { $Memo } else { $null }

    # Derive Federation DID from the Society's own record
    $soc    = $Script:SocietyDriver.GetOwnSocietyAsync().GetAwaiter().GetResult()
    $fedDid = if ($soc?.FederationDid) { $soc.FederationDid } else { 'did:drn:federation' }

    # Sign canonical JSON
    $json    = Build-CanonicalTransferJson $PayerDid $fedDid $grana $effectiveNonce $timestamp $memo
    $payload = [System.Text.Encoding]::UTF8.GetBytes($json)
    $sig     = $Script:SocietyDriver.SignSecp256k1($payload, $PayerKeyPair.PrivateKeyBytes)

    if (-not $PSCmdlet.ShouldProcess($PayerDid, "Transfer $svrn7 SVRN7 to Federation")) { return }

    $result = $Script:SocietyDriver.TransferToFederationAsync(
        $PayerDid, $grana, $effectiveNonce, $sig, $memo).GetAwaiter().GetResult()
    Resolve-OperationResult -Result $result -Operation 'FederationTransfer' | Out-Null

    Write-Verbose "Federation transfer committed: $grana grana"

    [PSCustomObject]@{
        PSTypeName  = 'Svrn7.FederationTransferResult'
        PayerDid    = $PayerDid
        PayeeDid    = $fedDid
        AmountGrana = $grana
        AmountSvrn7 = $svrn7
        Nonce       = $effectiveNonce
        Timestamp   = $timestamp
        Memo        = $Memo
        Success     = $true
    }
}

#endregion

###############################################################################
#region OVERDRAFT — ISvrn7SocietyDriver.GetOverdraftStatusAsync / GetOverdraftRecordAsync
###############################################################################

function Get-Svrn7OverdraftStatus {
    <#
    .SYNOPSIS
        Returns the current overdraft status of this Society's wallet.

    .DESCRIPTION
        Wraps ISvrn7SocietyDriver.GetOverdraftStatusAsync(). Returns one of:

          Clean     TotalOverdrawnGrana is zero. No outstanding Federation credit.
          Overdrawn 0 < TotalOverdrawnGrana < OverdraftCeilingGrana. Citizen
                    registration continues; future draws available up to the ceiling.
          Ceiling   TotalOverdrawnGrana >= OverdraftCeilingGrana. Citizen registration
                    is blocked until the Federation reduces TotalOverdrawnGrana via
                    a top-up transfer.

    .INPUTS
        None. This cmdlet does not accept pipeline input.

    .OUTPUTS
        PSCustomObject [Svrn7.OverdraftStatus]
            SocietyDid [string]  This Society's DID.
            Status     [string]  'Clean', 'Overdrawn', or 'Ceiling'.

    .EXAMPLE
        PS> Get-Svrn7OverdraftStatus

    .EXAMPLE
        PS> if ((Get-Svrn7OverdraftStatus).Status -eq 'Ceiling') {
                Write-Warning 'Registration blocked — await Federation top-up.'
            }

    .NOTES
        C# API: ISvrn7SocietyDriver.GetOverdraftStatusAsync()
        Spec:   draft-herman-svrn7-overdraft-protocol-00 §3, §4.1
    #>
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param()

    Assert-SocietyDriver
    $status = $Script:SocietyDriver.GetOverdraftStatusAsync().GetAwaiter().GetResult()

    [PSCustomObject]@{
        PSTypeName = $Script:TypeOverdraftStatus
        SocietyDid = $Script:SocietyDriver.SocietyDid
        Status     = $status.ToString()
    }
}

function Get-Svrn7OverdraftRecord {
    <#
    .SYNOPSIS
        Returns the full overdraft accounting record for this Society.

    .DESCRIPTION
        Wraps ISvrn7SocietyDriver.GetOverdraftRecordAsync(). Returns all overdraft
        accounting fields including the permanent audit counters LifetimeDrawsGrana
        and DrawCount, which never decrease.

        Returns a zeroed record when no overdraft event has ever occurred.

    .INPUTS
        None. This cmdlet does not accept pipeline input.

    .OUTPUTS
        PSCustomObject [Svrn7.OverdraftRecord]
            SocietyDid            [string]
            Status                [string]    'Clean', 'Overdrawn', or 'Ceiling'.
            TotalOverdrawnGrana   [long]      Current outstanding grana (reset-able by top-up).
            OverdraftCeilingGrana [long]      Configured ceiling.
            LifetimeDrawsGrana    [long]      Cumulative grana drawn (never decreases).
            DrawCount             [int]       Total draw events (never decreases).
            DrawAmountGrana       [long]      Configured draw increment.
            LastDrawAt            [datetime]  UTC timestamp of last draw, or MinValue.

    .EXAMPLE
        PS> Get-Svrn7OverdraftRecord | Format-List

    .EXAMPLE
        PS> $rec = Get-Svrn7OverdraftRecord
        PS> "Lifetime draws: $($rec.LifetimeDrawsGrana / 1e6) SVRN7 across $($rec.DrawCount) events"

    .NOTES
        C# API: ISvrn7SocietyDriver.GetOverdraftRecordAsync()
        Spec:   draft-herman-svrn7-overdraft-protocol-00 §4, §10
    #>
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param()

    Assert-SocietyDriver

    $societyDid = $Script:SocietyDriver.SocietyDid
    $rec        = $Script:SocietyDriver.GetOverdraftRecordAsync().GetAwaiter().GetResult()

    if ($null -eq $rec) {
        return [PSCustomObject]@{
            PSTypeName            = 'Svrn7.OverdraftRecord'
            SocietyDid            = $societyDid
            Status                = 'Clean'
            TotalOverdrawnGrana   = 0L
            OverdraftCeilingGrana = 0L
            LifetimeDrawsGrana    = 0L
            DrawCount             = 0
            DrawAmountGrana       = 0L
            LastDrawAt            = [datetime]::MinValue
        }
    }

    [PSCustomObject]@{
        PSTypeName            = 'Svrn7.OverdraftRecord'
        SocietyDid            = $societyDid
        Status                = $rec.Status.ToString()
        TotalOverdrawnGrana   = $rec.TotalOverdrawnGrana
        OverdraftCeilingGrana = $rec.OverdraftCeilingGrana
        LifetimeDrawsGrana    = $rec.LifetimeDrawsGrana
        DrawCount             = $rec.DrawCount
        DrawAmountGrana       = $rec.DrawAmountGrana
        LastDrawAt            = if ($rec.LastDrawAt.HasValue) { $rec.LastDrawAt.Value } `
                                else { [datetime]::MinValue }
    }
}

#endregion

###############################################################################
#region MEMBERSHIP — ISvrn7SocietyDriver.GetMemberCitizenDidsAsync / IsMemberAsync
###############################################################################

function Get-Svrn7SocietyMembers {
    <#
    .SYNOPSIS
        Returns the primary DIDs of all citizens registered in this Society.

    .DESCRIPTION
        Wraps ISvrn7SocietyDriver.GetMemberCitizenDidsAsync(). The list is the
        complete set of primary citizen DIDs linked to this Society via their
        SocietyMembershipRecord. The list grows as citizens are registered;
        GDPR erasure deactivates the citizen's DID but does not remove the
        structural membership record.

    .INPUTS
        None. This cmdlet does not accept pipeline input.

    .OUTPUTS
        PSCustomObject [Svrn7.SocietyMemberList]
            SocietyDid   [string]    This Society's DID.
            MemberCount  [int]       Number of registered members.
            MemberDids   [string[]]  Array of primary citizen DIDs.

    .EXAMPLE
        PS> Get-Svrn7SocietyMembers | Select-Object MemberCount, MemberDids

    .EXAMPLE
        # Pipeline member DIDs into Get-Svrn7Balance (from Federation module)
        PS> (Get-Svrn7SocietyMembers).MemberDids | Get-Svrn7Balance

    .NOTES
        C# API: ISvrn7SocietyDriver.GetMemberCitizenDidsAsync()
        Spec:   draft-herman-web7-society-architecture-00 §4.2
    #>
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param()

    Assert-SocietyDriver
    $dids = $Script:SocietyDriver.GetMemberCitizenDidsAsync().GetAwaiter().GetResult()

    [PSCustomObject]@{
        PSTypeName  = 'Svrn7.SocietyMemberList'
        SocietyDid  = $Script:SocietyDriver.SocietyDid
        MemberCount = $dids.Count
        MemberDids  = [string[]]$dids
    }
}

function Test-Svrn7SocietyMember {
    <#
    .SYNOPSIS
        Tests whether a DID belongs to a registered citizen of this Society.

    .DESCRIPTION
        Wraps ISvrn7SocietyDriver.IsMemberAsync(). Resolves the input DID to its
        primary form before the membership check, so both primary and secondary
        DIDs are accepted. Returns $true if the citizen has a SocietyMembershipRecord
        linking them to this Society.

    .PARAMETER Did
        A citizen DID to test. Accepts primary or secondary DIDs. Supports pipeline.

    .INPUTS
        System.String — DID strings piped directly.

    .OUTPUTS
        PSCustomObject [Svrn7.MembershipResult]
            Did        [string]  The queried DID.
            SocietyDid [string]  This Society's DID.
            IsMember   [bool]    Whether the citizen is a registered member.

    .EXAMPLE
        PS> Test-Svrn7SocietyMember -Did 'did:sovronia:3J98...'

    .EXAMPLE
        PS> 'did:sovronia:abc', 'did:sovronia:xyz' |
                Test-Svrn7SocietyMember |
                Where-Object IsMember |
                Select-Object -ExpandProperty Did

    .NOTES
        C# API: ISvrn7SocietyDriver.IsMemberAsync(string)
        Spec:   draft-herman-web7-society-architecture-00 §4.3
    #>
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory, ValueFromPipeline)]
        [ValidateNotNullOrEmpty()]
        [string] $Did
    )

    process {
        Assert-SocietyDriver
        $isMember = $Script:SocietyDriver.IsMemberAsync($Did).GetAwaiter().GetResult()
        [PSCustomObject]@{
            PSTypeName = 'Svrn7.MembershipResult'
            Did        = $Did
            SocietyDid = $Script:SocietyDriver.SocietyDid
            IsMember   = $isMember
        }
    }
}

#endregion

###############################################################################
#region DID METHOD GOVERNANCE
# ISvrn7SocietyDriver.RegisterSocietyDidMethodAsync / DeregisterSocietyDidMethodAsync
#                      GetSocietyDidMethodsAsync
###############################################################################

function Register-Svrn7SocietyDidMethod {
    <#
    .SYNOPSIS
        Registers an additional DID method name for this Society (self-service).

    .DESCRIPTION
        Wraps ISvrn7SocietyDriver.RegisterSocietyDidMethodAsync(). Self-service —
        no Foundation signature required. The method name is automatically
        associated with this Society's DID (no -SocietyDid parameter needed,
        unlike Register-Svrn7DidMethod in the Federation module).

        The method name must:
          - Match [a-z0-9]+ (W3C DID Core §8.1)
          - Not currently be Active in the Federation registry
          - Not be in its dormancy period (within 30 days of deregistration)

    .PARAMETER MethodName
        DID method name to register. Must match [a-z0-9]+. Accepts pipeline input.

    .INPUTS
        System.String — method name strings piped directly.

    .OUTPUTS
        PSCustomObject [Svrn7.SocietyDidMethodRegistration]
            SocietyDid  [string]  This Society's DID.
            MethodName  [string]  The newly registered method name.
            Status      [string]  Always 'Active'.
            Success     [bool]    Always $true (throws on failure).

    .EXAMPLE
        PS> Register-Svrn7SocietyDidMethod -MethodName 'sovroniamed'

    .EXAMPLE
        PS> 'sovroniaedu', 'sovroniahealth' | Register-Svrn7SocietyDidMethod

    .NOTES
        C# API: ISvrn7SocietyDriver.RegisterSocietyDidMethodAsync(string)
        Spec:   draft-herman-did-method-governance-00 §6.2
    #>
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Low')]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory, ValueFromPipeline)]
        [ValidatePattern('^[a-z0-9]+$')]
        [string] $MethodName
    )

    process {
        Assert-SocietyDriver

        $societyDid = $Script:SocietyDriver.SocietyDid
        if (-not $PSCmdlet.ShouldProcess($societyDid, "Register DID method '$MethodName'")) { return }

        Write-Verbose "Registering method '$MethodName' for '$societyDid'..."

        $result = $Script:SocietyDriver.RegisterSocietyDidMethodAsync($MethodName).GetAwaiter().GetResult()
        Resolve-OperationResult -Result $result -Operation 'RegisterSocietyDidMethod' | Out-Null

        [PSCustomObject]@{
            PSTypeName = $Script:TypeDidMethodReg
            SocietyDid = $societyDid
            MethodName = $MethodName
            Status     = 'Active'
            Success    = $true
        }
    }
}

function Unregister-Svrn7SocietyDidMethod {
    <#
    .SYNOPSIS
        Deregisters an additional DID method name from this Society.

    .DESCRIPTION
        Wraps ISvrn7SocietyDriver.DeregisterSocietyDidMethodAsync(). The method
        name enters a dormancy period (default 30 days configured in Svrn7Options
        .DidMethodDormancyPeriod) during which it cannot be re-registered by any
        Society.

        The Society's primary method name (IsPrimary = $true) cannot be
        deregistered — attempting to do so throws PrimaryDidMethodException.

        All existing DIDs issued under the deregistered method name remain valid
        and resolvable (forward-only guarantee — draft-herman-did-method-governance-00
        §7.4). Only new DID issuance under the name is blocked.

    .PARAMETER MethodName
        The DID method name to deregister. Must not be the Society's primary
        method name.

    .INPUTS
        None. This cmdlet does not accept pipeline input.

    .OUTPUTS
        PSCustomObject [Svrn7.SocietyDidMethodDeregistration]
            SocietyDid  [string]  This Society's DID.
            MethodName  [string]  The deregistered method name.
            Status      [string]  Always 'Dormant'.
            Success     [bool]    Always $true (throws on failure).

    .EXAMPLE
        PS> Unregister-Svrn7SocietyDidMethod -MethodName 'sovroniaedu'

    .NOTES
        C# API: ISvrn7SocietyDriver.DeregisterSocietyDidMethodAsync(string)
        Spec:   draft-herman-did-method-governance-00 §7
    #>
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium')]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory)]
        [ValidatePattern('^[a-z0-9]+$')]
        [string] $MethodName
    )

    Assert-SocietyDriver

    $societyDid = $Script:SocietyDriver.SocietyDid
    if (-not $PSCmdlet.ShouldProcess($societyDid, "Deregister DID method '$MethodName'")) { return }

    Write-Verbose "Deregistering method '$MethodName' from '$societyDid'..."

    $result = $Script:SocietyDriver.DeregisterSocietyDidMethodAsync($MethodName).GetAwaiter().GetResult()
    Resolve-OperationResult -Result $result -Operation 'DeregisterSocietyDidMethod' | Out-Null

    [PSCustomObject]@{
        PSTypeName = $Script:TypeDidMethodDereg
        SocietyDid = $societyDid
        MethodName = $MethodName
        Status     = 'Dormant'
        Success    = $true
    }
}

function Get-Svrn7SocietyDidMethods {
    <#
    .SYNOPSIS
        Returns all DID method names registered to this Society.

    .DESCRIPTION
        Wraps ISvrn7SocietyDriver.GetSocietyDidMethodsAsync(). Returns both Active
        and Dormant method names owned by this Society. The primary method name
        (IsPrimary = $true) is always present in the list.

    .INPUTS
        None. This cmdlet does not accept pipeline input.

    .OUTPUTS
        PSCustomObject [Svrn7.SocietyDidMethodRecord] — one object per method name:
            MethodName    [string]   The DID method name.
            SocietyDid    [string]   This Society's DID.
            IsPrimary     [bool]     $true for the immutable primary method name.
            Status        [string]   'Active' or 'Dormant'.
            RegisteredAt  [datetime] UTC registration timestamp.
            DormantUntil  [datetime] Dormancy expiry (MinValue when Active).

    .EXAMPLE
        PS> Get-Svrn7SocietyDidMethods | Format-Table -AutoSize

    .EXAMPLE
        PS> Get-Svrn7SocietyDidMethods | Where-Object { $_.IsPrimary }

    .EXAMPLE
        PS> Get-Svrn7SocietyDidMethods | Where-Object Status -eq 'Active' |
                Select-Object -ExpandProperty MethodName

    .NOTES
        C# API: ISvrn7SocietyDriver.GetSocietyDidMethodsAsync()
        Spec:   draft-herman-did-method-governance-00 §5, §9.2
    #>
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param()

    Assert-SocietyDriver

    $records = $Script:SocietyDriver.GetSocietyDidMethodsAsync().GetAwaiter().GetResult()

    foreach ($r in $records) {
        [PSCustomObject]@{
            PSTypeName   = 'Svrn7.SocietyDidMethodRecord'
            MethodName   = $r.MethodName
            SocietyDid   = $r.SocietyDid
            IsPrimary    = $r.IsPrimary
            Status       = $r.Status.ToString()
            RegisteredAt = $r.RegisteredAt
            DormantUntil = if ($r.DormantUntil.HasValue) { $r.DormantUntil.Value } `
                           else { [datetime]::MinValue }
        }
    }
}

#endregion

###############################################################################
#region CROSS-SOCIETY VC RESOLUTION
# ISvrn7SocietyDriver.FindVcsBySubjectAcrossSocietiesAsync
###############################################################################

function Find-Svrn7VcsBySubject {
    <#
    .SYNOPSIS
        Resolves Verifiable Credentials for a subject DID across all known Societies
        via DIDComm fan-out.

    .DESCRIPTION
        Wraps ISvrn7SocietyDriver.FindVcsBySubjectAcrossSocietiesAsync(). Dispatches
        a VcResolveBySubjectRequest DIDComm message to every Society known to the
        Federation registry in parallel, collects responses within the timeout window,
        and returns a merged result set.

        Partial results are always returned — if some Societies do not respond within
        the timeout, the TimedOutSocieties list in the result identifies which ones
        did not contribute. Callers SHOULD inspect TimedOutSocieties to assess result
        completeness and decide whether to retry.

        This implements Principle P9 (Partial availability over total unavailability)
        from draft-herman-web7-society-architecture-00 §8.

    .PARAMETER SubjectDid
        The subject DID whose VCs to resolve. May be a primary or secondary DID.

    .PARAMETER TimeoutSeconds
        Maximum seconds to wait for each Society's response.
        Default: 10 seconds. Range: 1–300.

    .INPUTS
        None. This cmdlet does not accept pipeline input.

    .OUTPUTS
        PSCustomObject [Svrn7.CrossSocietyVcQueryResult]
            SubjectDid          [string]    The queried DID.
            Records             [object[]]  All VcRecord objects collected.
            RecordCount         [int]       Total number of VCs found.
            RespondedSocieties  [string[]]  DIDs of Societies that replied.
            TimedOutSocieties   [string[]]  DIDs of Societies that did not reply.
            IsComplete          [bool]      $true only if TimedOutSocieties is empty.

    .EXAMPLE
        PS> Find-Svrn7VcsBySubject -SubjectDid 'did:sovronia:3J98...'

    .EXAMPLE
        PS> $result = Find-Svrn7VcsBySubject -SubjectDid $did -TimeoutSeconds 20
        PS> if (-not $result.IsComplete) {
                Write-Warning "Partial result — $($result.TimedOutSocieties.Count) Society(ies) timed out"
            }
        PS> $result.Records | Format-Table VcId, Type, Status

    .NOTES
        C# API: ISvrn7SocietyDriver.FindVcsBySubjectAcrossSocietiesAsync(string, TimeSpan?, CancellationToken)
        Spec:   draft-herman-didcomm-svrn7-transfer-00 §11
                draft-herman-web7-society-architecture-00 §6.2 P9
    #>
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string] $SubjectDid,

        [Parameter()]
        [ValidateRange(1, 300)]
        [int] $TimeoutSeconds = 10
    )

    Assert-SocietyDriver

    $timeout = [TimeSpan]::FromSeconds($TimeoutSeconds)
    Write-Verbose "Fan-out VC query for '$SubjectDid' (timeout: ${TimeoutSeconds}s)..."

    $qr = $Script:SocietyDriver.FindVcsBySubjectAcrossSocietiesAsync(
        $SubjectDid, $timeout).GetAwaiter().GetResult()

    $responded = [string[]]($qr.RespondedSocieties ?? @())
    $timedOut  = [string[]]($qr.TimedOutSocieties  ?? @())

    Write-Verbose "VC query complete. Records: $($qr.Records.Count). Timed out: $($timedOut.Count)"

    [PSCustomObject]@{
        PSTypeName         = $Script:TypeVcQueryResult
        SubjectDid         = $SubjectDid
        Records            = $qr.Records
        RecordCount        = $qr.Records.Count
        RespondedSocieties = $responded
        TimedOutSocieties  = $timedOut
        IsComplete         = $timedOut.Count -eq 0
    }
}

#endregion

###############################################################################
#region MODULE CLEANUP
###############################################################################

$ExecutionContext.SessionState.Module.OnRemove = {
    if ($Script:SocietyDriver) {
        try { $Script:SocietyDriver.DisposeAsync().GetAwaiter().GetResult() } catch {}
        $Script:SocietyDriver = $null
    }
}

#endregion

Export-ModuleMember -Function @(
    'Connect-Svrn7Society'
    'Get-Svrn7OwnSociety'
    'Register-Svrn7CitizenInSociety'
    'Add-Svrn7CitizenDid'
    'Invoke-Svrn7IncomingTransfer'
    'Invoke-Svrn7ExternalTransfer'
    'Invoke-Svrn7FederationTransfer'
    'Get-Svrn7OverdraftStatus'
    'Get-Svrn7OverdraftRecord'
    'Get-Svrn7SocietyMembers'
    'Test-Svrn7SocietyMember'
    'Register-Svrn7SocietyDidMethod'
    'Unregister-Svrn7SocietyDidMethod'
    'Get-Svrn7SocietyDidMethods'
    'Find-Svrn7VcsBySubject'
)
