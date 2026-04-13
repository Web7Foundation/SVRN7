# Parchment Programming Modeling Language (PPML)
# draft-herman-parchment-programming-00
# Author: M. Herman, Web 7.0 Foundation
# Published: April 2026

Internet-Draft: draft-herman-parchment-programming-00
Published:      April 2026
Expires:        October 2026
Author:         M. Herman, Web 7.0 Foundation
Status:         Informational
Workgroup:      Web 7.0 Foundation Governance Council
Related:        draft-herman-web7-society-architecture-00
                draft-herman-svrn7-ai-legibility-00
                draft-herman-didcomm-svrn7-transfer-00

---

## Abstract

This document specifies the Parchment Programming Modeling Language (PPML) — a software development
discipline in which a formal annotated architecture diagram is the primary specification
and the single source of truth for a software system. Code, documentation, configuration,
and generated tests are derived from the diagram; the diagram is not derived from the code.
Parchment Programming inverts the conventional workflow in which architecture diagrams are
retrospective illustrations of existing code. Instead, the diagram is written first, reviewed
first, and stabilised first. Implementation begins only after the diagram is complete and
constitutes the normative architecture record for the lifetime of the system.

PPML defines a diagram grammar (visual element types and their semantic meanings),
a set of derivation rules (how each diagram element maps to one or more software artefacts), a
tractability invariant (every artefact must trace to a diagram element), and a change process
(diagrams are the change record; code changes without a corresponding diagram change are
considered undocumented and non-conformant). The Web 7.0 Decentralized System Architecture
(DSA) Trusted Digital Assistant (TDA) is used throughout as a running normative example.

---

## 1. Introduction

Software architecture diagrams have historically occupied an unstable position in the software
development lifecycle. At the beginning of a project they are aspirational sketches. Midway
through they become optimistic overviews. At the end they are either abandoned or retroactively
updated to match whatever was actually built. In all three phases they are secondary: the code
is the truth; the diagram is an approximation.

Parchment Programming rejects this relationship. In Parchment Programming:

1. The diagram is the truth.
2. The code is a derivation of the diagram.
3. A diagram element without a code artefact is an implementation gap.
4. A code artefact without a diagram element is an undocumented deviation.

The name "Parchment Programming" draws on the pre-printing-press tradition in which a scribe
produced a master document on parchment — durable, authoritative, and the canonical reference
from which all copies were made. In Parchment Programming the architecture diagram is that
master document: durable (it persists across the full system lifetime), authoritative (it
constrains implementation decisions), and the source from which all downstream artefacts —
code, tests, documentation, and configuration — are copied or derived.

PPML is specifically designed for the era of AI-assisted software development. When
a developer instructs an AI coding assistant to "implement component X", the assistant must be
able to resolve "what is X?" from an unambiguous specification. A well-formed Parchment
Programming diagram, with a formal Legend and precise element semantics, constitutes exactly
such a specification. This document specifies PPML in sufficient detail that a
conformant diagram can serve as a primary input to an AI code generator.

### 1.1 Motivation

The conventional software development workflow places diagrams downstream of analysis and
upstream of implementation, but treats them as optional and non-normative. This produces four
recurring failure modes:

**Diagram drift**: As code evolves, diagrams are not updated. After the first release, the
diagram describes a system that no longer exists.

**Ambiguous diagrams**: Diagrams use inconsistent visual vocabularies — boxes and arrows without
formally defined meanings. Two developers reading the same diagram produce different
implementations.

**Untraced artefacts**: Code is written for components that do not appear in any diagram.
Architectural decisions are undocumented. The system accumulates undocumented complexity.

**AI hallucination under-specification**: AI coding assistants asked to implement a component
from an ambiguous or informal specification hallucinate plausible-sounding but incorrect code.
The absence of a formal, machine-readable diagram contributes directly to this failure mode.

Parchment Programming addresses all four failure modes through the diagram-first discipline,
the formal Legend, the tractability invariant, and the change process.

### 1.2 Relationship to Existing Methodologies

Parchment Programming is compatible with and complements:

- **Model-Driven Engineering (MDE)**: MDE generates code from formal models. Parchment
  Programming is a lightweight, diagram-centric variant that does not require a formal
  modelling language (UML, SysML) but imposes a formal Legend on any visual notation.

- **Architecture Decision Records (ADRs)**: ADRs record individual decisions. Parchment
  Programming records the entire architecture as a single authoritative artefact and treats
  ADRs as the change log for that artefact.

- **Test-Driven Development (TDD)**: TDD specifies behaviour through tests before writing
  implementation code. Parchment Programming specifies structure through diagrams before
  writing either tests or implementation code. The two methodologies compose: PP produces the
  structure; TDD verifies the behaviour.

- **Domain-Driven Design (DDD)**: DDD aligns software models with business domain concepts.
  Parchment Programming is agnostic to the domain model but provides the visual specification
  medium in which domain models can be expressed.

### 1.3 Scope

This document specifies:

1. The Parchment Programming Modeling Language (PPML) definition and core principles (PP-1 through PP-8).
2. The Parchment Diagram Grammar: element types, the Legend, and annotation conventions.
3. The Diagram-to-Artefact Derivation Rules.
4. The Tractability Invariant and conformance requirements.
5. The Diagram Change Process.
6. The AI Code Generation Contract.
7. The Web 7.0 DSA TDA diagram as a normative running example.
8. Conformance criteria for implementations and diagrams.

This document does not specify:

- Any particular drawing tool or file format for diagrams.
- Any particular programming language, framework, or runtime environment.
- The content of any specific Web 7.0 / SVRN7 architectural component (see related
  specifications in the References section).

---

## 2. Conventions and Definitions

The key words MUST, MUST NOT, REQUIRED, SHALL, SHALL NOT, SHOULD, SHOULD NOT, RECOMMENDED,
NOT RECOMMENDED, MAY, and OPTIONAL are to be interpreted as described in BCP 14 [RFC2119]
[RFC8174] when, and only when, they appear in all capitals as shown here.

---

## 3. Terminology

- **Parchment Diagram**: An architecture diagram that conforms to the Parchment Programming
  Modeling Language (PPML). A conformant diagram carries a formal Legend, uses consistent
  element types, and constitutes the normative specification for the system it depicts.

- **Legend**: A box within the Parchment Diagram that formally defines every visual element
  type used in the diagram. The Legend is mandatory in a conformant Parchment Diagram. A
  diagram without a Legend is not a Parchment Diagram.

- **Element Type**: A named, visually distinct category of diagram component defined in the
  Legend. Each element type has a formal semantic meaning that maps to one or more software
  artefact categories. Examples: Protocol, Host, LOBE, Runspace, Data Storage.

- **Element Instance**: A specific box, arrow, label, or Data Storage database in the diagram that is an
  instance of a defined element type. Every element instance MUST be traceable to exactly one
  element type in the Legend.

- **Derivation Rule**: A normative mapping from an element type (or element instance) to one
  or more software artefact categories.

- **Artefact**: A software artefact derived from a diagram element. Examples: a C# class, a
  PowerShell module, a configuration file, a database schema, an IETF Internet-Draft.

- **Tractability Invariant**: The requirement that (a) every element instance in a Parchment
  Diagram has at least one corresponding artefact, and (b) every artefact in the implementation
  has at least one corresponding element instance.

- **Implementation Gap**: An element instance in the diagram for which no artefact yet exists.
  Tracked in the Gap Register (Section 7.3) and MUST be resolved in a future sprint.

- **Undocumented Deviation**: An artefact in the implementation for which no element instance
  exists. Non-conformant.

- **Parchment Epoch**: A named version of the Parchment Diagram corresponding to a major
  architectural state. Within an epoch the diagram is stable; changes within an epoch produce
  sub-versions (e.g., DSA 0.16, DSA 0.19, DSA 0.24 within Epoch 0).

- **AI Code Generation Contract**: A set of requirements on the Parchment Diagram that make it
  machine-readable by an AI code generator. Specified in Section 8.

---

## 4. Core Principles

Parchment Programming is defined by eight principles. A conformant PPML implementation MUST adhere
to all eight. Principles are identified by the prefix PP-N.

### PP-1: Diagram Primacy

The Parchment Diagram is the primary specification for the system. It is not a summary of the
code, not a high-level overview, and not a communication aid. It is the source of truth from
which all other artefacts are derived. In any conflict between the diagram and the code, the
diagram is correct and the code MUST be updated to conform.

Corollary: When a new requirement arrives, the diagram is updated first. Implementation begins
only after the diagram update is reviewed and accepted.

