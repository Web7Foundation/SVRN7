# CESR Key and Signature Encoding Profile for SOVRONA (SVRN7)
# draft-herman-cesr-svrn7-profile-00
# Author: M. Herman, Web 7.0 Foundation
# Published: April 2026

Internet-Draft: draft-herman-cesr-svrn7-profile-00
Published:      April 2026
Expires:        October 2026
Author:         M. Herman, Web 7.0 Foundation
Status:         Informational
Workgroup:      Web 7.0 Foundation Governance Council
Related:        draft-herman-svrn7-monetary-protocol-00
                draft-herman-web7-society-architecture-00
                draft-herman-didcomm-svrn7-transfer-00

---

## Abstract

This document specifies a Composable Event Streaming Representation (CESR) [CESR] profile
for the key and signature encoding conventions used in the SOVRONA (SVRN7) Web 7.0 Shared
Reserve Currency ecosystem. The profile identifies the specific CESR derivation codes used
for secp256k1 signatures, Ed25519 signatures, secp256k1 public keys, and Ed25519 public keys;
specifies how each encoded value appears in transfer request signatures, DIDComm message
signatures, DID identifiers, and Verifiable Credential proofs; and defines the canonical
encoding and decoding procedures. This profile enables interoperability between SVRN7
implementations and the broader KERI/CESR identity ecosystem.

---

## 1. Introduction

Composable Event Streaming Representation (CESR) [CESR] is a dual text-binary encoding
specification developed by the KERI community that enables compact, self-describing encoding
of cryptographic primitives — keys, signatures, digests, and identifiers — in both human-
readable text form and compact binary form. CESR primitives are self-framing: the first
character(s) of a CESR-encoded value identify its type and length, enabling stream parsing
without out-of-band framing.

The SVRN7 ecosystem uses a subset of CESR codes for encoding cryptographic values in:
- Transfer request signatures (secp256k1)
- DIDComm message signatures (Ed25519)
- DID identifiers (secp256k1 public key as Base58btc — see Section 4.3)
- Verifiable Credential proof values

This profile specifies exactly which CESR codes are used, in which contexts, and how
conformant implementations encode and decode them. Cross-referencing this profile enables
AI coding assistants and human developers to generate correct SVRN7 cryptographic values
without guessing at encoding details.

### 1.1 Relationship to CESR and KERI

CESR is the encoding layer of the Key Event Receipt Infrastructure (KERI) [KERI] identity
system. SVRN7 does not implement KERI's key event log or identifier rotation model, but adopts
CESR's encoding conventions for cryptographic values because:

1. CESR codes are self-describing — a value carrying its own type tag reduces parsing errors.
2. CESR is an active IETF work item [CESR], giving the encoding specification standards-body
   standing.
3. KERI and CESR are well-represented in AI training corpora for decentralised identity.
   Using CESR notation makes SVRN7 legible to AI systems already familiar with the KERI
   ecosystem.

### 1.2 Scope

This document specifies:
1. The CESR codes used in SVRN7.
2. The Base64URL encoding conventions for each code.
3. The contexts in which each code appears.
4. Encoding and decoding procedures.
5. Interoperability notes for KERI/CESR implementations.

---

## 2. Conventions and Definitions

The key words MUST, MUST NOT, REQUIRED, SHALL, SHALL NOT, SHOULD, SHOULD NOT, RECOMMENDED,
NOT RECOMMENDED, MAY, and OPTIONAL are to be interpreted as described in BCP 14 [RFC2119]
[RFC8174] when, and only when, they appear in all capitals as shown here.

All Base64URL values in this document use the URL-safe alphabet (RFC 4648 §5) without
padding characters.

---

## 3. Terminology

- **CESR**: Composable Event Streaming Representation. A dual text-binary encoding for
  cryptographic primitives [CESR].

- **Derivation Code**: The leading character(s) of a CESR-encoded value that identify its
  type and length. Also called a "lead code" or "selector" in CESR literature.

- **CESR Text Domain**: The human-readable Base64URL representation of a CESR primitive,
  consisting of a derivation code followed by the Base64URL-encoded bytes.

- **secp256k1**: The elliptic curve used for SVRN7 transfer signing and governance operations.
  Defined in [SECP256K1]. Also called K-256 or SECG secp256k1.

- **Ed25519**: The elliptic curve used for DIDComm messaging signatures. Defined in [RFC8032].

- **X25519**: The Diffie-Hellman function over Curve25519 used for DIDComm key agreement.
  Derived from Ed25519 keys via the birational map in [RFC7748].

- **Compact Signature**: A secp256k1 signature in the 64-byte format (r || s), without
  recovery byte.

- **Base58btc**: The Bitcoin-variant Base58 encoding alphabet (no 0, O, I, l characters).
  Used for encoding DID identifiers.

