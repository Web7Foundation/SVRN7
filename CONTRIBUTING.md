# Contributing to SVRN7

Thank you for your interest in the SOVRONA (SVRN7) Shared Reserve Currency project.

## Methodology

SVRN7 is built using **Parchment Programming** (PPML — Parchment Programming Modeling
Language). All architectural decisions originate in the DSA 0.24 Epoch 0 diagram. Code
is derived from the diagram, not the other way around.

Before contributing code, read the diagram. If a proposed change cannot be traced to a
diagram element, it belongs in a diagram change proposal first.

## How to Contribute

### Reporting Bugs

Open a GitHub Issue with:
- The version (`v0.8.0`, commit SHA, or date)
- The affected project (`Svrn7.Core`, `Svrn7.Society`, etc.)
- Steps to reproduce
- Expected vs actual behaviour

### Proposing Changes

1. **Small fixes** (typos, doc corrections, test additions) — open a PR directly.
2. **Behavioural changes** — open an Issue first describing the change and its diagram
   traceability. Reference the DSA version and element instance label.
3. **New LOBEs** — provide a `.lobe.json` descriptor alongside `.psm1` and `.psd1`.
   See `lobes/Svrn7.Email.lobe.json` for the canonical format.
4. **Protocol additions** — new DIDComm `@type` URIs must follow the Locator DID URL
   convention: `did:drn:svrn7.net/protocols/{family}/{version}/{type}` for standard
   SVRN7 protocols, or `did:drn:{your-domain}/protocols/...` for third-party LOBEs.

### Pull Requests

- Target the `main` branch.
- All tests must pass locally: `dotnet test Web7-DSA.sln`
- New behaviour requires new tests.
- C# code must target .NET 8 (`net8.0`), use nullable reference types, and follow the
  existing naming conventions (see `README.md` Section 19).
- PowerShell modules must require version 7.0+ (`#Requires -Version 7.0`).
- Each new LOBE must ship `.psm1` + `.psd1` + `.lobe.json`.

## Architecture Constraints

The following are not negotiable in Epoch 0:

- All TDA-to-TDA communication is DIDComm V2, `SignThenEncrypt` mode.
- The TDA's single inbound surface is `POST /didcomm` (Kestrel, HTTP/2 + mTLS).
- No gRPC, no public REST API, no SMTP, no CalDAV.
- Protocol type URIs use `did:drn:svrn7.net/protocols/...` (Locator DID URLs).
- Cross-ecosystem interoperability with non-SVRN7 DIDComm agents is not a goal.
- All persistent state is LiteDB. No external database dependencies.

## Code Style

- C#: standard .NET naming (PascalCase types, camelCase locals, `_camelCase` fields).
- PowerShell: Verb-Noun cmdlet names, `Set-StrictMode -Version Latest`.
- JSON: 2-space indent.
- No tabs anywhere.

## IETF Internet-Drafts

The IETF drafts in `specs/` are submitted under `ipr="trust200902"`. Contributions to
draft text are subject to IETF Trust Legal Provisions. If you intend to contribute to
the draft text, you must agree to those terms before your contribution can be accepted.

## Licence

By contributing, you agree that your contributions will be licensed under the MIT
License as stated in the `LICENSE` file.
