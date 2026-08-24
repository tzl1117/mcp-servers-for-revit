# MCP Server Testing Guide

This directory contains test infrastructure for the mcp-servers-for-revit MCP Server component (Node.js).

## ⚠️ Important: this server uses STDIO, not HTTP

`server/src/index.ts` connects via `StdioServerTransport` - exactly how Claude
Desktop talks to it (spawns `node build/index.js` and communicates over
stdin/stdout using JSON-RPC framed messages). **There is no HTTP listener on
port 3000.** Tools like `curl`, REST Client, or `fetch()` cannot reach it.

`test.http` is kept only as a historical reference for request/response
shapes and is marked deprecated at the top of the file.

## Quick Start

### 1. Build the server

```bash
cd server
npm install   # first time only
npm run build
```

### 2. Run the automated stdio test harness (recommended)

```bash
# first time only - installs the SDK client used by the harness
cd tests
npm install
cd ..

node tests/integration/manual-test.mjs
```

This script spawns `node server/build/index.js` as a child process, connects
with the official MCP `Client`/`StdioClientTransport`, lists all registered
tools, and calls several of them (`say_hello`, `log_action` variants, and two
expected-failure cases). It prints a pass/fail summary and exits non-zero if
anything unexpectedly failed.

### 3. Explore interactively with MCP Inspector

```bash
npx @modelcontextprotocol/inspector@latest node server/build/index.js
```

Opens a browser UI where you can list tools, fill in arguments, and call them
one at a time - the closest equivalent to what REST Client gave us for HTTP
servers. (v1 of the inspector is deprecated; always use `@latest`.)

### 4. Debug with breakpoints

- Open [`.vscode/launch.json`](../.vscode/launch.json) and pick **"Manual Test
  (stdio, auto-attach to server)"**, then press **F5**.
- This runs `tests/integration/manual-test.mjs`, which spawns the built
  server as a child process. `autoAttachChildProcesses` lets VS Code attach
  its debugger to that child automatically, so breakpoints set in
  `server/src/tools/*.ts` are hit even though the code under the debugger is
  the test harness, not the server itself.
- Alternatively, use the **"MCP Server (Debug)"** config to run the server
  directly if you just want to confirm it boots and registers tools without
  driving it from a client.

## Testing Workflow

### Workflow 1: Rapid Iteration (No Revit)

1. Edit a tool in `server/src/tools/*.ts`.
2. `cd server && npm run build`
3. `node tests/integration/manual-test.mjs` (from repo root) to re-run the
   suite, or use MCP Inspector to call the tool by hand.
4. Set breakpoints and use the **"Manual Test (stdio, auto-attach to
   server)"** debug config for step-through debugging.

### Workflow 2: Full Integration (With Revit)

Tools like `send_code_to_revit` and `say_hello` require the Revit plugin to
be listening on `localhost:8080`. Without Revit running, these calls return a
handled error (`"...failed: connect to revit client failed"`) rather than
crashing - this is expected and confirms the MCP layer works independently
of Revit.

1. Start Revit (ensure the plugin is loaded).
2. Run `node tests/integration/manual-test.mjs`, or use MCP Inspector to call
   `send_code_to_revit` / `say_hello`.
3. Debug the C# plugin side separately: open Visual Studio → Debug → Attach
   to Process → `Revit.exe`.

### Workflow 3: Automated Testing

```bash
node tests/integration/manual-test.mjs
```

Exit code `0` = all tests passed, `1` = at least one failed.

## Adding a new test case

Edit `tests/integration/manual-test.mjs` and add another `runTest(...)` call,
e.g.:

```js
await runTest("my_new_tool - happy path", () =>
  client.callTool({ name: "my_new_tool", arguments: { foo: "bar" } })
);
```

Pass `{ expectError: true }` as a third argument for cases that should fail
(missing required params, unknown tool names, etc.) - the harness treats a
thrown error or an `isError: true` tool response as a pass in that mode.

## Logs Location

**Activity logs:**
- File: `%LOCALAPPDATA%\mcp-server-for-revit\logs\revit-activity.jsonl`
  (override with the `MCP_REVIT_LOG_DIR` env var), append-only, one JSON
  object per line (JSON Lines format). Two kinds of entries are written:
  - `layer: "MCP-TOOL-CALL"` - automatic, written for **every** tool call
    regardless of which tool. A `status: "start"` entry is written before the
    handler runs and a matching `status: "success"` / `"error"` entry
    (same `requestId`, plus `durationMs` and the full result/error) after it
    finishes. This happens for free for any tool registered via
    `server.tool(...)` - see `server/src/tools/register.ts` and
    `server/src/utils/activityLogger.ts`.
  - `layer: "CLAUDE-INTENT"` - written only when the `log_action` tool is
    explicitly called, capturing Claude's stated intent/reasoning before
    acting. Has its own `requestId`, separate from the `MCP-TOOL-CALL` entry
    for the `log_action` call itself.

**MCP Server console output:**
- Printed to stderr by the server itself (tool registration messages,
  `"Revit MCP Server start success"`, connection errors). Visible directly in
  the terminal running `manual-test.mjs` or the MCP Inspector.

## Debugging Tips

### "Tool not found" error

1. Is the tool file in `server/src/tools/` and does it export a function
   starting with `register` (e.g. `registerMyTool`)? `register.ts`
   auto-discovers tools this way - no manual registration list to update.
2. Did you run `npm run build` in `server/` after adding/editing the file?
3. Check the console output for `已注册工具: <file>.js` (registered) or a
   warning that no register function was found.

### "connect ECONNREFUSED ::1:8080" when calling Revit-dependent tools

Expected when Revit / the plugin isn't running. The tool call itself still
succeeds at the MCP layer and returns a handled error message rather than
throwing - this is normal for local testing without Revit.

### "Breakpoint not hitting"

1. `npm run build` in `server/` to make sure `.js`/`.map` files are current.
2. Use the **"Manual Test (stdio, auto-attach to server)"** launch config
   (not a plain terminal run) - only the debugger-launched process gets
   breakpoints via `autoAttachChildProcesses`.
3. Confirm `sourceMaps: true` in `.vscode/launch.json` and that
   `server/tsconfig.json` has `"sourceMap": true`.