### PP-2: Legend Formalism

Every Parchment Diagram MUST include a Legend that defines all visual element types used in
the diagram. The Legend MUST specify, for each element type: a unique name, a visual
description, a semantic definition, and a derivation class.

A diagram that uses a visual element not defined in its Legend is non-conformant.

### PP-3: Element Instance Unambiguity

Every element instance in the diagram MUST be an unambiguous instance of exactly one element
type defined in the Legend. Two element instances that use the same visual style MUST belong
to the same element type. A diagram in which the same box style is used to mean "a service"
in one place and "a database" in another is non-conformant.

### PP-4: Tractability

Every element instance in a conformant Parchment Diagram MUST be associated with at least one
implementation artefact or an entry in the Gap Register. Every implementation artefact MUST be
associated with at least one element instance. Tractability verification is a release gate.

### PP-5: Change Record

Changes to the system MUST be recorded as changes to the Parchment Diagram first. A code
change not preceded by a diagram change (or accompanied by justification that no diagram change
is required, e.g., for bug fixes within an existing element instance) is non-conformant.

### PP-6: Epoch Stability

Within a Parchment Epoch, the element types defined in the Legend MUST NOT change. New element
instances MAY be added; element instances MAY be renamed; element instances MUST NOT change
element type within an epoch. Epoch transitions require a Legend review.

### PP-7: AI Legibility

A conformant Parchment Diagram MUST be AI-legible. Specifically, the diagram, when described
in structured prose, MUST be sufficient for an AI code generator to produce a correct, runnable
artefact for any element instance, given only the Legend definition, the element instance label,
and the derivation rules.

### PP-8: Living Specification

A Parchment Diagram is a living document for the lifetime of the system it describes. It
evolves with the system, subject to the change process in Section 9. Archival versions are
retained to support audit and regulatory requirements.

---

## 5. The Parchment Diagram Grammar

### 5.1 Required Diagram Elements

A conformant Parchment Diagram MUST contain:

1. **Title block**: System name, version identifier, and Parchment Epoch designation.
   Example: "Web 7.0 Decentralized System Architecture (DSA) 0.19 Epoch 0".

2. **Subtitle**: A one-line characterisation of the architectural quality attributes targeted
   by the design. Example: "Secure, Trusted, DID-native, DIDComm-native Web 7.0 DIDLibOS".

3. **Legend box**: Defines all visual element types (see Section 5.2).

4. **Element instances**: The diagram content — all named boxes, arrows, Data Storage databases,
   and labels that constitute the system description.

5. **Copyright and licence notice**: Copyright holder, year, and licence.

### 5.2 The Legend

The Legend MUST appear within the diagram boundary. It MUST define every visual element type
used in the diagram. The Legend box itself is a diagram element; its visual style MUST be
distinct from all element types it defines.

For each element type, the Legend entry MUST specify:

- **Name**: A unique noun or noun phrase, consistent with element instance labels.
- **Visual specification**: Border style, fill colour, text colour, and shape, sufficient
  to reproduce the element type unambiguously.
- **Semantic class**: The architectural role of element instances of this type.
- **Derivation rule**: The software artefact category produced from element instances of
  this type (Section 6.2).

The Legend MUST NOT define element types that do not appear in the diagram.

Starting with DSA 0.24, the Legend is formally labelled the **PPML Legend** — the Parchment
Programming Modeling Language Legend — making explicit that the visual grammar is a named
modeling language, not an informal annotation convention.

#### 5.2.1 PPML Legend 0.25 — Formal Specification

The PPML Legend 0.25 defines eleven element types. It is the normative visual grammar for
the Web 7.0 DSA 0.24 Epoch 0 diagram and for any conformant diagram that adopts the same
element vocabulary. The Legend appears in the lower-left corner of the diagram, inside a
light-grey rounded-rectangle boundary box labelled "PPML Legend 0.25" in bold sans-serif.

**Scope note**: Eight of the eleven element types (Protocol, Network, LOBE, Device, Data
Storage, Data Access, Host, Conditional Components Criteria) are general-purpose and
applicable to any software architecture. Three element types (Runspace Pool, Switchboard,
PowerShell Runspace) are specific to the Web 7.0 / SVRN7 TDA architecture. Adopters of
PPML for other technology stacks MAY substitute equivalent element types for these three,
provided the Legend is updated accordingly and the derivation rules are adjusted to match
the target technology.

---

**Element Type 1: Protocol**

```
Visual:     Vertical rotated rectangle with rounded corners.
            Border:     purple / violet (#7B2D8B or equivalent).
            Fill:       purple / violet, same hue.
            Text:       white, bold, rotated 90° counter-clockwise.
            Shape:      tall narrow pill, vertically oriented.
```

- **Semantic class**: A communication protocol instance — an inbound service endpoint, an
  outbound client call, or a protocol specification that governs message exchange.
- **Derivation rule**: One inbound Endpoint (e.g., HTTP route, DIDComm handler) or one
  outbound Client call, plus one Protocol specification document (IETF Internet-Draft,
  OpenAPI spec, or equivalent).
- **Web 7.0 example instances**: "DIDComm V2 Messaging", "HTTP Listener/Sender (HTTPClient)".

---

**Element Type 2: Network**

```
Visual:     Vertical rotated rectangle with rounded corners.
            Border:     dark gold / amber (#B8860B or equivalent).
            Fill:       yellow / gold gradient.
            Text:       dark, bold, rotated 90° counter-clockwise.
            Shape:      tall narrow bar, vertically oriented (narrower than Protocol).
```

- **Semantic class**: A transport rail — a physical or logical network over which protocol
  traffic flows. Not an active component; represents the medium, not the endpoint.
- **Derivation rule**: One transport configuration section (e.g., TLS configuration,
  network binding, DNS record). No code artefact is derived; Network elements inform
  configuration and deployment artefacts.
- **Web 7.0 example instances**: "Internet/LAN/P2P", "Internet" (peer mesh side).

---

**Element Type 3: LOBE**

```
Visual:     Rounded rectangle (landscape orientation).
            Border:     medium blue (#2196F3 or equivalent), 2px solid.
            Fill:       white or very light blue.
            Text:       dark blue, bold, centred.
            Inner icon: Small cyan rounded rectangle (tablet silhouette) centred,
                        representing a cognitive capability module.
```

- **Semantic class**: A cognitive capability module — a self-contained unit of domain
  logic implemented as a PowerShell module (.psm1) or equivalent. LOBEs expose named
  cmdlets consumed by agent runspace pipelines.
- **Derivation rule**: One PowerShell module file (.psm1 or equivalent), one manifest
  (.psd1), one LOBE descriptor (.lobe.json), and a set of exported cmdlets. Each cmdlet
  is an independently invocable pipeline element.
- **Web 7.0 example instances**: "UX LOBE", "SVRN7 LOBE", "Svrn7.Email", "Svrn7.Onboarding".
- **LOBE descriptor note**: Each LOBE MUST ship a .lobe.json descriptor declaring its
  protocol URI registrations, cmdlet schemas, and AI legibility metadata. See Section 16.

---

**Element Type 4: Device**

```
Visual:     Rounded rectangle (landscape, smaller than LOBE).
            Border:     medium blue (#2196F3 or equivalent), same hue as LOBE border.
            Fill:       white or very light blue (same as LOBE fill).
            Text:       dark blue, bold, centred.
            Shape:      compact outline rounded rectangle, appears inside or
                        adjacent to a LOBE element. Outline style — not solid fill.
```

- **Semantic class**: A physical or logical UX device — a platform-specific user-facing
  endpoint (CLI, mobile, browser, voice interface). Not a compute node; represents the
  interface surface of a Device element type.
- **Derivation rule**: One platform-specific UX module or adapter. In Web 7.0, typically
  a CLI handler, browser extension, or mobile app interface shim.
- **Web 7.0 example instances**: "CLI TDA Interface", tablet icon inside UX LOBE.

---

**Element Type 5: Data Storage**

```
Visual:     Classic database cylinder (top-cap ellipse + rectangular body).
            Border:     dark navy blue (#1A237E or equivalent).
            Fill:       dark navy blue, solid.
            Text:       white, bold, centred.
            Shape:      standard cylinder icon, vertically compact.
```

- **Semantic class**: A persistent data store — a durable database or file-system store
  that survives process restart. In the Web 7.0 / SVRN7 implementation, all Data Storage
  instances are LiteDB embedded databases.
- **Derivation rule**: One LiteDB context class (or equivalent store context), one or more
  collection definitions, one store interface (IXxxStore), and one store implementation.
  The database file path is a required configuration parameter.