---

## 4. CESR Codes Used in SVRN7

### 4.1 Complete Code Table

| Code | Algorithm | Value length (bytes) | Encoded length | Context |
|------|-----------|---------------------|----------------|---------|
| `0B` | secp256k1 compact signature | 64 | 88 chars | Transfer request `Signature` field |
| `0B` | secp256k1 compact signature | 64 | 88 chars | STH `Signature` field (Merkle log) |
| `0B` | secp256k1 compact signature | 64 | 88 chars | Erasure commitment `Signature` |
| `0B` | secp256k1 compact signature | 64 | 88 chars | Governance operation `Signature` |
| `0D` | Ed25519 signature | 64 | 88 chars | DIDComm JWS `signature` value |
| `D11A`| secp256k1 compressed public key | 33 | 48 chars | DID Document `publicKeyMultibase` (see §4.3) |
| `1AAB`| Ed25519 public key | 32 | 44 chars | DID Document `publicKeyMultibase` (see §4.3) |

Note: SVRN7 uses `D11A` and `1AAB` codes for public keys in DID Documents only. In practice,
DID identifiers use Base58btc encoding (Section 4.3), which is not CESR. The CESR codes for
public keys appear only when a DID Document explicitly uses `publicKeyMultibase` format.

### 4.2 secp256k1 Signature Encoding (Code `0B`)

#### 4.2.1 Code Meaning

The two-character code `0B` in the CESR text domain identifies a secp256k1 compact signature:
- `0` — indicates a two-character lead code.
- `B` — the selector for secp256k1 compact signature (64 bytes).

#### 4.2.2 Encoding Procedure

Given a secp256k1 compact signature `sig-bytes` (64 bytes, r || s):

```
cesr-sig = "0B" + base64url-nopad(sig-bytes)
```

The base64url encoding of 64 bytes produces exactly 86 characters (ceiling(64 * 8 / 6) = 86,
no padding required since 64 is divisible by 3). Total encoded length: 88 characters.

Example:
```
sig-bytes = <64 random bytes for illustration>
cesr-sig  = "0BrjXfQ8A...qZ3E"    (88 characters total)
```

#### 4.2.3 Decoding Procedure

```
if value does not start with "0B":
    raise InvalidCesrCodeError("Expected secp256k1 signature code 0B")
sig-bytes = base64url-decode(value[2:])    // decode chars 3 onward
if len(sig-bytes) != 64:
    raise InvalidSignatureLengthError
```

#### 4.2.4 Signing Procedure

```
payload-bytes  = UTF-8(canonical-json)
hash           = SHA-256(payload-bytes)
sig-bytes      = secp256k1-sign-compact(hash, private-key-bytes)
// sig-bytes is the 64-byte compact (r || s) format — NO recovery byte
Signature      = "0B" + base64url-nopad(sig-bytes)
```

Implementations MUST use the compact 64-byte signature format. The 65-byte DER-encoded or
65-byte recoverable-with-recovery-byte formats MUST NOT be used.

#### 4.2.5 Verification Procedure

```
if Signature does not start with "0B":
    return SignatureVerificationError
sig-bytes     = base64url-decode(Signature[2:])
payload-bytes = UTF-8(canonical-json)
hash          = SHA-256(payload-bytes)
public-key    = retrieve-secp256k1-pubkey(payerDid)
result        = secp256k1-verify-compact(hash, sig-bytes, public-key)
return result
```

### 4.3 Ed25519 Signature Encoding (Code `0D`)

#### 4.3.1 Code Meaning

The two-character code `0D` in the CESR text domain identifies an Ed25519 signature:
- `0` — indicates a two-character lead code.
- `D` — the selector for Ed25519 signature (64 bytes).

#### 4.3.2 Encoding Procedure

Given an Ed25519 signature `sig-bytes` (64 bytes):

```
cesr-sig = "0D" + base64url-nopad(sig-bytes)
```

Total encoded length: 88 characters (same as secp256k1, same byte length).

#### 4.3.3 Context: DIDComm JWS

In DIDComm SignThenEncrypt messages [DRAFT-DIDCOMM-TRANSFER], the JWS `signature` value is
a Base64URL-encoded Ed25519 signature without CESR prefix. The CESR `0D` prefix applies only
when an Ed25519 signature appears as a standalone field in a SVRN7 data structure outside of
a JWS envelope.

In JWS context: `signature = base64url-nopad(ed25519-sig-bytes)` (no CESR prefix)
In standalone context: `signature = "0D" + base64url-nopad(ed25519-sig-bytes)`

### 4.4 Public Key Encoding in DID Documents

SVRN7 DID Documents MUST encode public keys using the W3C DID Core `publicKeyMultibase`
property with Base58btc encoding (multibase prefix `z`):

