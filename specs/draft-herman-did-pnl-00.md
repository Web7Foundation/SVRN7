# Digital Agent Post-Nominal Letters (PNL) DID Method
# draft-herman-did-pnl-00

Internet-Draft: draft-herman-did-pnl-00
Published:      13 April 2026
Expires:        14 October 2026
Author:         M. Herman, Web 7.0 Foundation
Status:         Informational
Workgroup:      Decentralized Identifiers
Datatracker:    https://datatracker.ietf.org/doc/draft-herman-did-pnl/

---

## Abstract

This document specifies the `did:pnl` Decentralized Identifier (DID) method for Digital
Agent Post-Nominal Letters (PNL). Post-Nominal Letters are a machine-readable suffix
system that encodes structured, multi-dimensional role and capability metadata about
digital agents. The `did:pnl` method defines a DID method-specific identifier syntax
that encodes a PNL expression across seven orthogonal dimensions: Capability Class,
Domain Specialization, Authority/Permission Level, Trust/Verification Level, Operational
Role, Affiliation/Principal, and Reputation/Performance Tier. A `did:pnl` identifier
resolves to a DID Document containing the structured PNL metadata, cryptographically
verifiable claims, and optional service endpoints. The method is fully compatible with
the W3C DID Core specification [W3C.DID-CORE] and the W3C Verifiable Credentials Data
Model v2 [W3C.VC-DATA-MODEL-2].

---

## 1. Introduction

### 1.1 Background

Human professionals use post-nominal letters (e.g., "MD", "PhD", "CPA") to convey
credentials, specializations, and roles in a compact, standardized form. As digital
agents — including AI language models, orchestration systems, robotic process automation,
and simulation engines — become first-class participants in digital ecosystems, there is
a corresponding need for a machine-readable, structured, and cryptographically verifiable
mechanism for conveying equivalent metadata about agents.

The `did:pnl` method addresses this need by defining a DID method in which the
method-specific identifier IS the PNL expression. A `did:pnl` DID identifies not an
individual agent instance, but a **PNL Role Profile** — a named intersection of
capability, domain, authority, trust, operational role, affiliation, and reputation
characteristics that any conformant agent MAY claim through a Verifiable Credential.

### 1.2 Design Goals

The `did:pnl` method is designed to satisfy the following goals:

1. **Orthogonality**: Each of the seven PNL dimensions is independent. A token from one
   dimension conveys no information about any other dimension.

2. **Machine-Readability**: PNL expressions MUST be parseable by software without
   natural-language processing. Human-readable labels are secondary.

3. **Cryptographic Verifiability**: Claims over PNL dimensions MUST be expressible as
   W3C Verifiable Credentials issued by an authoritative party. Self-asserted claims are
   permitted but MUST be distinguishable from third-party-verified claims.

4. **Progressive Disclosure**: A PNL expression MAY include as few as one dimension
   token (Capability Class only) and grow to include all seven dimensions. Resolvers
   MUST handle partial PNL expressions.

5. **Controlled Vocabularies**: Each dimension has a normative registry of tokens (see
   Section 6). Extensions MUST use the `x-` prefix convention defined in Section 6.8.

6. **Composability**: `did:pnl` identifiers MAY appear in the `alsoKnownAs`, `service`,
   or Verifiable Credential subject fields of other DID Documents.

7. **DID Core Conformance**: Every `did:pnl` identifier MUST conform to W3C DID Core
   [W3C.DID-CORE] and be resolvable to a well-formed DID Document.

### 1.3 Relationship to Other DID Methods

A `did:pnl` DID identifies a **role profile**, not an agent instance. Individual agent
instances are identified by their own DIDs (e.g., `did:drn:agentY.alpha.svrn7.net`,
`did:key:z6Mk...`, `did:web:example.com:agents:agentY`). An agent instance CLAIMS
membership in a PNL Role Profile via a Verifiable Credential whose `credentialSubject`
references the agent's own DID, and whose `type` array includes `PnlRoleCredential`.
The `did:pnl` DID appears in the `credentialSubject.pnlProfileDid` field.

---

## 2. Conventions and Definitions

The key words MUST, MUST NOT, REQUIRED, SHALL, SHALL NOT, SHOULD, SHOULD NOT, RECOMMENDED,
NOT RECOMMENDED, MAY, and OPTIONAL are to be interpreted as described in BCP 14 [RFC2119]
[RFC8174] when, and only when, they appear in all capitals as shown here.

---

## 3. Terminology

- **Digital Agent**: Any autonomous or semi-autonomous software entity capable of
  perceiving its environment, reasoning, and taking actions. Includes AI language models,
  orchestration systems, robotic process automation bots, simulation engines, and
  agentic workflow coordinators.

- **Post-Nominal Letter (PNL)**: A structured token or set of tokens appended to an
  agent's identifier that conveys role and capability metadata in a compact, standardized
  form, analogous to post-nominal letters used by human professionals.

- **PNL Expression**: A structured string composed of one or more dimension tokens,
  separated by the `.` delimiter, encoding a specific combination of agent characteristics
  across the seven PNL dimensions (A through G).

