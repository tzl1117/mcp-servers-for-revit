# MCP Security Review

## Project summary

- Name: mcp-servers-for-revit
- Source: https://github.com/mcp-servers-for-revit/mcp-servers-for-revit
- Tier: 2
- Date: June 25, 2026
- Reviewer: Stu Charlton
- Overall status: 31% · REMEDIATION REQUIRED

## Executive summary

mcp-servers-for-revit is an open-source MCP server that connects AI assistants such as Claude to a live Autodesk Revit session via a local TCP socket bridge. It exposes 27 tools spanning read, write, create, delete, and arbitrary C# code execution inside Revit. The TypeScript MCP server runs locally and communicates only with a C# plugin listening on localhost:8080—no independent outbound connections were identified in the source code. However, because tools return Revit model and room data to the AI client over the MCP stdio transport, confidential BIM and project data is inherently passed to whichever cloud AI service the user has configured.

The most critical finding is the send_code_to_revit tool, which allows an AI client to execute arbitrary C# code inside the running Revit process with full .NET API access—including the file system, network stack, and registry. There is no sandboxing, no allowlisting, and no human confirmation step. This creates a severe prompt-injection and lateral-movement risk: a manipulated or compromised AI prompt could destroy model data, exfiltrate client files, or pivot to other local services. Additional gaps include the absence of authentication on the localhost socket, no TLS encryption on the local channel, no audit logging, no responsible disclosure policy, and no dependency CVE scanning in CI. Several of these gaps are directly in scope for Tier 2 controls.

The overall weighted score of 30.9% is well below the 75% conditional-approval threshold. This MCP must not be deployed on any project until send_code_to_revit is disabled or removed and the other critical controls listed below are remediated.

Known Vendor Confirmed: Autodesk / Sparx Fire. Accountability runs through SmithGroup's Autodesk relationship. Note: the direct publisher is Sparx Fire (sparx-fire.com), a fire protection technology startup that operates independently of Autodesk—Autodesk does not appear to own, endorse, or maintain this MCP server. This relationship must be formally verified with IT Security before deployment. Known Vendor score floors have been applied to applicable items.

## Tier 2 justification

- Exposes write, modify, and delete operations on live Revit model elements.
- Provides a send_code_to_revit tool that executes arbitrary C# in the Revit process with full system access.
- Returns BIM/project data (elements, rooms, material quantities, geometry) to the AI client; when Claude Desktop is used, this data flows to Anthropic cloud infrastructure.
- No authentication, no TLS, and no audit trail — all Tier 2 mandatory controls.
- Per SmithGroup policy, any MCP that passes project or model data to a cloud AI service is Tier 2 at minimum.

## Critical findings

### EXEC-1 — Unrestricted dynamic C# code execution

The send_code_to_revit tool accepts arbitrary C# source code and executes it inside the Revit process with no sandboxing, allowlisting, or validation. The executing code has full access to the .NET runtime, Windows file system, Revit API Document object, and local network. An adversarial prompt or prompt-injection attack could destroy model data, exfiltrate client project files, install malware, or pivot to internal network services. This tool must be disabled or removed before any SmithGroup project deployment.

### EXEC-2 + EXEC-3 — Code execution is unsandboxed and has no human approval gate

No AppDomain isolation, container boundary, or filesystem restriction is applied. Code runs with the same permissions as Revit.exe. An AI client can invoke send_code_to_revit autonomously without any user confirmation dialog.

### SCOPE-1 + SCOPE-2 — Tool scope far exceeds minimum necessary access; destructive ops have no confirmation

send_code_to_revit violates the principle of least privilege by granting the AI unrestricted system access through the Revit process. Additionally, delete_element can be called by the AI with no confirmation dialog, no double-confirmation, and no undo mechanism — a mistaken or adversarial call can irreversibly destroy model content.

### AUTH-1 — No authentication on MCP server or Revit plugin socket

The TypeScript MCP server requires no credentials. The Revit plugin listens on localhost:8080 with no authentication — any local process can send arbitrary JSON-RPC commands to Revit, including send_code_to_revit.

## Scored checklist

### CHAIN — Supply Chain Trust

- CHAIN-1: Publisher identity is verifiable and accountable
  - Result: Partial
  - Weight: 3
  - Notes: Direct publisher is Sparx Fire / mcp-servers-for-revit GitHub org. Known Vendor confirmed by reviewer via Autodesk; direct accountability not confirmed. Must verify with IT Security.

- CHAIN-2: SBOM / dependency manifest is available and auditable
  - Result: Partial
  - Weight: 2
  - Notes: server/package.json lists 4 runtime deps: @modelcontextprotocol/sdk ^1.7.0, better-sqlite3 ^12.8.0, ws ^8.18.1, zod ^3.24.2. No formal SBOM; npm audit not run. KV floor applied.

- CHAIN-3: Binary distribution is signed or independently verifiable
  - Result: Partial
  - Weight: 2
  - Notes: npm package uses OIDC trusted publishing with provenance attestation. GitHub Releases include C# DLL binaries without confirmed code signing. KV floor applied.

