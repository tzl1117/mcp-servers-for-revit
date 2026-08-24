/**
 * Manual Integration Test Script (stdio transport)
 *
 * Run with: node tests/integration/manual-test.mjs
 *
 * The MCP server (server/src/index.ts) communicates over STDIO, not HTTP.
 * This script spawns `node server/build/index.js` as a child process and
 * talks to it using the official MCP Client SDK, exactly like Claude Desktop
 * would. Requires `npm run build` in server/ to be up to date, and
 * `npm install` to have been run in this tests/ folder.
 */

import path from "node:path";
import fs from "node:fs";
import { fileURLToPath } from "node:url";
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";
import { getLogFilePath } from "../../server/build/utils/activityLogger.js";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const serverEntry = path.resolve(__dirname, "../../server/build/index.js");

// Unique per run so log-file assertions can find exactly the entries this run produced.
const logMarker = `test-marker-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
let markerRequestId;

const results = [];

async function runTest(name, fn, { expectError = false } = {}) {
  const startTime = Date.now();
  console.log(`\n▶ Running: ${name}`);
  try {
    const response = await fn();
    const duration = Date.now() - startTime;
    const isToolError = response?.isError === true;
    const passed = expectError ? isToolError : !isToolError;

    console.log(`  ${passed ? "✓" : "✗"} Response (${duration}ms):`);
    console.log(`  ${JSON.stringify(response, null, 2).split("\n").join("\n  ")}`);

    results.push({ name, passed, duration });
  } catch (error) {
    const duration = Date.now() - startTime;
    const passed = expectError; // a thrown error also satisfies "expected to fail"
    console.log(`  ${passed ? "✓" : "✗"} Threw (${duration}ms): ${error.message}`);
    results.push({ name, passed, duration });
  }
}

async function main() {
  console.log("═════════════════════════════════════════════════════════════");
  console.log("  MCP Server Integration Tests (stdio)");
  console.log(`  Spawning: node ${serverEntry}`);
  console.log("═════════════════════════════════════════════════════════════");

  const transport = new StdioClientTransport({
    command: "node",
    args: [serverEntry],
    // MCP SDK only inherits an allowlisted set of env vars by default (PATH,
    // LOCALAPPDATA, etc.) - pass the full env through so overrides like
    // MCP_REVIT_LOG_DIR set before running this script actually reach the server.
    env: { ...process.env },
  });
  const client = new Client({ name: "manual-test-harness", version: "1.0.0" });
  await client.connect(transport);

  const { tools } = await client.listTools();
  console.log(`\nConnected. ${tools.length} tools registered.`);

  await runTest("say_hello (baseline)", () =>
    client.callTool({ name: "say_hello", arguments: { message: "Test Suite" } })
  );

  await runTest("log_action - basic", () =>
    client.callTool({
      name: "log_action",
      arguments: {
        action: "Test logging system",
        reason: "Verifying log_action tool functionality",
      },
    })
  );

  await runTest("log_action - with code_summary", () =>
    client.callTool({
      name: "log_action",
      arguments: {
        action: "Override revision cloud colors",
        reason: "User requested all clouds to be red in active view",
        code_summary:
          "Query FilteredElementCollector for RevisionCloud, iterate, apply red color override",
      },
    })
  );

  await runTest("log_action - with custom requestId", () =>
    client.callTool({
      name: "log_action",
      arguments: {
        action: "Query all walls in view",
        reason: "User wants wall count and properties",
        code_summary: "FilteredElementCollector.OfClass(typeof(Wall)).ToList()",
        requestId: "REQ-REVIT-WALLS-001",
      },
    })
  );

  await runTest(
    "Invalid tool name (expected to fail)",
    () => client.callTool({ name: "nonexistent_tool", arguments: {} }),
    { expectError: true }
  );

  await runTest(
    "log_action - missing required 'action' (expected to fail)",
    () =>
      client.callTool({
        name: "log_action",
        arguments: { reason: "Missing required 'action' parameter" },
      }),
    { expectError: true }
  );

  await runTest("log_action - unique marker (for log-file assertions)", async () => {
    const response = await client.callTool({
      name: "log_action",
      arguments: { action: logMarker, reason: "Verifying activity log file contents" },
    });
    markerRequestId = JSON.parse(response.content[0].text).requestId;
    return response;
  });

  await runTest("Activity log file (.jsonl) contains expected entries", async () => {
    const logFilePath = getLogFilePath();
    if (!fs.existsSync(logFilePath)) {
      throw new Error(`Log file not found at ${logFilePath}`);
    }

    const entries = fs
      .readFileSync(logFilePath, "utf-8")
      .trim()
      .split("\n")
      .filter(Boolean)
      .map((line, i) => {
        try {
          return JSON.parse(line);
        } catch {
          throw new Error(`Log line ${i + 1} is not valid JSON: ${line}`);
        }
      });

    const claudeIntent = entries.find((e) => e.layer === "CLAUDE-INTENT" && e.action === logMarker);
    if (!claudeIntent) throw new Error(`No CLAUDE-INTENT entry found for marker "${logMarker}"`);
    if (claudeIntent.requestId !== markerRequestId) {
      throw new Error(`CLAUDE-INTENT requestId mismatch: expected ${markerRequestId}, got ${claudeIntent.requestId}`);
    }

    const start = entries.find(
      (e) => e.layer === "MCP-TOOL-CALL" && e.status === "start" && e.tool === "log_action" && e.args?.action === logMarker
    );
    if (!start) throw new Error(`No MCP-TOOL-CALL start entry found for marker "${logMarker}"`);

    const success = entries.find(
      (e) => e.layer === "MCP-TOOL-CALL" && e.status === "success" && e.tool === "log_action" && e.requestId === start.requestId
    );
    if (!success) throw new Error(`No matching MCP-TOOL-CALL success entry for start requestId ${start.requestId}`);
    if (typeof success.durationMs !== "number") {
      throw new Error(`success entry durationMs is not a number: ${success.durationMs}`);
    }

    const sayHelloStart = entries.find(
      (e) => e.layer === "MCP-TOOL-CALL" && e.status === "start" && e.tool === "say_hello"
    );
    if (!sayHelloStart) throw new Error("No MCP-TOOL-CALL start entry found for say_hello");

    return {
      content: [
        { type: "text", text: `Verified ${entries.length} log lines; marker + start/success pairing all present.` },
      ],
    };
  });

  await client.close();

  console.log("\n═════════════════════════════════════════════════════════════");
  console.log("  Test Summary");
  console.log("═════════════════════════════════════════════════════════════");

  const passed = results.filter((r) => r.passed).length;
  const total = results.length;
  const avgDuration = results.reduce((sum, r) => sum + r.duration, 0) / total;

  console.log(`\n  Passed: ${passed}/${total}`);
  console.log(`  Average Response Time: ${avgDuration.toFixed(0)}ms\n`);

  results.forEach((r) => {
    const status = r.passed ? "✓" : "✗";
    const time = `${r.duration}ms`;
    console.log(`    ${status} ${r.name.padEnd(45)} [${time.padStart(6)}]`);
  });

  console.log("\n═════════════════════════════════════════════════════════════");
  process.exit(passed === total ? 0 : 1);
}

main().catch((error) => {
  console.error("Fatal error:", error);
  process.exit(1);
});