- **PNL Role Profile**: The DID subject of a `did:pnl` identifier. Represents the
  abstract intersection of characteristics encoded in the PNL Expression. Agents claiming
  this profile receive a `PnlRoleCredential` whose subject includes the profile DID.

- **PNL Dimension**: One of seven independent axes of agent characterization:
  (A) Capability Class, (B) Domain Specialization, (C) Authority/Permission Level,
  (D) Trust/Verification Level, (E) Operational Role, (F) Affiliation/Principal,
  (G) Reputation/Performance Tier.

- **Dimension Token**: A short, uppercase alphanumeric string from a registered or
  extension vocabulary that identifies a specific value within a PNL Dimension.

- **PnlRoleCredential**: A W3C VC v2 Verifiable Credential whose `type` includes
  `PnlRoleCredential`, asserting that the credential subject (an agent instance) conforms
  to the PNL Role Profile identified by a `did:pnl` DID.

- **Capability Class**: Dimension A — the fundamental operational paradigm of the agent
  (e.g., LLM, autonomous agent, simulation).

- **Domain Specialization**: Dimension B — the subject-matter domain in which the agent
  operates (e.g., finance, medical, legal).

- **Authority Level**: Dimension C — the permission scope the agent is authorized to
  exercise (e.g., advisory only, full execution, payment authorization).

- **Trust Level**: Dimension D — the provenance and verification tier of the agent's
  identity and credentials (e.g., self-asserted, organization-verified, government-verified).

- **Operational Role**: Dimension E — the functional role the agent plays within a
  multi-agent system (e.g., broker, auditor, guard).

- **Affiliation**: Dimension F — the principal or organization on behalf of which the
  agent operates (e.g., a specific user, organization, or DAO).

- **Reputation Tier**: Dimension G — a rated measure of past performance, reliability,
  or trust accumulated over time.

- **DID**: A Decentralized Identifier as specified in W3C DID Core [W3C.DID-CORE].

- **DID Document**: A set of data describing the DID subject per W3C DID Core Section 5.

- **Resolver**: A software component that, given a DID, returns a DID Document.

---

## 4. Method Name

The method name is: `pnl`

A DID conforming to this specification MUST begin with the prefix `did:pnl:`.
Implementations MUST produce and compare method prefixes in lowercase. The method name
`pnl` is registered in the W3C DID Specification Registries per Section 16.

---

## 5. Method-Specific Identifier

### 5.1 Syntax

The method-specific identifier of a `did:pnl` DID is a PNL Expression — a dot-separated
sequence of dimension tokens representing a specific PNL Role Profile.

```abnf
did-pnl          = "did:pnl:" pnl-expression
pnl-expression   = cap-class-token *( "." dimension-token )
cap-class-token  = cap-class-value
cap-class-value  = "LLM" / "PLN" / "AUT" / "SIM" / "ORC" / ext-token
dimension-token  = domain-token / authority-token / trust-token /
                   role-token / affil-token / rep-token / ext-token
domain-token     = domain-value [ "-" domain-sub ]
domain-value     = "FIN" / "MED" / "LEG" / "DEV" / "OPS" / "SCN" / "EDU" / "GOV" / ext-token
domain-sub       = 1*( ALPHA / DIGIT )
authority-token  = "ADV" / "SIM" / "ACT" / "EXEC" / "PAYEXEC" / "SYSADMIN" / ext-token
trust-token      = "SELF" / "ORG" / "3PVER" / "GOVVER" / "VCL2" / "VCL3" / ext-token
role-token       = "BROKER" / "AGENT" / "AUDITOR" / "GUARD" / "NEGOTIATOR" / ext-token
affil-token      = "A" 1*( ALPHA / DIGIT / "-" )
rep-token        = "R" 1*DIGIT / "TRUSTHI" / "TRUSTLO" / "SLA" 1*( DIGIT / "." ) / ext-token
ext-token        = "X-" 1*( ALPHA / DIGIT / "-" )
```

Notes on syntax:

1. `cap-class-token` MUST be the first token in any PNL Expression. Dimension tokens
   after the first SHOULD appear in dimension order (B, C, D, E, F, G), but resolvers
   MUST NOT reject out-of-order expressions.

2. All tokens MUST be uppercase. Resolvers MUST normalize to uppercase before comparison.

3. The affiliation token is distinguished by the leading `A` followed by an alphanumeric
   principal identifier (e.g., `ASVRN7`, `AUSER`, `AORGACME`, `ADAO123`). Hyphens are
   permitted within the principal identifier segment.

4. The reputation token is distinguished by a leading `R` followed by a digit string
   (e.g., `R1` through `R5`), or by the named tokens `TRUSTHI`, `TRUSTLO`, or `SLA`
   followed by a numeric SLA percentage (e.g., `SLA999`).

5. Extension tokens MUST use the `X-` prefix. Implementations MUST silently ignore
   unrecognized extension tokens during resolution.

### 5.2 Normalization

Implementations MUST normalize a PNL Expression by:

1. Converting all characters to uppercase.
2. Removing any whitespace or separator characters other than `.`.
3. Removing duplicate tokens within the same dimension.
4. If two tokens from the same registered dimension are present, retaining the more
   specific one (e.g., `FIN-RISK` supersedes `FIN`).

Normalized PNL Expressions are used for identifier equality comparison.

### 5.3 Conformant Examples

```
did:pnl:LLM
did:pnl:AUT.FIN
did:pnl:AUT.FIN.PAYEXEC
did:pnl:AUT.FIN.PAYEXEC.3PVER
did:pnl:AUT.FIN.PAYEXEC.3PVER.R4
did:pnl:LLM.MED.ADV.SELF.AGENT.AUSER.R3
did:pnl:ORC.DEV.EXEC.ORG.GUARD.AORGACME.TRUSTHI
did:pnl:AUT.FIN-RISK.PAYEXEC.3PVER.BROKER.ASVRN7.R4
did:pnl:SIM.GOV.SIM.GOVVER.AUDITOR.AORGACME.SLA999
did:pnl:PLN.OPS.ACT.VCL3.NEGOTIATOR.ADAO123.R5
```

---

## 6. PNL Dimension Registries

### 6.1 Dimension A — Capability Class (REQUIRED)

The Capability Class token identifies the fundamental operational paradigm of the agent.
Exactly one Capability Class token MUST appear as the first token in any PNL Expression.

| Token   | Name                        | Description |
|---------|-----------------------------|-------------|
| `LLM`   | Large Language Model        | Agent is primarily a large language model; capabilities are driven by in-context generation and reasoning. |
| `PLN`   | Planner                     | Agent is specialized for planning, scheduling, and task decomposition; typically orchestrates other agents or tools. |
| `AUT`   | Autonomous Agent            | Agent operates autonomously with minimal human oversight; capable of multi-step reasoning and action chains. |
| `SIM`   | Simulation Engine           | Agent operates in a simulation or synthetic environment; outputs are hypothetical or predictive rather than real-world. |
| `ORC`   | Orchestrator                | Agent coordinates a network of sub-agents or services; primarily a workflow coordinator with delegation authority. |

### 6.2 Dimension B — Domain Specialization (OPTIONAL)

The Domain Specialization token identifies the subject-matter domain in which the agent
is designed or trained to operate. Zero or one Domain Specialization token SHOULD appear.

| Token   | Sub-token example | Name               | Description |
|---------|-------------------|--------------------|-------------|
| `FIN`   | `FIN-RISK`, `FIN-TRADE` | Financial Services | Agent is specialized for financial services operations. |
| `MED`   | `MED-DIAG`, `MED-PHARM` | Medical / Healthcare | Agent is specialized for medical or healthcare contexts. |
| `LEG`   | `LEG-CONTR`, `LEG-COMP` | Legal               | Agent is specialized for legal research, contracts, or compliance. |
| `DEV`   | `DEV-SEC`, `DEV-OPS`    | Software Development | Agent is specialized for software development or DevOps. |
| `OPS`   | `OPS-LOG`, `OPS-PROC`   | Operations          | Agent is specialized for operational or logistics tasks. |
| `SCN`   | `SCN-CLIM`, `SCN-MKT`   | Science             | Agent is specialized for scientific research or analysis. |
| `EDU`   | `EDU-K12`, `EDU-HE`     | Education           | Agent is specialized for educational contexts. |
| `GOV`   | `GOV-TAX`, `GOV-PROC`   | Government          | Agent is specialized for government or public sector operations. |

Sub-tokens extend the base domain using a hyphen followed by a registered sub-domain
label (e.g., `FIN-RISK` for financial risk analysis). Sub-domain registries are
maintained by the domain authority and referenced in the DID Document's `service`
endpoint of type `PnlDomainRegistry`.

### 6.3 Dimension C — Authority/Permission Level (OPTIONAL)

The Authority Level token encodes the maximum permission scope the agent is authorized
to exercise. Zero or one Authority Level token SHOULD appear.

| Token      | Name                  | Description |
|------------|-----------------------|-------------|
| `ADV`      | Advisory Only         | Agent may only produce advice, recommendations, or analysis. No write operations permitted. |
| `SIM`      | Simulation            | Agent may execute simulated operations. Results do not affect real-world state. |
| `ACT`      | Action                | Agent may take real-world actions with bounded scope (e.g., read/write within a defined context). |
| `EXEC`     | Full Execution        | Agent may execute complex multi-step operations with broad scope. |
| `PAYEXEC`  | Payment Execution     | Agent is authorized to initiate and execute payment transactions up to a configured limit. |
| `SYSADMIN` | System Administration | Agent is authorized to perform system-level administration, including configuration changes. |

### 6.4 Dimension D — Trust/Verification Level (OPTIONAL)

The Trust Level token describes how the agent's identity and credentials have been
verified. Zero or one Trust Level token SHOULD appear.

