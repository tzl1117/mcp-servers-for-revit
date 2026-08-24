import fs from "fs";
import path from "path";
import { randomUUID } from "crypto";

/**
 * Resolves the log directory. Host apps (e.g. an installer) can override via
 * MCP_REVIT_LOG_DIR; otherwise falls back to a generic, package-scoped
 * location. This module intentionally has no knowledge of any specific
 * downstream distributor - see server/src/tools/log_action.ts history.
 */
export function getLogDir(): string {
  return (
    process.env.MCP_REVIT_LOG_DIR ||
    path.join(process.env.LOCALAPPDATA || process.env.HOME || "~", "mcp-server-for-revit", "logs")
  );
}

export function getLogFilePath(): string {
  return path.join(getLogDir(), "revit-activity.jsonl");
}

/** Appends one JSON object as a single line to the activity log (JSON Lines format). */
export function logActivity(entry: Record<string, unknown>): void {
  const logDir = getLogDir();
  const logFile = getLogFilePath();

  if (!fs.existsSync(logDir)) {
    fs.mkdirSync(logDir, { recursive: true });
  }

  fs.appendFileSync(logFile, JSON.stringify(entry) + "\n", "utf-8");
}

/**
 * Wraps an MCP tool handler so every call (success or failure) is recorded
 * automatically, independent of whether Claude also calls log_action.
 */
export function withActivityLogging<TArgs extends unknown[], TResult>(
  toolName: string,
  handler: (...args: TArgs) => Promise<TResult>
): (...args: TArgs) => Promise<TResult> {
  return async (...args: TArgs) => {
    const requestId = randomUUID();
    const startedAt = Date.now();
    const toolArgs = args[0];

    logActivity({
      timestamp: new Date().toISOString(),
      requestId,
      layer: "MCP-TOOL-CALL",
      status: "start",
      tool: toolName,
      args: toolArgs,
    });

    try {
      const result = await handler(...args);
      const isToolError = (result as { isError?: boolean } | undefined)?.isError === true;

      logActivity({
        timestamp: new Date().toISOString(),
        requestId,
        layer: "MCP-TOOL-CALL",
        status: isToolError ? "error" : "success",
        tool: toolName,
        durationMs: Date.now() - startedAt,
        result,
      });

      return result;
    } catch (error) {
      logActivity({
        timestamp: new Date().toISOString(),
        requestId,
        layer: "MCP-TOOL-CALL",
        status: "error",
        tool: toolName,
        durationMs: Date.now() - startedAt,
        error: error instanceof Error ? error.message : String(error),
      });

      throw error;
    }
  };
}