- **Web 7.0 example instances**: "Long-Term Message Memory (LiteDB)" → svrn7-inbox.db,
  "DID Doc Registry (LiteDB)" → svrn7-dids.db,
  "VC Doc Registry (LiteDB)" → svrn7-vcs.db,
  "Schema Registry (LiteDB)" [Conditional: Society TDA Only] → svrn7-schemas.db.

---

**Element Type 6: Data Access**

```
Visual:     Rectangle (landscape, no rounded corners or minor rounding).
            Border:     dark navy blue (#1A237E or equivalent), same as Data Storage.
            Fill:       dark navy blue, solid.
            Text:       white, bold, centred.
            Shape:      flat rectangle, visually paired with a Data Storage cylinder.
```

- **Semantic class**: A resolver or cache abstraction — a software interface that mediates
  access to a Data Storage element or an external data source. Data Access elements are
  the read path; Data Storage elements are the write path.
- **Derivation rule**: One C# interface (IXxxResolver or IXxxRegistry) and one or more
  implementations. May include an IMemoryCache registration as the hot-path read layer.
- **Web 7.0 example instances**: "DID Doc Resolver" → IDidDocumentResolver,
  "VC Doc Resolver" → IVcDocumentResolver,
  "Schema Resolver" [Conditional: Society TDA Only] → ISchemaResolver,
  "IMemoryCache" → IMemoryCache DI registration.

---

**Element Type 7: Runspace Pool**

```
Visual:     Large rounded rectangle (landscape or portrait).
            Border:     warm beige / khaki (#A0937A or equivalent), 2–3px solid.
            Fill:       light beige (#F5F0E8 or equivalent).
            Text:       dark brown, bold, centred or top-aligned.
            Shape:      outer container — contains PowerShell Runspace elements.
```

- **Semantic class** *(Web 7.0 / SVRN7 specific)*: A PowerShell RunspacePool — the
  managed pool of PowerShell runspaces from which agent runspaces are allocated.
  Represents both the pool infrastructure and the shared InitialSessionState.
- **Derivation rule**: One RunspacePoolManager class, one InitialSessionState construction
  method, one lobes.config.json (or equivalent), and one configuration entry specifying
  minimum and maximum runspace counts.
- **Adopter note**: For non-PowerShell architectures, substitute the equivalent pool or
  thread-pool manager element type, updating the name and derivation rule accordingly.
- **Web 7.0 example instances**: "PowerShell Runspace Pool" → RunspacePoolManager.cs.

---

**Element Type 8: Switchboard**

```
Visual:     Rounded rectangle (landscape, prominent).
            Border:     orange / amber (#FF9800 or equivalent), 2–3px solid.
            Fill:       light orange (#FFF3E0 or equivalent).
            Text:       dark orange or black, bold, centred.
            Shape:      wide landscape rectangle, typically spanning the full
                        width of its containing Runspace Pool or Host.
```

- **Semantic class** *(Web 7.0 / SVRN7 specific)*: A DIDComm message router — the
  component that receives inbound messages from the Protocol layer, applies epoch gating
  and idempotency checks, and dispatches each message to the appropriate LOBE cmdlet
  pipeline via the protocol registry.
- **Derivation rule**: One router class (DIDCommMessageSwitchboard or equivalent), one
  protocol registry (ConcurrentDictionary keyed by @type URI), and one outbound delivery
  queue. The Switchboard is the sole reader of the Long-Term Message Memory Data Storage.
- **Adopter note**: For non-DIDComm architectures, substitute a message broker, event bus,
  or router component, updating the name and derivation rule accordingly.
- **Web 7.0 example instances**: "DIDComm Message Switchboard" → DIDCommMessageSwitchboard.cs.

---

**Element Type 9: Host**

```
Visual:     Large rounded rectangle (landscape, outermost container).
            Border:     green (#4CAF50 or equivalent), 2–3px solid.
            Fill:       light green (#F1F8E9 or equivalent).
            Text:       dark green, bold, top-left or centred.
            Shape:      the outermost named container in the diagram; contains
                        all other active-component element types.
```

- **Semantic class**: A process container — the OS process boundary of the deployable
  system unit. In the Web 7.0 / SVRN7 implementation, the Host is a .NET 8 console
  application using Generic Host with Kestrel HTTP/2 + mTLS.
- **Derivation rule**: Exactly one OS process entry point (Program.cs or equivalent),
  one DI container registration (IServiceCollection), and one hosted service registration
  per active background component.
- **Web 7.0 example instances**: "Citizen/Society TDA (Host)" → Program.cs,
  "Citizen TDA (x5, VTC7 mesh)" → same software deployed N times as peer TDAs.

---

**Element Type 10: PowerShell Runspace**

```
Visual:     Rounded rectangle (landscape, contained within Runspace Pool).
            Border:     crimson / deep red (#C62828 or equivalent), 2–3px solid.
            Fill:       light pink (#FFEBEE or equivalent).
            Text:       dark red or black, bold, centred.
            Shape:      named agent slot within the Runspace Pool container.
```

- **Semantic class** *(Web 7.0 / SVRN7 specific)*: A named agent runspace — a specific
  PowerShell runspace allocated from the pool and bound to a particular agent script and
  LOBE pipeline role. Each PowerShell Runspace element instance corresponds to one agent
  script that owns a specific message-handling responsibility.
- **Derivation rule**: One agent script (.ps1), one routing registration in the Switchboard
  protocol registry (associating one or more @type URI patterns with the agent's entry-point
  cmdlet), and one JIT or eager LOBE import specification.
- **Adopter note**: For non-PowerShell architectures, substitute the equivalent worker,
  actor, or coroutine element type.
- **Web 7.0 example instances**: "Agent 1 Runspace" → Agent1-Coordinator.ps1,
  "Agent 2 — Onboarding" → Agent2-Onboarding.ps1,
  "Agent N — Invoicing" → AgentN-Invoicing.ps1.

---

**Element Type 11: Conditional Components Criteria**

```
Visual:     Large rounded rectangle with dashed border.
            Border:     medium blue, dashed line style (dash-dash-gap pattern).
            Fill:       very light blue (#E3F2FD or equivalent) or transparent.
            Text:       blue or dark, bold, centred. The label IS the condition name.
            Shape:      a grouping container that encloses other element instances.
                        The dashed border distinguishes it from all solid-border types.
```

- **Semantic class**: A conditional component group — a set of element instances that are
  present in a deployment only when a named condition is satisfied. The condition is
  stated in the element's label. The Conditional Components Criteria type is the PPML
  mechanism for optional, scenario-specific, or epoch-gated component groupings.
- **Derivation rule**: Each enclosed element instance derives its artefact per its own
  element type. All enclosed artefacts MUST be conditionally instantiated — present only
  when the condition label is satisfied. The condition label MUST be registered in the
  Gap Register and MUST be documented in the implementation as a named guard.
- **Condition label examples**:
  - "Society TDA Only" — enclosed components present only in Society TDA deployments.
  - "Epoch 1+" — enclosed components active only from Epoch 1 onwards.
  - "domain: health" — enclosed components included only in health-domain deployments.
- **Web 7.0 example instance**: The "Society TDA Only" group enclosing Schema Registry
  (LiteDB), Schema Resolver, DID Doc Registry (LiteDB), DID Doc Resolver, VC Doc Registry
  (LiteDB), and VC Doc Resolver.

---

#### 5.2.2 PPML Legend 0.25 — Summary Table

The following table provides a compact reference for all eleven PPML Legend 0.25 element
types, ordered by position in the canonical Legend diagram (upper-left to lower-right).

| # | Element Type              | Border colour        | Fill colour          | Semantic class                  | Web 7.0/SVRN7 specific? |
|---|---------------------------|----------------------|----------------------|---------------------------------|------------------------|
| 1 | Protocol                  | Purple / violet      | Purple / violet      | Communication protocol          | No                     |
| 2 | Network                   | Dark gold / amber    | Yellow / gold        | Transport rail                  | No                     |
| 3 | LOBE                      | Medium blue          | White / light blue   | Cognitive capability module     | No                     |
| 4 | Device                    | Medium blue (outline)| White / light blue   | UX device / interface surface   | No                     |
| 5 | Data Storage              | Dark navy blue       | Dark navy blue       | Persistent data store           | No                     |
| 6 | Data Access               | Dark navy blue       | Dark navy blue       | Resolver / cache abstraction    | No                     |
| 7 | Runspace Pool             | Warm beige / khaki   | Light beige          | PS RunspacePool container       | Yes (PS-specific)      |
| 8 | Switchboard               | Orange / amber       | Light orange         | DIDComm message router          | Yes (DIDComm-specific) |
| 9 | Host                      | Green                | Light green          | Process container               | No                     |
|10 | PowerShell Runspace       | Crimson / deep red   | Light pink           | Named agent runspace            | Yes (PS-specific)      |
|11 | Conditional Components    | Medium blue, dashed  | Very light blue      | Conditional component group     | No                     |
|   | Criteria                  | border               |                      |                                 |                        |