| Token     | Name                        | Description |
|-----------|-----------------------------|-------------|
| `SELF`    | Self-Asserted               | Agent's PNL claims are self-issued. No third-party verification. |
| `ORG`     | Organization-Verified       | Agent's claims have been verified by the agent's principal organization. |
| `3PVER`   | Third-Party Verified        | Agent's claims have been verified by an independent third-party auditor or certification body. |
| `GOVVER`  | Government-Verified         | Agent's claims have been verified by a government or regulatory authority. |
| `VCL2`    | VC Trust Level 2            | Agent holds Verifiable Credentials at NIST 800-63 IAL2 equivalent trust level. |
| `VCL3`    | VC Trust Level 3            | Agent holds Verifiable Credentials at NIST 800-63 IAL3 equivalent trust level. |

Trust Level tokens are ordered from lowest to highest assurance: `SELF` < `ORG` < `3PVER`
< `GOVVER` < `VCL2` < `VCL3`. Relying parties SHOULD define minimum acceptable trust
levels for their use cases.

### 6.5 Dimension E — Operational Role (OPTIONAL)

The Operational Role token identifies the functional role the agent plays in a multi-agent
system or workflow. Zero or one Operational Role token SHOULD appear.

| Token        | Name        | Description |
|--------------|-------------|-------------|
| `BROKER`     | Broker      | Agent matches or negotiates between two or more principals. |
| `AGENT`      | Agent       | Agent acts as a general-purpose agent on behalf of a principal. |
| `AUDITOR`    | Auditor     | Agent monitors, records, or evaluates actions of other agents for compliance or quality. |
| `GUARD`      | Guard       | Agent enforces access control, policy compliance, or safety constraints on other agents. |
| `NEGOTIATOR` | Negotiator  | Agent conducts structured negotiation on behalf of a principal. |

### 6.6 Dimension F — Affiliation/Principal (OPTIONAL)

The Affiliation token identifies the principal entity on whose behalf the agent operates.
Zero or one Affiliation token SHOULD appear. Affiliation tokens begin with `A` followed
by the principal identifier.

| Token pattern    | Name                   | Description |
|------------------|------------------------|-------------|
| `ASVRN7`         | SOVRONA Platform       | Agent operates on behalf of the SVRN7 Federation or Society. |
| `AUSER`          | Named User             | Agent operates on behalf of a registered citizen or user. |
| `AORG-{name}`    | Named Organization     | Agent operates on behalf of the named organization (e.g., `AORG-ACME`). |
| `ADAO-{id}`      | Named DAO              | Agent operates on behalf of the identified Decentralized Autonomous Organization. |
| `AFED-{id}`      | Named Federation       | Agent operates on behalf of the identified federation entity. |
| `APUB`           | Public / Unaffiliated  | Agent operates in the public interest with no specific principal affiliation. |

Affiliation tokens SHOULD be validated against the principal's own DID, referenced in
the `affiliationDid` property of the `PnlRoleCredential`.

### 6.7 Dimension G — Reputation/Performance Tier (OPTIONAL)

The Reputation Tier token encodes a rated measure of an agent's past performance,
reliability, or accumulated trust. Zero or one Reputation Tier token SHOULD appear.

| Token pattern   | Name                    | Description |
|-----------------|-------------------------|-------------|
| `R1`            | Reputation Tier 1       | Lowest performance/trust tier. New or unproven agent. |
| `R2`            | Reputation Tier 2       | Below-average performance history. |
| `R3`            | Reputation Tier 3       | Average performance; meets baseline requirements. |
| `R4`            | Reputation Tier 4       | Above-average performance; proven track record. |
| `R5`            | Reputation Tier 5       | Highest performance/trust tier. Elite standing. |
| `TRUSTHI`       | High Trust              | Qualitative high-trust designation from a trust framework. |
| `TRUSTLO`       | Low Trust               | Qualitative low-trust designation; restricted use contexts. |
| `SLA{value}`    | SLA Percentage          | Service Level Agreement rating as a percentage (e.g., `SLA999` = 99.9%). |

Reputation Tier tokens SHOULD be backed by a `PnlReputationCredential` issued by a
recognized reputation authority, referenced via a service endpoint of type
`PnlReputationService` in the DID Document.

### 6.8 Extension Tokens

Applications MAY define extension tokens using the `X-` prefix followed by an uppercase
alphanumeric label (e.g., `X-QUANTUM`, `X-EMBODIED`, `X-MULTIMODAL`). Extension tokens
MUST NOT collide with registered token names. Resolvers MUST NOT reject a PNL Expression
solely on the basis of unrecognized extension tokens; they MUST preserve extension tokens
in the resolved DID Document.

---

## 7. DID Document Structure

### 7.1 Minimum Conformant DID Document

A resolver MUST return at minimum the following DID Document for any syntactically valid
`did:pnl` identifier:

