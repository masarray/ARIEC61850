# Security and Operational-Risk Policy

ARIEC61850 includes code paths that can connect to IEC 61850 endpoints, write report-control attributes, issue guarded control requests, and publish raw Ethernet process-bus traffic. Treat every active network function as security-sensitive and operationally significant.

## Supported versions

The repository is pre-release. Security review and fixes apply to the current `main` branch unless a release notice states otherwise.

## Reporting a vulnerability

Use a private GitHub Security Advisory when available. When private reporting is unavailable, open a minimal public issue requesting maintainer contact and do not include exploit details, credentials, customer data, network diagrams, or sensitive captures.

Include, where relevant:

- affected commit or release;
- operating system and .NET SDK version;
- application or library component;
- adapter, driver, and transport details;
- whether the path is read-only, write, report, control, or publish;
- minimal synthetic reproduction steps;
- expected and observed behavior;
- impact and practical preconditions.

## Cybersecurity scope

Security review includes:

- malformed or oversized protocol input;
- parser and decoder robustness;
- resource exhaustion and unbounded queues;
- cancellation, timeout, reconnect, and session cleanup;
- unsafe default binding or listening behavior;
- unintended writes, control requests, or raw-frame publishing;
- exposure of credentials, station identifiers, customer data, or local paths;
- dependency and release-package integrity.

The project does not currently claim implementation of IEC 62351 security profiles unless a specific release documents and validates them.

## Operational-risk scope

Protocol acceptance does not prove that primary equipment, interlocking, wiring, protection logic, or a switching procedure is safe. Software guardrails reduce accidental actions but do not replace:

- switching authority;
- approved procedures;
- test permits;
- isolation and blocking plans;
- independent indications;
- site-specific risk assessment;
- manufacturer and asset-owner requirements.

## Active network functions

The following require deliberate operator action and approved test conditions:

- MMS writes;
- report-control and DataSet writes;
- control operations;
- GOOSE publishing;
- Sampled Values publishing;
- packet replay or raw Ethernet injection;
- server or simulator exposure beyond loopback.

Active functions should provide explicit target selection, visible operation details, confirmation, bounded execution, cancellation, cleanup, and evidence.

## Environment guidance

- Use isolated laboratory networks, approved commissioning segments, TAPs, or test switches.
- Do not connect active publishing or control features to an operational substation network without authorization and an approved plan.
- Prefer documentation-only IP addresses in examples.
- Use synthetic or contributor-owned SCL and captures.
- Keep real customer evidence outside the public repository.
- Run with the least privilege required by the transport.

## Claim boundary

A passing build, test suite, loopback profile, or laboratory exercise is not a cybersecurity certification, functional-safety assessment, IEC 61850 conformance certificate, or permission for operational deployment.