#### 5.2.3 Adopting PPML Legend 0.25 in Other Contexts

PPML Legend 0.25 is defined in the context of the Web 7.0 / SVRN7 TDA architecture. Adopters
applying PPML to other technology stacks MUST treat elements 1–6 and 9–11 as general-purpose
and portable. Elements 7 (Runspace Pool), 8 (Switchboard), and 10 (PowerShell Runspace) are
Web 7.0 / SVRN7 specific and SHOULD be replaced with technology-appropriate equivalents:

| PPML Legend 0.25 element | General architectural concept | Alternative examples                  |
|--------------------------|-------------------------------|---------------------------------------|
| Runspace Pool            | Worker pool / thread pool     | ThreadPoolExecutor, Akka ActorSystem  |
| Switchboard              | Message router / event bus    | MediatR, Kafka consumer group, NATS   |
| PowerShell Runspace      | Worker / actor / coroutine    | Akka Actor, Go goroutine, Node worker |

When substituting, the adopter MUST update the Legend to use the new element type name,
visual specification, and derivation rule. The PPML Legend box MUST be relabelled to reflect
the diagram version (e.g., "PPML Legend 1.0" for a first non-Web7 adoption).

### 5.3 Element Instance Requirements

Each element instance in a conformant Parchment Diagram MUST carry:

- **Label**: A unique name within the diagram, used to trace the element to artefacts.
- **Element type membership**: Visual membership in exactly one Legend-defined element type.
- **Connectivity**: For active-component element types, at least one connection (arrow or
  containment relationship) to at least one other element instance.

### 5.4 Arrows and Relationships

A conformant diagram MUST assign a consistent visual vocabulary to arrow styles:

- **Solid unidirectional arrow**: A directed data flow or control dependency.
- **Bidirectional arrow**: A symmetric relationship (e.g., cache <-> backing store).
- **Containment**: An element drawn inside another indicates lifecycle ownership by the outer.

Arrowhead styles, colours, and labels MAY further distinguish relationship types, but MUST be
defined in the Legend.

### 5.5 Zones and Layers

A Parchment Diagram SHOULD organise element instances into spatial zones corresponding to
architectural tiers, processing stages, or trust boundaries. Zone boundaries SHOULD be visually
distinct and labelled.

Zone organisation provides the primary reading order for the diagram and directly informs the
layered structure of the implementation (Section 6.3).

#### 5.5.1 Web 7.0 DSA TDA Example: Zone Organisation

The DSA 0.24 Epoch 0 diagram is organised into seven spatial zones reading left to right:

1. **Command Line/Touch Interfaces (CLTIs)**: Human-facing entry points. Element type: Device.
2. **Internet/LAN/P2P**: Transport rail. Element type: Network.
3. **LOBEs**: Cognitive capability layer. Element type: LOBE (UX LOBE, Onboard LOBE,
   Invoicing LOBE, "..." slot). In DSA 0.24, SVRN7 LOBE moves inside Agent 1 Runspace.
4. **Citizen/Society TDA (Host)**: Primary subject. Element types: Host (outer), Runspace Pool
   (inner), PowerShell Runspace (agent slots), Switchboard.
5. **DIDComm V2 Messaging / HTTP Listener**: Gateway. Element type: Protocol (two instances).
6. **Internet**: Transport cloud. Element type: Network (implied).
7. **Verifiable Trust Circles (VTC7)**: Federated peer mesh. Multiple Host instances.

This zone structure directly generates the layered implementation specification:

- Layer 0: Host process (.NET console app, Generic Host)
- Layer 1: Transport (Kestrel HTTP/2 + mTLS, POST /didcomm, HttpClient)
- Layer 2: DIDComm Pack/Unpack boundary (DIDCommPackingService)
- Layer 3: LOBEs (PowerShell modules: Svrn7.Federation.psm1, Svrn7.Society.psm1, ...)
- Layer 4: Runspace Pool and agents (RunspacePoolManager, agent scripts, Switchboard)
- Layer 5: Storage (Data Storage databases: svrn7.db, svrn7-dids.db, svrn7-vcs.db, svrn7-inbox.db)

---

## 6. Derivation Rules

Derivation rules are normative mappings from Parchment Diagram element types to software
artefact categories. For each element type defined in the Legend, there MUST be at least one
derivation rule. This section specifies a standard set of derivation rule categories and the
derivation rules for the Web 7.0 DSA TDA diagram as a normative example.

### 6.1 Derivation Rule Categories

| Category      | Examples                                                        |
|---------------|-----------------------------------------------------------------|
| Process       | OS process, containerised workload, console application         |
| Service       | BackgroundService, HostedService, OS service                    |
| Interface     | C# interface, TypeScript interface, Go interface                |
| Class         | C# class, Python class, Java class                              |
| Module        | PowerShell .psm1, Python package, Node.js module                |
| Endpoint      | HTTP route, gRPC method, WebSocket handler                      |
| Configuration | JSON config file, YAML manifest, environment variable           |
| Store         | LiteDB database, SQL table, file system path                    |
| Cache         | IMemoryCache, Redis, in-process dictionary                      |
| Protocol      | DIDComm protocol URI, REST path, gRPC service definition        |
| Test Suite    | xUnit project, Pester script, Jest test suite                   |
| Documentation | IETF Internet-Draft, OpenAPI specification, README              |

### 6.2 Standard Derivation Rules

The following rules are defined as standard. A diagram using these element type names MUST
apply these derivation rules unless the Legend explicitly overrides them.

For the full visual specification of each element type see Section 5.2.1. The table below
cross-references the Legend element type number from Section 5.2.2.

| # | Element Type              | Derivation Rule                                                                          | Artefact categories             |
|---|---------------------------|------------------------------------------------------------------------------------------|---------------------------------|
| 1 | Protocol                  | One inbound Endpoint or outbound Client call, plus one Protocol specification document.  | Endpoint, Client, Documentation |
| 2 | Network                   | One transport configuration section. No code artefact derived.                          | Configuration                   |
| 3 | LOBE                      | One .psm1 module, one .psd1 manifest, one .lobe.json descriptor, exported cmdlets.      | Module, Configuration           |
| 4 | Device                    | One platform-specific UX module or adapter.                                              | Module                          |
| 5 | Data Storage              | One LiteDB context class, one or more collection definitions, one IXxxStore interface.  | Store, Interface, Class         |
| 6 | Data Access               | One IXxxResolver or IXxxRegistry interface, one or more implementations.                | Interface, Class, Cache         |
| 7 | Runspace Pool             | One RunspacePoolManager class, one InitialSessionState builder, one config entry.       | Class, Configuration            |
| 8 | Switchboard               | One router class, one protocol registry (ConcurrentDictionary), one outbound queue.     | Class, Service                  |
| 9 | Host                      | One OS process entry point, one DI container, one hosted service per background component.| Process, Service, Configuration |
|10 | PowerShell Runspace       | One agent script (.ps1), one protocol registry entry in the Switchboard.                | Module, Configuration           |
|11 | Conditional Components    | Each enclosed element derives per its own type. All artefacts MUST be conditionally     | (meta-rule — see below)         |
|   | Criteria                  | instantiated. Condition label MUST be registered in the Gap Register.                   |                                 |

**Conditional Components Criteria — expanded rule**: The Conditional Components Criteria
element type is a grouping meta-rule, not an artefact-producing element type in its own
right. Its derivation rule is a modifier applied to every enclosed element instance:

1. Each enclosed element instance derives its artefact as if the Conditional Components
   wrapper were not present.
2. Every derived artefact MUST include a named instantiation guard — a compile-time,
   configuration-time, or DI-time condition that prevents instantiation when the named
   condition is not satisfied.
3. The condition label (e.g., "Society TDA Only") MUST appear as a comment or attribute
   in the instantiation guard, and MUST be registered in the Gap Register with its
   definition and the list of enclosed element instances it covers.

### 6.3 Layered Derivation