```json
{
  "@context": [
    "https://www.w3.org/ns/did/v1",
    "https://svrn7.net/ns/pnl/v1"
  ],
  "id": "did:pnl:AUT.FIN.PAYEXEC.3PVER.R4",
  "pnlExpression": "AUT.FIN.PAYEXEC.3PVER.R4",
  "pnlDimensions": {
    "capabilityClass": "AUT",
    "domainSpecialization": "FIN",
    "authorityLevel": "PAYEXEC",
    "trustLevel": "3PVER",
    "operationalRole": null,
    "affiliation": null,
    "reputationTier": "R4"
  }
}
```

### 7.2 Full DID Document with Verification Method and Service Endpoints

```json
{
  "@context": [
    "https://www.w3.org/ns/did/v1",
    "https://svrn7.net/ns/pnl/v1"
  ],
  "id": "did:pnl:AUT.FIN.PAYEXEC.3PVER.BROKER.ASVRN7.R4",
  "pnlExpression": "AUT.FIN.PAYEXEC.3PVER.BROKER.ASVRN7.R4",
  "pnlDimensions": {
    "capabilityClass": "AUT",
    "domainSpecialization": "FIN",
    "authorityLevel": "PAYEXEC",
    "trustLevel": "3PVER",
    "operationalRole": "BROKER",
    "affiliation": "ASVRN7",
    "reputationTier": "R4"
  },
  "verificationMethod": [
    {
      "id": "did:pnl:AUT.FIN.PAYEXEC.3PVER.BROKER.ASVRN7.R4#registry-key-1",
      "type": "Ed25519VerificationKey2020",
      "controller": "did:drn:svrn7.net",
      "publicKeyMultibase": "z6Mkf5rGMoatrSj1f4CyvuHBeXJELe9RPdzo2PKGNCKVtZxP"
    }
  ],
  "assertionMethod": [
    "did:pnl:AUT.FIN.PAYEXEC.3PVER.BROKER.ASVRN7.R4#registry-key-1"
  ],
  "service": [
    {
      "id": "did:pnl:AUT.FIN.PAYEXEC.3PVER.BROKER.ASVRN7.R4#pnl-registry",
      "type": "PnlRoleRegistry",
      "serviceEndpoint": "https://svrn7.net/pnl/registry/AUT.FIN.PAYEXEC.3PVER.BROKER.ASVRN7.R4"
    },
    {
      "id": "did:pnl:AUT.FIN.PAYEXEC.3PVER.BROKER.ASVRN7.R4#reputation",
      "type": "PnlReputationService",
      "serviceEndpoint": "https://svrn7.net/pnl/reputation/AUT.FIN.PAYEXEC.3PVER.BROKER.ASVRN7.R4"
    }
  ]
}
```

### 7.3 JSON-LD Context

Implementations SHOULD use the `did:pnl` context URI `https://svrn7.net/ns/pnl/v1` to
provide resolvable JSON-LD term definitions for `pnlExpression`, `pnlDimensions`, and
the service types `PnlRoleRegistry`, `PnlReputationService`, and `PnlDomainRegistry`.

### 7.4 Controller

The `controller` of a `did:pnl` DID Document is the entity that maintains the PNL Role
Registry for that role profile. By default, control is held by the PNL governing authority
(see Section 13). When an organization maintains a private PNL registry, `controller`
MUST reference a resolvable DID identifying that organization.

---

## 8. DID Resolution

### 8.1 Resolution Algorithm

Given a `did:pnl` identifier, a conformant resolver MUST:

1. Validate that the identifier matches the `did-pnl` ABNF production in Section 5.1.
   If validation fails, return `invalidDid` error.

2. Normalize the PNL Expression per Section 5.2.

3. Parse the normalized expression into dimension tokens, assigning each token to its
   registered dimension. Extension tokens (`X-` prefix) are collected into
   `pnlDimensions.extensions`.

4. Construct the minimum DID Document per Section 7.1.

5. If a PNL Role Registry service endpoint is configured and reachable, query it for an
   extended DID Document containing verification methods and additional service endpoints.
   If the registry returns a document, validate its `id` matches the normalized DID.
   Merge verification methods and service endpoints into the base document.

6. Return the assembled DID Document with `didDocumentMetadata` indicating resolution mode.

### 8.2 Resolution Modes

**Mode 1 — Stateless (REQUIRED)**
Resolver constructs the DID Document from the identifier string alone. No network access.
`didDocumentMetadata.resolutionMode` = `"stateless"`.

**Mode 2 — Registry-Assisted (RECOMMENDED)**
Resolver queries a PNL Role Registry for a fuller DID Document including verification
methods. `didDocumentMetadata.resolutionMode` = `"registry"`.

### 8.3 DID URL Dereferencing

Fragment-based DID URLs address verification method or service endpoint entries within
the resolved DID Document per W3C DID Core Section 3.2.

```
did:pnl:AUT.FIN.PAYEXEC.3PVER.R4#registry-key-1
```

Path-based DID URL dereferencing is NOT defined by this specification. Implementations
MAY define application-specific path conventions.

---

## 9. CRUD Operations