#### 4.4.1 secp256k1 Compressed Public Key (33 bytes)

```
publicKeyMultibase = "z" + base58btc(multicodec-secp256k1-prefix + pubkey-bytes)
```

where `multicodec-secp256k1-prefix = 0xe7 0x01` (varint encoding of multicodec code 0xe7).

This is NOT a CESR encoding. The multibase `z` prefix denotes Base58btc, not CESR text domain.

#### 4.4.2 Ed25519 Public Key (32 bytes)

```
publicKeyMultibase = "z" + base58btc(multicodec-ed25519-prefix + pubkey-bytes)
```

where `multicodec-ed25519-prefix = 0xed 0x01` (varint encoding of multicodec code 0xed).

Again, this is multibase Base58btc, not CESR.

#### 4.4.3 CESR Codes for publicKeyMultibase (alternative)

Implementations MAY alternatively use CESR-encoded public keys in `publicKeyMultibase` with
the multibase CESR prefix `u` (URL-safe Base64):

```
secp256k1: publicKeyMultibase = "u" + "D11A" + base64url-nopad(pubkey-bytes)
Ed25519:   publicKeyMultibase = "u" + "1AAB" + base64url-nopad(pubkey-bytes)
```

This alternative is provided for interoperability with KERI-native implementations that
prefer CESR over multibase Base58btc. SVRN7 reference implementations use Base58btc by
default.

### 4.5 DID Identifier Encoding

The method-specific identifier in a SVRN7 DID is the Base58btc encoding of the entity's
secp256k1 compressed public key (33 bytes), WITHOUT any CESR code or multicodec prefix:

```
identifier = base58btc(secp256k1-compressed-pubkey-bytes)
DID        = "did:" + method-name + ":" + identifier
```

Example:
```
pubkey-bytes = <33-byte compressed secp256k1 public key>
identifier   = "8bQk4rMaXPF..."    (Base58btc, ~44 characters)
DID          = "did:socalpha:8bQk4rMaXPF..."
```

This encoding is consistent with the `did:key` method convention [DID-KEY] for secp256k1
identifiers, without the multicodec prefix, since the method name already implies the key type.

---

## 5. CESR in Verifiable Credential Proofs

When SVRN7 Verifiable Credentials [DRAFT-MONETARY] include a CESR signature in the JWT proof,
the `Signature` field in the VC JWT carries the CESR-encoded secp256k1 signature:

```json
{
  "header": { "alg": "ES256K", "typ": "JWT" },
  "payload": {
    "iss": "<societyDid>",
    "sub": "<citizenDid>",
    "jti": "<vcId>",
    ...
    "vc": {
      "credentialSubject": {
        "verificationPublicKeyHex": "<issuer-secp256k1-pubkey-hex>"
      }
    }
  },
  "signature": "<base64url-nopad(secp256k1-compact-sig)>"
}
```

Note: JWT compact serialisation uses base64url without CESR prefix in the signature component.
The CESR `0B` prefix appears only in non-JWT standalone signature fields (e.g., the `Signature`
field in a `TransferRequest`). JWT uses the algorithm identifier (`"alg": "ES256K"`) in the
header to indicate secp256k1 rather than a CESR derivation code.

---

## 6. Summary: Where Each Code Appears

| CESR Code | Appears in | Field name |
|-----------|-----------|------------|
| `0B` | `TransferRequest` JSON | `Signature` |
| `0B` | `SupplyUpdateRequest` JSON | `Signature` |
| `0B` | `EpochAdvancementRequest` JSON | `Signature` |
| `0B` | `SignedTreeHead` JSON | `Signature` |
| `0B` | `ErasureCommitment` JSON | `Signature` |
| `0D` | DIDComm standalone Ed25519 fields | `signature` |
| Base58btc | DID identifier (not CESR) | `did:{method}:{identifier}` |
| Base58btc | `publicKeyMultibase` (default) | DID Document |
| `1AAB` (CESR alt.) | `publicKeyMultibase` (KERI-compat.) | DID Document |
| `D11A` (CESR alt.) | `publicKeyMultibase` (KERI-compat.) | DID Document |
| base64url (no prefix) | JWT `signature` component | VC JWT |

---

## 7. Interoperability with KERI/CESR Implementations

KERI implementations that support CESR natively may encounter SVRN7 signatures in two forms:

1. **CESR text domain**: `"0B<86-char-base64url>"` — directly parseable by any CESR-aware
   library as a secp256k1 signature.
2. **JWT compact signature**: `"<86-char-base64url>"` (no prefix) — the CESR prefix is
   absent; the signature type is conveyed by the JWT header `"alg": "ES256K"`.