For diagrams organised in spatial zones, derivation proceeds layer by layer. The derivation of
a layer MUST be complete before derivation of the next inner layer begins. This constraint
ensures that outer-layer interfaces are defined before inner-layer implementations are specified.

Layered derivation order for the DSA TDA:

1. **Layer 0 — Host**: Derive the Process (console app entry point) and DI container.
2. **Layer 1 — Transport**: Derive the Kestrel endpoint and HttpClient outbound sender.
3. **Layer 2 — Protocol**: Derive the IDIDCommService interface and DIDCommPackingService.
4. **Layer 3 — LOBEs**: Derive each PowerShell module and its exported cmdlets.
5. **Layer 4 — Runspace Pool and Agents**: Derive RunspacePoolManager and agent scripts.
6. **Layer 5 — Storage**: Derive Data Storage databases (LiteDB context classes), collection schemas, and store interfaces.

### 6.4 Derivation of Connections

- **Directed arrow A -> B**: The artefact derived from A holds a reference to (or calls) the
  artefact derived from B. In a DI system, A declares a constructor parameter of the type
  derived from B.
- **Bidirectional arrow A <-> B**: Both artefacts hold references to each other, or a shared
  interface is declared and both implement it.
- **Containment (B inside A)**: Lifecycle ownership. A creates, starts, and disposes B.

#### 6.4.1 Web 7.0 DSA TDA Example: Connection Derivations

- HTTP Listener/Sender (Protocol) -> Switchboard (Switchboard): KestrelListenerService calls
  DIDCommMessageSwitchboard.Enqueue() on each inbound message.
- Switchboard inside Agent 1 (PowerShell Runspace) inside Runspace Pool inside TDA (Host):
  Host owns RunspacePool; RunspacePool owns Agent 1; Agent 1 owns Switchboard loop.
- IMemoryCache (Data Access) <-> Long-Term Message Memory (Data Storage): IMemoryCache.Get()
  on cache miss triggers LiteDB query; IMemoryCache.Set() on cache population.

---

## 7. Tractability

Tractability is the property that every diagram element instance has a corresponding artefact
and every artefact has a corresponding diagram element instance.

### 7.1 Forward Tractability

**Definition**: For every element instance E in the Parchment Diagram, there exists at least
one artefact A such that A was derived from E according to the applicable derivation rule.

**Verification**: A Tractability Matrix lists each element instance alongside its derived
artefacts. The matrix is produced at each release and MUST be reviewed before release approval.

**Gap handling**: If element instance E has no corresponding artefact, E appears in the Gap
Register (Section 7.3). The Gap Register entry MUST document the reason and target sprint.

### 7.2 Backward Tractability

**Definition**: For every artefact A in the implementation, there exists at least one element
instance E such that A was derived from E.

**Verification**: Each pull request introducing a new artefact MUST include a statement
identifying the element instance from which the artefact is derived. A pull request introducing
an artefact with no diagram element MUST include a justification (bug fix, test helper, build
script) or MUST be preceded by a diagram update.

**Undocumented deviation**: An artefact with no diagram element and no justification is an
undocumented deviation. Undocumented deviations are non-conformant and MUST NOT be merged.

### 7.3 Gap Register

The Gap Register records all known gaps (element instances with no artefact). Each entry MUST
contain:

- **Element Instance Label**: The exact label from the diagram.
- **Element Type**: The Legend entry for this element instance.
- **Derivation Target**: The artefact category that is missing.
- **Priority**: Critical / High / Medium / Low.
- **Target Sprint**: The planned sprint for resolution.
- **Notes**: Any design decision recorded for the implementation.

The Gap Register is not a defect tracker. It is a planning instrument that makes the distance
between diagram and implementation explicitly visible and measurable.

#### 7.3.1 Web 7.0 DSA TDA Example: Gap Register (excerpt, as at SVRN7 v0.8.0)

| Element Instance          | Type           | Artefact                   | Status   |
|---------------------------|----------------|----------------------------|----------|
| HTTP Listener/Sender      | Protocol       | KestrelListenerService.cs  | ✓ Done   |
| Switchboard (hosted svc)  | Switchboard    | SwitchboardHostedService   | ✓ Done   |
| Runspace Pool (outer box) | Runspace Pool  | RunspacePoolManager.cs     | ✓ Done   |
| LobeManager               | LOBE (implied) | LobeManager.cs             | ✓ Done   |
| Svrn7RunspaceContext      | (Host service) | Svrn7RunspaceContext.cs    | ✓ Done   |
| Svrn7.Email.psm1          | LOBE           | Svrn7.Email.psm1           | ✓ Done   |
| Svrn7.Calendar.psm1       | LOBE           | Svrn7.Calendar.psm1        | ✓ Done   |
| Svrn7.Presence.psm1       | LOBE           | Svrn7.Presence.psm1        | ✓ Done   |
| Svrn7.Notifications.psm1  | LOBE           | Svrn7.Notifications.psm1   | ✓ Done   |
| Svrn7.Onboarding.psm1     | LOBE           | Svrn7.Onboarding.psm1      | ✓ Done   |
| Svrn7.Invoicing.psm1      | LOBE           | Svrn7.Invoicing.psm1       | ✓ Done   |
| Agent 1 coordinator       | PS Runspace    | Agent1-Coordinator.ps1     | ✓ Done   |
| Agent 2 onboarding        | PS Runspace    | Agent2-Onboarding.ps1      | ✓ Done   |
| Agent N invoicing         | PS Runspace    | AgentN-Invoicing.ps1       | ✓ Done   |
| IMemoryCache wiring       | Data Access    | DI + Svrn7RunspaceContext  | ✓ Done   |
| Schema Registry (LiteDB)  | Data Storage   | SchemaLiteContext.cs       | ✓ Done   |
|   [Cond: Society TDA Only]|                | ISchemaRegistry + impls    |          |
| Schema Resolver           | Data Access    | ISchemaResolver + impls    | ✓ Done   |
|   [Cond: Society TDA Only]|                |                            |          |
| InboxMessage.Id DID URL   | (protocol)     | TdaResourceId in Core      | ✓ Done   |
| Dead-letter outbox        | Data Storage   | Outbox collection in inbox | Deferred |

Elements in the Gap Register represent planned work, not design uncertainty. The diagram defines
what MUST be built; the Gap Register tracks when it will be built.

---

## 8. AI Code Generation Contract

Parchment Programming is designed to function as a primary input to AI code generation systems.
This section specifies the contract between a conformant Parchment Diagram and an AI code
generator.

### 8.1 Diagram Guarantees to the AI Generator

A conformant Parchment Diagram MUST make the following guarantees:

**G1. Every element instance has a unique, unambiguous label.** The AI generator resolves
artefact names from labels without disambiguation.

**G2. Every element instance belongs to exactly one element type.** The AI generator determines
the artefact category from the Legend derivation rules without inference.

**G3. Containment relationships define ownership hierarchies.** The AI generator produces
correct DI registration order, startup sequence, and dispose order from containment structure.

**G4. Arrow direction defines dependency direction.** The AI generator produces correct
constructor injection declarations from arrow direction.

**G5. The Gap Register is complete.** The AI generator produces a complete list of missing
artefacts, their types, and priorities from the Gap Register alone.

**G6. The diagram version is identified.** The AI generator can confirm the correct version
and detect version mismatches with the codebase.

### 8.2 AI Generator Obligations

An AI code generator processing a conformant Parchment Diagram MUST:

**O1. Ground all generated artefact names in element instance labels.** The AI generator MUST
NOT invent artefact names that do not correspond to any element instance.

**O2. Respect layered derivation order.** The AI generator MUST NOT generate an inner-layer
artefact before the enclosing outer layer's interface has been derived and confirmed.

**O3. Honour the tractability invariant.** The AI generator MUST flag any generation request
for an artefact that has no corresponding diagram element instance. It MUST NOT silently
generate such an artefact.

**O4. Propagate the Gap Register.** After generating artefacts, the AI generator MUST update
the Gap Register to reflect resolved gaps and MAY add newly discovered gaps.

**O5. Report derivation trace.** For each generated artefact, the AI generator SHOULD produce
a one-line derivation trace: "Artefact X derived from element instance Y of type Z in diagram
version V."

### 8.3 The Parchment Programming Loop

The interaction between a developer, a Parchment Diagram, and an AI code generator follows a
defined loop:

```
LOOP:
  1.  Developer updates the Parchment Diagram (adds/modifies element instances).
  2.  Developer provides the updated diagram and current Gap Register to the AI generator.
  3.  AI generator identifies element instances not yet in the Tractability Matrix.
  4.  AI generator applies derivation rules to each new element instance.
  5.  AI generator produces artefact(s) for each new element instance.
  6.  AI generator produces a derivation trace for each artefact.
  7.  AI generator updates the Gap Register (removes resolved, notes new gaps).
  8.  Developer reviews generated artefacts for correctness.
  9.  Developer commits artefacts and updated Gap Register.
  10. Developer updates Tractability Matrix.
UNTIL: Gap Register is empty.
```

This loop is the normative workflow for Parchment Programming in an AI-assisted environment.
The diagram drives the loop; the AI generator accelerates it; the developer reviews and
approves.

### 8.4 Interpreting Diagram Ambiguities

Diagrams may contain visual distinctions that the AI generator must interpret without explicit
prose guidance. This section provides normative interpretation rules for common cases.

**Rule AI-1: Positional annotation vs. element type difference.** If two element instances
of the same element type appear in different spatial positions (e.g., one inside the Runspace
Pool, one outside), the AI generator MUST treat this as a positional annotation, not a type
difference. The artefact category is identical; only configuration may differ.

Example (DSA TDA): LOBE instances appear both inside the Runspace Pool (Email, Calendar,
Presence, Notifications LOBEs) and outside (UX LOBE, Onboard LOBE, Invoicing LOBE). Both sets are
instances of element type LOBE. Both derive the same artefact category: PowerShell Module.
The positional distinction indicates temporal load behaviour (eager pre-load vs. JIT import),
which is recorded in configuration (lobes.config.json), not in the type system.

**Rule AI-2: Unlabelled instances are extensibility slots.** An element instance labelled
only with "..." or an ellipsis is an intentionally unlabelled extensibility slot. The AI
generator MUST record it in the Gap Register as a planned-but-unspecified gap. It MUST NOT
generate a concrete artefact for an unlabelled instance.

**Rule AI-3: Repeated instances are peer deployments.** Multiple element instances of the same
type with the same or parallel labels (e.g., five "Citizen TDA" boxes in the VTC7 mesh) derive
the same artefact. The AI generator generates one artefact and notes that it is deployed N
times.

---

## 9. The Diagram Change Process

### 9.1 Change Triggers

A Parchment Diagram MUST be updated when any of the following occurs:

- A new component is introduced (new element instance required).
- An existing component is renamed (element instance label updated).
- An existing component's architectural role changes (element type re-assignment).
- A new relationship is established (new arrow).
- An existing relationship is removed (arrow deletion).
- A new element type is needed (Legend update — see Section 9.2).
- The epoch transitions (see Section 9.3).

A Parchment Diagram MAY be updated when:

- An element instance label is clarified (cosmetic rename that does not change the artefact name).
- The diagram layout is reorganised for readability (no semantic change).

### 9.2 Legend Updates

Adding a new element type to the Legend is a significant change and MUST follow this process:

1. Propose the new element type with its name, visual style, semantic definition, and
   derivation rule.
2. Verify that no existing element type already covers the semantic class of the proposed type.
3. Review all existing element instances to confirm none should be re-typed to the new type.
4. Add the new element type to the Legend.
5. Update the diagram to use the new element type for any element instances it covers.
6. Update the derivation rules document.
7. Update the AI Code Generation Contract if new derivation obligations are introduced.

Removing an element type is PROHIBITED unless the element type has no instances in the diagram.
An element type with existing instances MUST be deprecated before it can be removed in a
subsequent epoch.

### 9.3 Epoch Transitions

A Parchment Epoch transition is triggered when the system undergoes a fundamental architectural
restructuring — not incremental extension, but a change to element types, zone organisation, or
principal ownership hierarchies.

An epoch transition REQUIRES:

1. A complete Legend review: all element types are re-confirmed or deprecated.
2. A new version identifier in the diagram title block.
3. A new Gap Register initialised from the delta between previous and new diagram.
4. A compatibility statement documenting which artefacts from the previous epoch remain valid.

### 9.4 Versioning Convention

Parchment Diagrams MUST use the following versioning convention in their title block:

```
{System Name} {Major}.{Minor} Epoch {N}
```

Where:

- `{System Name}` is the canonical name of the system or system component.
- `{Major}.{Minor}` is the diagram version. Minor increments are within-epoch changes.
  Major increments indicate epoch transitions.
- `Epoch {N}` is the epoch designator. N starts at 0.

Example: "Web 7.0 Decentralized System Architecture (DSA) 0.19 Epoch 0".

---

## 10. Conformance

### 10.1 Conformant Parchment Diagram

A diagram conforms to this specification if and only if:

1. It contains a title block satisfying Section 5.1 item 1.
2. It contains a Legend satisfying Section 5.2.
3. Every element instance satisfies Section 5.3.
4. Arrows are assigned consistent semantics satisfying Section 5.4.
5. A Gap Register exists and is maintained satisfying Section 7.3.
6. Derivation rules are documented for every element type in the Legend per Section 6.1.

### 10.2 Conformant Implementation

An implementation conforms to this specification if and only if:

1. A Tractability Matrix exists and is current per Section 7.1.
2. Every artefact has a documented derivation trace per Section 7.2.
3. No undocumented deviations exist.
4. The Gap Register is current and complete.
5. Changes to the implementation are preceded by or accompanied by diagram changes per
   Section 9.1.

### 10.3 Partial Conformance

A partially conformant implementation MUST:

1. Maintain a conformant Parchment Diagram for the subset of the system it covers.
2. Identify the boundary of coverage explicitly (zones, layers, or element types in scope).
3. Not make conformance claims for element instances outside its declared coverage boundary.

---

## 11. Extended Example: Web 7.0 DSA TDA

This section provides a complete worked example of Parchment Programming applied to the
Web 7.0 Decentralized System Architecture (DSA) Trusted Digital Assistant (TDA). This example
is normative: all rules in Sections 4 through 10 are illustrated here and MUST be interpreted
consistently with this example.

### 11.1 Diagram Overview

The Web 7.0 DSA 0.24 Epoch 0 diagram depicts a Citizen/Society Trusted Digital Assistant —
a sovereign, DID-native, DIDComm-native runtime. The diagram title block:

```
Web 7.0™ Decentralized System Architecture (DSA) 0.24 Epoch 0
Secure, Trusted, DID-native, DIDComm-native Web 7.0 DIDLibOS
Copyright © 2026 Michael Herman (Alberta, Canada) — TDW™ — CC BY-SA 4.0
```

The PPML Legend 0.25 (Section 5.2.1) appears in the lower-left corner of the diagram,
inside a light-grey rounded-rectangle boundary box. It defines eleven element types
arranged in a 2×N grid layout:

- Upper area (left to right): Protocol (vertical purple pill), Network (vertical yellow bar),
  LOBE (blue outline with tablet icon), Device (blue outline label inside LOBE)
- Right column (top to bottom): Data Storage (dark navy cylinder), Data Access (dark navy
  rectangle), Runspace Pool (beige outline)
- Middle row: Switchboard (orange outline, wide), PowerShell Runspace (crimson outline)
- Lower row: Host (green outline, widest)
- Bottom: Conditional Components Criteria (light blue dashed border, full width)

DSA 0.24 introduces three significant changes from 0.19:
(1) the Legend is now formally labelled "PPML Legend 0.25"; (2) the Conditional Components
Criteria element type is added as element type 11; and (3) the first Conditional Components
instance appears — "Society TDA Only" — enclosing three registry/resolver pairs. DSA 0.24
also adds Schema Registry (LiteDB) with Schema Resolver as net-new components within the
"Society TDA Only" group.

### 11.2 Legend Application

Applying the Legend produces the following element instance classification (selected):