| Operation  | Status            | Notes |
|------------|-------------------|-------|
| Create     | Implicit          | No registration required for Mode 1. A PNL expression is valid as a DID the moment it conforms to the ABNF in Section 5.1. Registry entries are created by submitting a registration request to the PNL Role Registry. |
| Read       | REQUIRED          | Mode 1 stateless resolution MUST be supported by all conformant resolvers. Mode 2 registry-assisted resolution is RECOMMENDED. |
| Update     | Registry-Only     | The PNL expression in the method-specific identifier is immutable. Only the associated DID Document properties (verification methods, service endpoints) MAY be updated via the registry. |
| Deactivate | Registry-Only     | A PNL Role Profile MAY be deactivated in the registry. Stateless resolvers will continue to resolve the DID; registry-mode resolvers MUST return `deactivated: true` in `didDocumentMetadata`. |

---

## 10. PnlRoleCredential

### 10.1 Purpose

A `PnlRoleCredential` is a W3C VC v2 Verifiable Credential issued by a PNL authority
to an agent instance, asserting that the agent conforms to the characteristics described
by a specific `did:pnl` Role Profile.

### 10.2 Credential Schema

```json
{
  "@context": [
    "https://www.w3.org/2018/credentials/v1",
    "https://svrn7.net/ns/pnl/v1"
  ],
  "type": ["VerifiableCredential", "PnlRoleCredential"],
  "issuer": "did:drn:svrn7.net",
  "validFrom": "2026-04-13T00:00:00Z",
  "validUntil": "2027-04-13T00:00:00Z",
  "credentialSubject": {
    "id": "did:drn:agenty.alpha.svrn7.net",
    "pnlProfileDid": "did:pnl:AUT.FIN.PAYEXEC.3PVER.BROKER.ASVRN7.R4",
    "pnlExpression": "AUT.FIN.PAYEXEC.3PVER.BROKER.ASVRN7.R4",
    "capabilityClass": "AUT",
    "domainSpecialization": "FIN",
    "authorityLevel": "PAYEXEC",
    "trustLevel": "3PVER",
    "operationalRole": "BROKER",
    "affiliation": "ASVRN7",
    "affiliationDid": "did:drn:svrn7.net",
    "reputationTier": "R4"
  },
  "proof": {
    "type": "Ed25519Signature2020",
    "created": "2026-04-13T00:00:00Z",
    "verificationMethod": "did:drn:svrn7.net#key-1",
    "proofPurpose": "assertionMethod",
    "proofValue": "z58DAdFfa9..."
  }
}
```

### 10.3 Self-Asserted vs. Third-Party PnlRoleCredentials

When `trustLevel` is `SELF`, the `credentialSubject.id` and `issuer` MUST be the same
DID — i.e., the agent issues its own credential. Relying parties SHOULD treat
self-asserted `PnlRoleCredential` instances with reduced trust and MUST NOT grant
`PAYEXEC`, `EXEC`, or `SYSADMIN` authority to self-asserted claims.

When `trustLevel` is `ORG`, `3PVER`, `GOVVER`, `VCL2`, or `VCL3`, the `issuer` MUST
differ from `credentialSubject.id` and MUST be independently resolvable.

---

## 11. Verification of PNL Claims

### 11.1 Verification Algorithm

A relying party wishing to verify an agent's PNL claims MUST:

1. Obtain the agent's `PnlRoleCredential` (via presentation, DIDComm, or HTTP).
2. Resolve the `issuer` DID to obtain the issuer's DID Document and verification method.
3. Verify the credential proof per W3C VC Data Model v2 Section 5.
4. Verify `validFrom` <= current time <= `validUntil`.
5. Verify the `pnlProfileDid` resolves to a non-deactivated DID Document.
6. Verify the `pnlExpression` in the credential matches the normalized form of the
   `pnlProfileDid` method-specific identifier.
7. If `trustLevel` is not `SELF`, verify the `issuer` is an accredited PNL authority by
   checking the issuer's DID Document for a service endpoint of type `PnlAuthority`.
8. If `authorityLevel` includes `PAYEXEC`, `EXEC`, or `SYSADMIN`, verify `trustLevel`
   is at minimum `ORG`. Relying parties MAY require `3PVER` or higher for sensitive operations.

### 11.2 Revocation

`PnlRoleCredential` revocation MUST be supported via one of:
- W3C VC Data Model v2 Status List 2021 (`credentialStatus` field).
- DIDComm v2 revocation notification to the holder's inbox.

Resolvers SHOULD check revocation status before accepting PNL claims in high-authority
contexts (`PAYEXEC`, `EXEC`, `SYSADMIN`).

---

## 12. DID Method Governance

### 12.1 PNL Governing Authority

The initial PNL Governing Authority is the Web 7.0 Foundation. The Governing Authority:

- Maintains the normative token registries in Section 6.
- Issues accreditation to recognized PNL authorities via `PnlAuthority` credentials.
- Operates the root PNL Role Registry at `https://svrn7.net/pnl/registry/`.
- Publishes additions and deprecations to token registries with at least 90 days notice.

### 12.2 Delegated Authority