KERI-aware implementations SHOULD handle both forms. SVRN7 implementations receiving a value
that is 86 characters of Base64URL (no `0B` prefix) in a context where a secp256k1 signature
is expected MAY interpret it as a compact secp256k1 signature without CESR framing.

SVRN7 does not implement KERI key event logs, identifier rotation, or threshold signatures.
CESR is adopted solely for its encoding conventions, not its key management model.

---

## 8. Encoding Validation Rules

Conformant implementations MUST enforce the following validation rules when parsing
CESR-encoded values in SVRN7 contexts:

| Rule | Check |
|------|-------|
| `0B` code integrity | Value starts with exactly "0B" followed by 86 Base64URL characters |
| `0D` code integrity | Value starts with exactly "0D" followed by 86 Base64URL characters |
| No padding | Base64URL encoded values MUST NOT contain `=` padding characters |
| URL-safe alphabet | Base64URL values MUST use `-` and `_` (not `+` and `/`) |
| secp256k1 sig length | Decoded bytes: exactly 64 bytes |
| Ed25519 sig length | Decoded bytes: exactly 64 bytes |
| secp256k1 pubkey length | Compressed: exactly 33 bytes; prefix byte 0x02 or 0x03 |
| Ed25519 pubkey length | Exactly 32 bytes |

---

## 9. Security Considerations

### 9.1 Derivation Code Stripping
The CESR derivation code is part of the signed value in SVRN7 (it appears inside the canonical
JSON that is hashed before signing). Any manipulation of the derivation code after signing will
produce a signature verification failure. Implementations MUST include the full CESR-prefixed
value in the canonical JSON, not just the raw bytes.

### 9.2 Signature Malleability
secp256k1 signatures have a known malleability property: for any valid (r, s) signature, the
value (r, n-s) is also a valid signature where n is the curve order. Implementations MUST
enforce low-S normalisation: `s ≤ n/2`. This prevents an attacker from creating an alternative
valid signature from an observed signature, which could affect idempotency checks based on
the TransferId (which is computed from the canonical JSON including the Signature field).

### 9.3 Key Type Confusion
The `0B` code (secp256k1) and `0D` code (Ed25519) have different key types and MUST NOT be
used interchangeably. Implementations MUST verify that the CESR derivation code matches the
expected key algorithm for the context before performing signature verification.

---

## 10. Privacy Considerations

CESR-encoded public keys and signatures do not in themselves constitute personal data. However,
in the SVRN7 context, a secp256k1 public key uniquely identifies a citizen or society — it is
the basis of the DID identifier. Therefore the same privacy considerations apply to
CESR-encoded keys as to DIDs in the SVRN7 ecosystem (see [WEB70-ARCH] Section 11).

---

## 11. IANA Considerations

This document has no IANA actions. The CESR derivation codes referenced in this document
(`0B`, `0D`, `D11A`, `1AAB`) are defined in [CESR] and this document does not create new codes.

---

## 12. References

### Normative
- [RFC2119] Bradner, S. Key words for use in RFCs. BCP 14, March 1997.
- [RFC8174] Leiba, B. Ambiguity of Uppercase vs Lowercase. May 2017.
- [RFC4648] Josefsson, S. The Base16, Base32, and Base64 Data Encodings. October 2006.
- [RFC7748] Langley, A. et al. Elliptic Curves for Diffie-Hellman Key Agreement. January 2016.
- [RFC8032] Josefsson, S. and I. Liusvaara. Ed25519: Edwards-Curve Digital Signature Algorithm. January 2017.
- [CESR] Smith, S. Composable Event Streaming Representation. draft-ssmith-cesr. IETF.
- [SECP256K1] Standards for Efficient Cryptography Group. SEC 2: Recommended Elliptic Curve Domain Parameters. Version 2.0, 2010.

### Informative
- [DRAFT-MONETARY] Herman, M. SOVRONA (SVRN7) Monetary Transfer Protocol. draft-herman-svrn7-monetary-protocol-00.
- [WEB70-ARCH] Herman, M. Web 7.0 Digital Society Architecture. draft-herman-web7-society-architecture-00.
- [DRAFT-DIDCOMM-TRANSFER] Herman, M. DIDComm Transfer Protocol for SVRN7. draft-herman-didcomm-svrn7-transfer-00.
- [KERI] Smith, S. Key Event Receipt Infrastructure. draft-ssmith-keri. IETF.
- [DID-KEY] Longley, P. et al. The did:key Method. W3C Community Group Report.
- [WEB70-IMPL] Herman, M. SOVRONA (SVRN7) .NET 8 Reference Implementation. https://github.com/web7foundation/svrn7.

---

## Author's Address

Michael Herman
Web 7.0 Foundation
Bindloss, Alberta, Canada
Email: mwherman@gmail.com
URI: https://hyperonomy.com/about/