| Element Instance Label             | Element Type       | Derivation Target              |
|------------------------------------|--------------------|--------------------------------|
| Citizen/Society TDA (Host)         | Host               | Program.cs console app + DI   |
| PowerShell Runspace Pool           | Runspace Pool      | RunspacePool + manager class   |
| Agent 1 Runspace                   | PowerShell Runspace| Agent 1 coordinator script    |
| Agent 2 — LOBE A                   | PowerShell Runspace| LOBE A agent script (generic)  |
| Agent N — LOBE Z                   | PowerShell Runspace| LOBE Z agent script (generic)  |
| DIDComm Message Switchboard        | Switchboard        | DIDCommMessageSwitchboard.cs   |
| UX LOBE                            | LOBE               | Svrn7.UX.psm1                  |
| SVRN7 LOBE                         | LOBE               | Svrn7.Federation.psm1 +        |
|                                    |                    | Svrn7.Society.psm1             |
| DIDComm V2 Messaging               | Protocol           | DIDCommPackingService.cs       |
| HTTP Listener/Sender (HTTPClient)  | Protocol           | KestrelListenerService.cs +    |
|                                    |                    | HttpClient (named "didcomm")   |
| Long-Term Message Memory (LiteDB)  | Data Storage       | InboxLiteContext.cs            |
| DID Doc Registry (LiteDB)          | Data Storage       | DidRegistryLiteContext.cs      |
| VC Doc Registry (LiteDB)           | Data Storage       | VcRegistryLiteContext.cs       |
| IMemoryCache                       | Data Access        | IMemoryCache DI registration   |
| DID Doc Resolver                   | Data Access        | IDidDocumentResolver + impls   |
| VC Doc Resolver                    | Data Access        | IVcDocumentResolver + impls    |
| Schema Registry (LiteDB) [NEW]     | Data Storage       | SchemaLiteContext.cs           |
|   (Conditional: Society TDA Only)  |                    | ISchemaRegistry implementation |
| Schema Resolver [NEW]              | Data Access        | ISchemaResolver + impls        |
|   (Conditional: Society TDA Only)  |                    |                                |
| SOVRONA (SVRN7) SRC                | (line label)       | ISvrn7Driver.TransferAsync +   |
|                                    |                    | 8-step UTXO validator          |
| Citizen TDA (x5, VTC7 mesh)        | Host               | Same software deployed N times |

### 11.3 Derivation Trace Examples

**DIDCommPackingService.cs** (Svrn7.DIDComm project)
Derived from: "DIDComm V2 Messaging" — element type Protocol — DSA 0.24 Epoch 0.
Derivation rule: Protocol -> implements IDIDCommService; provides UnpackAsync/PackEncryptedAsync.

**InboxLiteContext.cs** (Svrn7.Society project)
Derived from: "Long-Term Message Memory (LiteDB)" — element type Data Storage — DSA 0.24 Epoch 0.
Derivation rule: Data Storage database -> LiteDB context class; one collection per entity type.

**Svrn7.Federation.psm1** (lobes/Svrn7.Federation.psm1)
Derived from: "SVRN7 LOBE" — element type LOBE — DSA 0.24 Epoch 0.
Derivation rule: LOBE -> PowerShell module (.psm1); exports named cmdlets (35 cmdlets).

**Register-Svrn7CitizenInSociety** (cmdlet in Svrn7.Society.psm1)
Derived from: "SVRN7 LOBE" (-> Svrn7.Society.psm1) and the Switchboard routing rule for
"onboard/1.0/request" -> Agent 2 Onboarding -> Register-Svrn7CitizenInSociety pipeline.

### 11.4 The Parchment Programming Loop: One Iteration

**Iteration trigger**: Developer adds "HTTP Listener/Sender (HTTPClient)" to DSA 0.24.

**AI generator input**: Updated diagram, Legend (element type "Protocol"), derivation rule for
Protocol (-> Endpoint + Client artefacts).

**AI generator output**:
- KestrelListenerService.cs: BackgroundService implementing a Kestrel minimal API with
  POST /didcomm route. Calls IDIDCommService.UnpackAsync followed by
  IInboxStore.EnqueueAsync. Returns 202 Accepted. HTTP/2 + mTLS.
- HttpClient registration: named client "didcomm" with Polly exponential backoff, 3 attempts.
- Derivation trace: "KestrelListenerService derived from element 'HTTP Listener/Sender
  (HTTPClient)' of type Protocol in DSA 0.24 Epoch 0."

**Gap Register update**: "HTTP Listener/Sender" entry removed. Tractability Matrix updated.

### 11.5 LOBE Temporality: Applying Rule AI-1

The DSA 0.24 diagram shows LOBEs both outside the PowerShell Runspace Pool (UX LOBE,
Onboard LOBE, Invoicing LOBE, "..." slot) and inside Agent 1 Runspace (SVRN7, Email,
Calendar, Presence, Notifications). All are instances of element type LOBE.

Applying Rule AI-1 (Section 8.4): the positional distinction is a positional annotation, not
an element type difference. Both sets are instances of element type LOBE. Both derive the same
artefact category: PowerShell Module.

The positional distinction indicates temporal load behaviour:

- LOBEs shown outside the Runspace Pool: pre-loaded (eager) into RunspacePool.InitialSessionState
  at Host startup. Listed in lobes.config.json "eager" array.
- LOBEs shown inside a Runspace: imported on first use (JIT) by the agent runspace script via
  Import-Module. Listed in lobes.config.json "jit" array.

This interpretation satisfies PP-3 (element instance unambiguity) by treating all LOBE
instances as the same element type while preserving the visual distinction as a meaningful
temporal annotation.

### 11.6 Protocol Design from LOBE Element Instances

Each LOBE element instance in the DSA TDA derives not only a PowerShell module but also one
or more DIDComm protocol URIs that the Switchboard uses for routing. This is an example of
a single element type (LOBE) producing multiple artefact categories (Module + Protocol):