Organizations MAY operate delegated PNL registries for their own namespaces. Delegated
authority is granted by the Governing Authority via a `PnlDelegatedAuthority` credential
whose subject is the delegated authority's DID. A delegated registry MAY define additional
domain sub-tokens (Dimension B) within their registered domain allocation.

### 12.3 Token Deprecation

When a registered token is deprecated:
1. The Governing Authority updates the token registry to mark the token as `deprecated`.
2. Existing `PnlRoleCredential` instances referencing the deprecated token remain valid
   until their `validUntil` date.
3. Resolvers MUST include a `pnlMetadata.deprecatedTokens` array in the resolved DID
   Document for any deprecated tokens present in the expression.

---

## 13. Interoperability

### 13.1 With did:drn (Web 7.0)

In the Web 7.0 / SVRN7 ecosystem, agent instances are identified by `did:drn` Identity
DIDs. The `did:pnl` DID appears as the value of `pnlProfileDid` in the agent's
`PnlRoleCredential`. The `did:drn` agent DID Document MAY include a service endpoint of
type `PnlPresentation` pointing to the agent's VP endpoint.

### 13.2 With W3C Verifiable Presentations

Agents SHOULD bundle `PnlRoleCredential` instances into a Verifiable Presentation for
selective disclosure. Holders MAY omit specific dimension values from the presentation
when the relying party does not require them (progressive disclosure per Section 1.2).

### 13.3 With DIDComm v2

In DIDComm v2 message headers, agents SHOULD include their `pnlProfileDid` in the
`from_prior` or application-level `pnl` extension header. Switchboards MAY use PNL
dimensions for message routing, capability negotiation, and access control.

### 13.4 With OpenID for Verifiable Credentials (OID4VC)

PNL issuers MAY expose `PnlRoleCredential` issuance endpoints via OID4VC Credential
Issuance (draft 13+). The credential type `PnlRoleCredential` MUST be registered in the
issuer's credential metadata.

---

## 14. Privacy Considerations

### 14.1 Correlation Risks

A `did:pnl` Role Profile DID is PUBLIC by design. Any party observing the DID can
immediately recover all PNL dimension tokens. Agents operating in privacy-sensitive
contexts SHOULD:

- Use Verifiable Presentations with selective disclosure to reveal only the minimum
  required dimension tokens.
- Use pairwise `did:peer` identifiers for agent-to-agent communication, referencing
  the `did:pnl` profile only via presentation.

### 14.2 Affiliation Exposure

The Affiliation token (Dimension F) MAY reveal the principal organization or user
on whose behalf the agent operates. Agents SHOULD omit the Affiliation token from the
`did:pnl` expression in contexts where affiliation must remain confidential, and instead
provide it only via selective disclosure VP.

### 14.3 Reputation Tracking

Reputation Tier tokens (Dimension G) encode performance history. Chains of `PnlRoleCredential`
instances with the same `credentialSubject.id` and varying Reputation Tier values constitute
a longitudinal performance record. Agents concerned about longitudinal tracking SHOULD use
different identifier DIDs across reputation-tier transitions.

### 14.4 Minimisation Guidance

Issuers SHOULD NOT include dimension tokens in a `PnlRoleCredential` that are not
operationally necessary for the intended use case. Relying parties MUST NOT require
more PNL dimensions than are necessary for their authorization decision.

---

## 15. Security Considerations

### 15.1 Self-Asserted Claim Risk

Self-asserted `PnlRoleCredential` instances (Trust Level `SELF`) provide no meaningful
security assurance beyond proof-of-key-control. Relying parties MUST NOT grant
operational authority (Dimension C tokens `EXEC`, `PAYEXEC`, `SYSADMIN`) based solely
on self-asserted claims.

### 15.2 Authority Escalation

Implementations MUST NOT infer authority from the presence of a dimension token alone.
The `authorityLevel` token encodes the CLAIMED maximum authority. The actual granted
authority in any deployment is determined by the relying party's access control policy
applied AFTER credential verification per Section 11.1.

### 15.3 Token Spoofing

The `did:pnl` method-specific identifier is syntactically unconstrained beyond ABNF
compliance. Any party can construct a `did:pnl` DID containing any token combination.
Relying parties MUST verify `PnlRoleCredential` instances rather than accepting PNL
expressions from untrusted sources as authoritative.

### 15.4 Registry Availability

Mode 2 resolution depends on registry availability. Implementations MUST implement
Mode 1 fallback when the registry is unavailable. Registries MUST be protected with
TLS 1.3 [RFC8446] and SHOULD be deployed with redundancy.

### 15.5 Credential Expiry Enforcement

Relying parties MUST reject expired `PnlRoleCredential` instances. A reasonable
maximum validity period for high-authority credentials (`PAYEXEC`, `EXEC`, `SYSADMIN`)
is 90 days. Credentials asserting `SELF` trust level SHOULD NOT exceed 30 days validity.

### 15.6 DIDComm Transport Security

When PNL claims are conveyed via DIDComm v2, messages MUST be packed using
SignThenEncrypt mode to provide both authenticity (non-repudiation) and confidentiality.
Authcrypt-only packing MUST NOT be used for messages conveying high-authority PNL claims.