### INJ — Prompt & Tool Injection

- INJ-1: Tool descriptions contain no prompt injection vectors
  - Result: Partial
  - Weight: 3
  - Notes: All 27 tool descriptions reviewed; no embedded injection strings detected. Behavioral sandbox testing required before deployment. KV floor applied.

- INJ-2: Tool outputs are sanitized before return to AI
  - Result: Partial
  - Weight: 2
  - Notes: TypeScript code parses WebSocket JSON responses and returns via MCP SDK. No explicit sanitization layer. Behavioral testing required. KV floor applied.

- INJ-3: Server is resilient to adversarial model-supplied inputs
  - Result: Partial
  - Weight: 3
  - Notes: RISK: send_code_to_revit accepts arbitrary C# with no filtering — a prompt injection attack could execute malicious code. KV floor raises from 0 to 1; this remains a critical open item.

### AUTH — Authentication & Authorization

- AUTH-1: MCP server requires authentication to invoke tools
  - Result: Fail
  - Weight: 2
  - Notes: No credentials required on stdio interface. Revit plugin on localhost:8080 accepts unauthenticated JSON-RPC connections from any local process.

- AUTH-2: Unauthorized data access is prevented by access controls
  - Result: Partial
  - Weight: 2
  - Notes: No access controls on localhost socket. KV floor applied; contractual liability assumed via Autodesk relationship. Physical verification needed.

### NET — Network Behavior

- NET-1: No unexpected outbound connections are made
  - Result: Pass
  - Weight: 3
  - Notes: Source code reviewed: TypeScript server connects only to localhost:8080; no external API calls. Caveat: send_code_to_revit could execute code making external connections.

- NET-2: All network traffic is encrypted in transit
  - Result: Fail
  - Weight: 2
  - Notes: TCP socket to localhost:8080 uses raw plaintext TCP. No TLS. All tool I/O is unencrypted on the local channel.

- NET-3: Network connections are scoped to minimum required hosts
  - Result: Pass
  - Weight: 1
  - Notes: Hardcoded to localhost:8080 only in ConnectionManager.ts. No external host connectivity in TypeScript layer.

### SCOPE — Permission Scope

- SCOPE-1: Tools expose only minimum necessary access
  - Result: Fail
  - Weight: 3
  - Notes: send_code_to_revit grants unrestricted C# execution with full Revit API, file system, and .NET access. Far exceeds minimum necessary for any Revit AI use case.

- SCOPE-2: Destructive operations require explicit confirmation
  - Result: Fail
  - Weight: 3
  - Notes: delete_element requires no confirmation. No undo mechanism documented. AI can call it autonomously without human approval.

- SCOPE-3: Read and write capabilities are granularly controllable
  - Result: Partial
  - Weight: 2
  - Notes: Revit plugin Settings UI allows enabling/disabling individual commands. Not enforced per-session at runtime but provides static configuration control.

### EXEC — Code Execution Controls

- EXEC-1: Dynamic code execution is absent or strictly restricted
  - Result: Fail
  - Weight: 4
  - Notes: CRITICAL: send_code_to_revit explicitly executes arbitrary C# code in Revit. No allowlisting, sandboxing, or pattern restriction. Full .NET and Revit API access confirmed in source.

- EXEC-2: Code execution is sandboxed from host file system and network
  - Result: Fail
  - Weight: 3
  - Notes: No sandboxing. C# code executes in Revit.exe process context with full Windows file system, registry, and network access.

- EXEC-3: Code execution requires human-in-the-loop approval
  - Result: Fail
  - Weight: 3
  - Notes: No approval gate. AI can invoke send_code_to_revit autonomously without any Revit dialog or user confirmation step.

### DATA — Data Handling

- DATA-1: Client/project data does not flow to unauthorized external services
  - Result: Partial
  - Weight: 3
  - Notes: TypeScript server makes no independent external connections. However, tool responses (BIM elements, rooms, materials) are returned to the AI client over stdio; with Claude Desktop, this data flows to Anthropic cloud. Review SmithGroup data governance before use on client projects.

- DATA-2: Sensitive data is not persisted beyond session without consent
  - Result: Partial
  - Weight: 2
  - Notes: store_project_data and store_room_data tools write to a local SQLite database (better-sqlite3). Persistence is local only; no cloud persistence from the server itself.

- DATA-3: Data returned to AI is scoped to what was requested
  - Result: Pass
  - Weight: 1
  - Notes: Tools have specific, documented scopes. ai_element_filter provides filtering. get_current_view_elements limited to current view. Reasonable scoping observed.

### LOG — Logging & Audit

- LOG-1: Audit logging covers all tool invocations and parameters
  - Result: Fail
  - Weight: 2
  - Notes: No audit logging. Only console.error() for startup and error conditions. No record of tool calls, parameters, or user context.

- LOG-2: Logs do not capture or transmit sensitive data
  - Result: Pass
  - Weight: 2
  - Notes: Pass by absence: minimal stderr logging contains no sensitive data. Error messages log command names and connection errors only.