| LOBE Instance       | Module File             | DIDComm Protocol URIs                     |
|---------------------|-------------------------|-------------------------------------------|
| Common LOBE         | Svrn7.Common.psm1       | — (shared helpers, eager)                 |
| Federation LOBE     | Svrn7.Federation.psm1   | did:drn:svrn7.net/protocols/transfer/1.0/*|
|                     |                         | did:drn:svrn7.net/protocols/did/1.0/*     |
| Society LOBE        | Svrn7.Society.psm1      | did:drn:svrn7.net/protocols/transfer/1.0/*|
|                     |                         | did:drn:svrn7.net/protocols/onboard/1.0/* |
| UX LOBE             | Svrn7.UX.psm1           | did:drn:svrn7.net/protocols/ux/1.0/*      |
| Email LOBE          | Svrn7.Email.psm1        | did:drn:svrn7.net/protocols/email/1.0/*   |
| Calendar LOBE       | Svrn7.Calendar.psm1     | did:drn:svrn7.net/protocols/calendar/1.0/*|
| Presence LOBE       | Svrn7.Presence.psm1     | did:drn:svrn7.net/protocols/presence/1.0/*|
| Notifications LOBE  | Svrn7.Notifications.psm1| did:drn:svrn7.net/protocols/notification/1.0/*|
| Onboarding LOBE     | Svrn7.Onboarding.psm1   | did:drn:svrn7.net/protocols/onboard/1.0/* |
| Invoicing LOBE      | Svrn7.Invoicing.psm1    | did:drn:svrn7.net/protocols/invoice/1.0/* |
| Identity LOBE       | Svrn7.Identity.psm1     | did:drn:svrn7.net/protocols/did/1.0/*     |
|                     |                         | did:drn:svrn7.net/protocols/vc/1.0/*      |

Each LOBE either defines a net-new DIDComm protocol or tunnels an existing industry standard
inside a DIDComm envelope:

- **Net-new**: Presence (presence/1.0/status, presence/1.0/subscribe), Notifications
  (notification/1.0/alert), Onboarding (onboard/1.0/request, onboard/1.0/receipt), Invoicing
  (invoice/1.0/request, invoice/1.0/receipt).
- **Tunneling**: Email LOBE tunnels RFC 5322 (email message format) inside a DIDComm body.
  Calendar LOBE tunnels iCalendar (RFC 5545) inside a DIDComm body.

### 11.7 Conditional Components: "Society TDA Only"

DSA 0.24 introduces the first instance of the Conditional Components Criteria element type.
A dashed-border rectangle labelled "Society TDA Only" encloses six element instances:

- DID Doc Registry (LiteDB) [Data Storage]
- DID Doc Resolver [Data Access]
- VC Doc Registry (LiteDB) [Data Storage]
- VC Doc Resolver [Data Access]
- Schema Registry (LiteDB) [Data Storage] — net new in DSA 0.24
- Schema Resolver [Data Access] — net new in DSA 0.24

Applying the Conditional Components derivation rule:

1. Each enclosed element instance derives its artefact per its own element type:
   - DID Doc Registry (LiteDB) -> DidRegistryLiteContext.cs (Data Storage rule)
   - DID Doc Resolver -> IDidDocumentResolver + implementations (Data Access rule)
   - VC Doc Registry (LiteDB) -> VcRegistryLiteContext.cs (Data Storage rule)
   - VC Doc Resolver -> IVcDocumentResolver + implementations (Data Access rule)
   - Schema Registry (LiteDB) -> SchemaLiteContext.cs (Data Storage rule)
   - Schema Resolver -> ISchemaResolver + implementations (Data Access rule)

2. All six artefacts MUST be conditionally instantiated. The condition is "Society TDA Only":
   they are registered in the DI container and their Data Storage databases opened ONLY when the
   Host is configured as a Society TDA. A Citizen-only TDA deployment omits these artefacts
   entirely.

3. The condition "Society TDA Only" MUST be represented in configuration:
   - services.AddSvrn7Society() includes these registrations.
   - A minimal Host that calls only services.AddSvrn7Federation() excludes them.

4. The Schema Registry and Schema Resolver are net-new components in DSA 0.24. Their
   artefacts (SchemaLiteContext.cs, ISchemaRegistry, ISchemaResolver) do not exist in
   SVRN7 v0.8.0 and are added to the Gap Register as High priority.

The "Society TDA Only" boundary is the normative authority for which registry/resolver
components are conditional. An implementation that includes these artefacts unconditionally
(i.e., always instantiates them regardless of TDA type) is non-conformant.

### 11.8 Pass-by-Reference Semantics as a Diagram-Derived Constraint

The DSA 0.24 diagram shows the Switchboard connected to the Long-Term Message Memory (LiteDB)
via a directed arrow. Applying the derivation rule for this connection:

- The Switchboard holds a reference to IInboxStore (the Data Access interface for the LiteDB
  Data Storage element instance).
- On receiving a batch of inbound messages, the Switchboard does NOT copy message payloads
  into agent runspaces. Instead, it passes the LiteDB ObjectId (the message reference) to
  the LOBE cmdlet pipeline.

This pass-by-reference design is directly mandated by the connection topology in the diagram:
the Runspace Pool (and the agent runspaces within it) has NO arrow connecting it to the Long-
Term Message Memory element instance. The only connection to Long-Term Message Memory is through
the Switchboard and (via bidirectional arrow) through IMemoryCache. Therefore:

- Agent runspaces MUST NOT hold a reference to IInboxStore.
- Agent runspaces read message content exclusively through IMemoryCache (hot path) or by
  passing the ObjectId to cmdlets that resolve the reference via the $SVRN7.Cache context.

This is an example of a structural constraint (the absence of a connection in the diagram)
deriving a constraint on the implementation (no direct IInboxStore access from agents).

---

## 12. Security Considerations

### 12.1 Diagram Authenticity

A Parchment Diagram accepted as the authoritative specification for a system MUST be protected
against unauthorised modification. Implementations MUST:

- Store the authoritative Parchment Diagram under version control with commit signing.
- Treat the diagram as a security-sensitive artefact with the same access controls as source
  code.
- Verify the identity of the diagram author before accepting a diagram change into the canonical
  record.

### 12.2 AI Generator Trustworthiness

Artefacts produced by an AI generator MUST be reviewed by a human developer before being merged
into the codebase. The Parchment Programming Loop (Section 8.3, step 8) explicitly requires
developer review. Automation of this review step is PROHIBITED.

### 12.3 Tractability as a Security Property

The tractability invariant (Section 7) ensures that no undocumented code exists in the
implementation. An artefact that does not trace to a diagram element cannot have been reviewed
as part of the diagram review process. Maintaining backward tractability is a control against
the introduction of undocumented and unreviewed functionality.

---

## 13. Privacy Considerations

Parchment Diagrams document the architecture of a system, including its data storage topology,
communication patterns, and identity model. Diagrams that include element instances for
personally-identifiable information (PII) stores, user profile databases, or communication
channels carrying private messages MUST be handled as privacy-sensitive documents subject to
applicable data protection regulations.

---

## 14. IANA Considerations

This document has no IANA actions.

---

## 15. References

### Normative

- [RFC2119]  Bradner, S. Key words for use in RFCs. BCP 14, March 1997.
- [RFC8174]  Leiba, B. Ambiguity of Uppercase vs Lowercase in RFC 2119 Key Words. May 2017.

### Informative
- [TDA-LOBE-REGISTRY] Herman, M. TDA LOBE Registry and Descriptor Format.
              draft-herman-tda-lobe-registry-00. Web 7.0 Foundation, 2026.
              Specifies the application-specific extension descriptor format
              (Section 16) for Web 7.0 TDA LOBEs.

- [WEB70-ARCH]       Herman, M. Web 7.0 Digital Society Architecture.
                     draft-herman-web7-society-architecture-00.
- [DRAFT-ALE]        Herman, M. AI Legibility Engineering for Web 7.0 and SVRN7.
                     draft-herman-svrn7-ai-legibility-00.
- [DRAFT-TDA-DESIGN] Herman, M. Web 7.0 TDA Consolidated Design Specification v0.24.
                     Web 7.0 Foundation Internal Document, April 2026.
- [DRAFT-MONETARY]   Herman, M. SOVRONA (SVRN7) Monetary Transfer Protocol.
                     draft-herman-svrn7-monetary-protocol-00.
- [DSA-024]          Herman, M. Web 7.0 Decentralized System Architecture (DSA) 0.24 Epoch 0.
                     Diagram. Web 7.0 Foundation, April 2026.
                     Copyright (c) 2026 Michael Herman (Alberta, Canada) -- TDW(TM) -- CC BY-SA 4.0.
- [DSA-019]          Herman, M. Web 7.0 Decentralized System Architecture (DSA) 0.19 Epoch 0.
                     Diagram. Web 7.0 Foundation, April 2026. Superseded by [DSA-024].
- [MDE-OVERVIEW]     Schmidt, D. Model-driven Engineering. IEEE Computer 39(2), 2006.
- [ADR-NYGARD]       Nygard, M. Documenting Architecture Decisions. Cognitect Blog, 2011.
                     https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions.
- [WEB70-IMPL]       Herman, M. SOVRONA (SVRN7) .NET 8 Reference Implementation.
                     https://github.com/web7foundation/svrn7.
- [LLMS-TXT]         Herman, M. SVRN7 llms.txt -- Authoritative Platform Description.
                     https://svrn7.net/llms.txt.


---

## 16. Application-Specific Extension Mechanisms

### 16.1 Overview

PPML defines the diagram grammar, derivation rules, and change process for a general
class of C#/.NET software systems. Individual systems built on PPML MAY extend the
derivation rules in Section 6 to address application-specific artefact types that do
not appear in the generic PPML derivation table.

Common extension patterns include:

**Plugin or extension descriptors.** Many systems require a mechanism for third-party
developers to extend the system's capability set without modifying compiled code. A
plugin descriptor — a structured file (JSON, YAML, or XML) shipped alongside an extension
module — declares the extension's identity, capability registrations, and metadata.
Each such descriptor is a PPML artefact derived from a diagram element of an application-
defined element type. The diagram element type MUST be defined in the Legend. The derivation
rule MUST specify: the descriptor file format, the file naming convention, the fields
required for capability registration, and the runtime loading mechanism.

**Runtime capability registries.** Systems that support hot-loading of extensions (without
process restart) typically maintain an in-process registry that maps capability keys
(e.g., protocol URI patterns, event type strings, command names) to loaded module handles
and entry-point names. This registry is a PPML artefact derived from a Switchboard, Router, Dispatcher, or
equivalent routing element in the diagram.

**AI legibility metadata.** Systems intended for AI-assisted operation or AI-aided
pipeline construction SHOULD include an AI metadata block in plugin descriptors. This block
provides machine-readable summaries, use cases, composition hints, and limitations that
enable an AI to discover, understand, and compose plugin pipelines. The structure of this
block SHOULD mirror the tool definition format of the Model Context Protocol (MCP) [MCP-SPEC]
to enable forward compatibility with MCP-based AI integration in future system versions.

### 16.2 Requirement on Extension Artefacts

All extension descriptors and runtime registries MUST satisfy the Tractability Invariant
(Section 7): every extension descriptor MUST trace to a named diagram element, and every
field in the descriptor schema MUST derive from a requirement documented in the diagram
or its associated Gap Register. Extension descriptor fields with no diagram traceability
are undocumented extensions and MUST be documented in a revision to the diagram before
the next release.

### 16.3 Separation of Concerns

Application-specific extension descriptor formats and registry implementations MUST be
documented in application-specific specifications or design documents, not in this
document. This document specifies only the PPML-level requirements: that such descriptors
exist as PPML artefacts, that they derive from diagram elements, and that they satisfy
the Tractability Invariant. The content and schema of any specific descriptor format is
outside the scope of this specification.
---

## Author's Address

Michael Herman
Web 7.0 Foundation
Bindloss, Alberta, Canada
Email: mwherman@gmail.com
URI:   https://hyperonomy.com/about/