---

## 16. IANA Considerations

Requests registration of DID method name `pnl` in the W3C DID Specification Registries.

| Field        | Value               |
|--------------|---------------------|
| Method Name  | `pnl`               |
| Status       | provisional         |
| Specification| This document       |
| Contact      | See Author's Address|

No IANA registry actions are required beyond the W3C DID method registration. Future
revisions of this specification MAY request an IANA Media Type registration for the
`application/pnl+json` type to convey PNL credential bundles.

---

## Appendix A: Complete PNL Composition Example

**Scenario**: AgentY is an autonomous financial broker operating on behalf of the SVRN7
platform, authorized to execute payments, third-party verified, with Reputation Tier 4.

**PNL Expression**: `AUT.FIN.PAYEXEC.3PVER.BROKER.ASVRN7.R4`

| Dimension | Token      | Meaning |
|-----------|------------|---------|
| A         | `AUT`      | Autonomous Agent |
| B         | `FIN`      | Financial Services domain |
| C         | `PAYEXEC`  | Authorized for payment execution |
| D         | `3PVER`    | Third-party verified identity |
| E         | `BROKER`   | Acts as a broker between principals |
| F         | `ASVRN7`   | Affiliated with SVRN7 platform |
| G         | `R4`       | Reputation Tier 4 (above average) |

**`did:pnl` DID**: `did:pnl:AUT.FIN.PAYEXEC.3PVER.BROKER.ASVRN7.R4`

**Agent instance DID**: `did:drn:agenty.alpha.svrn7.net`

**PnlRoleCredential** (abbreviated):
```json
{
  "type": ["VerifiableCredential", "PnlRoleCredential"],
  "issuer": "did:drn:svrn7.net",
  "credentialSubject": {
    "id": "did:drn:agenty.alpha.svrn7.net",
    "pnlProfileDid": "did:pnl:AUT.FIN.PAYEXEC.3PVER.BROKER.ASVRN7.R4"
  }
}
```

**Human-readable notation** (informative only): `AgentY, AUT, FIN, PAY-EXEC, 3P-VER, REP-4`

---

## Appendix B: Dimension Token Quick Reference

```
A (Capability):   LLM  PLN  AUT  SIM  ORC
B (Domain):       FIN  MED  LEG  DEV  OPS  SCN  EDU  GOV  [+ sub-tokens]
C (Authority):    ADV  SIM  ACT  EXEC  PAYEXEC  SYSADMIN
D (Trust):        SELF  ORG  3PVER  GOVVER  VCL2  VCL3
E (Role):         BROKER  AGENT  AUDITOR  GUARD  NEGOTIATOR
F (Affiliation):  ASVRN7  AUSER  AORG-{name}  ADAO-{id}  AFED-{id}  APUB
G (Reputation):   R1  R2  R3  R4  R5  TRUSTHI  TRUSTLO  SLA{value}
Extensions:       X-{label}
```

---

## References

### Normative

- [RFC2119] Bradner, S. Key words for use in RFCs. BCP 14, March 1997.
- [RFC5234] Crocker, D. Augmented BNF. STD 68, January 2008.
- [RFC8174] Leiba, B. Ambiguity of Uppercase vs Lowercase in RFC 2119. May 2017.
- [RFC8446] Rescorla, E. TLS 1.3. August 2018.
- [W3C.DID-CORE] Sporny et al. DIDs v1.0. W3C Recommendation, July 2022.
- [W3C.DID-RESOLUTION] Sabadello, M. DID Resolution v0.3. W3C Working Group Note, 2023.
- [W3C.VC-DATA-MODEL-2] Sporny et al. VC Data Model v2.0. W3C Candidate Recommendation, 2024.

### Informative

- [W3C.DID-SPEC-REGISTRIES] Sporny, Steele. DID Specification Registries. 2023.
- [NIST.SP.800-63A] Grassi et al. Digital Identity Guidelines: Enrollment and Identity
  Proofing. NIST Special Publication 800-63A, 2017.
- [DIDComm-V2] Hardman et al. DIDComm Messaging v2. Decentralized Identity Foundation, 2022.
- [did-drn] Herman, M. Decentralized Resource Name (DRN) DID Method.
  draft-herman-did-w3c-drn-00. Web 7.0 Foundation, March 2026.
- [PNL-BLOG] Herman, M. Digital Agents: What Are Possible Post-Nominal Letters Strategies
  for Identifying Different Kinds or Roles for Digital Agents?
  https://hyperonomy.com/2026/04/13/digital-agents-what-are-possible-post-nominal-letters-strategies-for-identifying-different-kinds-or-roles-for-digital-agents/
  April 2026.
- [WEB70-SOCIETY] Herman, M. Web 7.0 Society Architecture.
  draft-herman-web7-society-architecture-00. Web 7.0 Foundation, 2026.

---

## Author's Address

Michael Herman
Web 7.0 Foundation
Bindloss, Alberta, Canada
Email: mwherman@gmail.com
URI: https://hyperonomy.com/about/