- LOG-3: Log output can be routed to SIEM or central logging
  - Result: Fail
  - Weight: 1
  - Notes: Stderr only. No structured JSON logging, no log levels configured, not SIEM-compatible out of the box.

### ISO — Isolation & Sandboxing

- ISO-1: MCP server process runs with least-privilege permissions
  - Result: Fail
  - Weight: 2
  - Notes: Node.js process runs as current user with no privilege dropping. send_code_to_revit escalates effective access to Revit.exe process permissions.

- ISO-2: MCP server is isolated from other host processes
  - Result: Partial
  - Weight: 2
  - Notes: TypeScript process is isolated as a Node.js child process. However, send_code_to_revit allows the server to reach into and control the Revit process and system resources.

### SECMGMT — Security Management

- SECMGMT-1: Responsible disclosure / vulnerability reporting mechanism exists
  - Result: Fail
  - Weight: 1
  - Notes: No SECURITY.md, no vulnerability disclosure policy. GitHub Issues only. No point of contact for security reports.

- SECMGMT-2: Codebase is actively maintained with timely patch cadence
  - Result: Partial
  - Weight: 2
  - Notes: v1.0.0 released Feb 26, 2026; 26 commits; 3 contributors; 8 open issues, 4 open PRs. Actively worked on but young project (<4 months at review date). Limited track record.

- SECMGMT-3: Dependencies are scanned for known CVEs in CI/CD
  - Result: Fail
  - Weight: 2
  - Notes: No Dependabot, no npm audit in CI. GitHub Actions workflows focus on release build and publish only. No security scanning step identified.

### COMP — Compliance

- COMP-1: Privacy policy and data handling documentation exist
  - Result: Fail
  - Weight: 1
  - Notes: No privacy policy. MIT license only. No documentation on what data is collected, stored, or transmitted.

- COMP-2: Security certification (SOC 2, ISO 27001) is on file
  - Result: Fail
  - Weight: 2
  - Notes: Sparx Fire is a hardware startup; no SOC 2 or ISO 27001 certification found. Known Vendor floor (2 if cert on file) not applicable — no cert confirmed.

- COMP-3: Regulatory restrictions (CUI, ITAR, etc.) are documented
  - Result: Fail
  - Weight: 2
  - Notes: No documentation of regulatory restrictions. No prohibition on use with government, CUI, or ITAR-controlled project data.

## Open items requiring resolution

| ID | Issue | Severity | Verification steps |
| --- | --- | --- | --- |
| EXEC-1 | Disable or remove send_code_to_revit before any deployment | CRITICAL | Remove from command.json and commandset DLL, or implement strict C# allowlisting + AppDomain sandboxing + human approval gate before re-enabling. |
| EXEC-2/3 | Sandbox code execution and add human-in-the-loop gate | CRITICAL | If retained, implement AppDomain isolation, filesystem restrictions, and a Revit dialog requiring explicit user approval before execution. |
| SCOPE-2 | Add confirmation for delete_element and destructive tools | CRITICAL | Implement a Revit dialog or MCP confirmation prompt before delete_element executes. Disable destructive tools by default in Settings. |
| AUTH-1 | Add authentication to the localhost socket | HIGH | Implement a shared secret or token-based handshake between TypeScript server and Revit plugin so only authorized clients can send commands. |
| NET-2 | Encrypt the localhost TCP channel | HIGH | Wrap the TCP socket with TLS (self-signed cert acceptable for localhost) to prevent other local processes from intercepting tool I/O. |
| LOG-1 | Implement audit logging for all tool invocations | HIGH | Log tool name, parameters (redacted for sensitive data), timestamp, and user context to a structured log file routable to SmithGroup SIEM. |
| SECMGMT-3 | Add Dependabot or npm audit to CI pipeline | HIGH | Add GitHub Dependabot and npm audit --audit-level=high to release workflow; block release on high-severity CVEs in dependencies. |
| CHAIN-1 | Verify Known Vendor relationship covers Sparx Fire as direct publisher | HIGH | Confirm with IT Security whether the Autodesk Known Vendor relationship extends to Sparx Fire. If not, rescore without KV floors — recommendation remains REMEDIATION REQUIRED. |
| SECMGMT-1 | Add SECURITY.md with responsible disclosure process | MEDIUM | Add SECURITY.md to repo per GitHub advisories best practice. Include point of contact and expected response timeline. |
| COMP-1/3 | Document privacy and regulatory restrictions | MEDIUM | Add documentation specifying: (1) data sent to AI model, (2) local SQLite persistence scope, (3) prohibition on use with CUI/ITAR project data. |

## Score summary

| Metric | Value |
| --- | ---: |
| Weighted Score | 42 |
| Max Possible | 136 |
| Percentage | 30.9% |
| Recommendation | REMEDIATION REQUIRED |

This report was generated by Claude AI using the SmithGroup MCP Security Review framework. Outputs must be reviewed by a qualified IT Security professional before use in any deployment decision. This is not a substitute for a full penetration test or vendor risk assessment. © 2026 SmithGroup.