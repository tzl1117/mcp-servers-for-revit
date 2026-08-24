import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { randomUUID } from "crypto";
import { getLogFilePath, logActivity } from "../utils/activityLogger.js";

/**
 * Log action tool - Claude calls this to log intent before executing other MCP tools
 * Creates audit trail of all Claude decisions and tool calls
 */
export function registerLogActionTool(server: McpServer) {
  server.tool(
    "log_action",
    "Log Claude's intent and reasoning before executing tools. Creates audit trail of all actions with tracing via requestId.",
    {
      action: z
        .string()
        .describe("What you are about to do (e.g., 'Override revision cloud colors in active view')"),
      reason: z
        .string()
        .describe("Why you are doing this (e.g., 'User requested visual distinction of recent design changes')"),
      code_summary: z
        .string()
        .optional()
        .describe("Optional summary of the code logic (e.g., 'Filter revision clouds by date, apply red color override')"),
      requestId: z
        .string()
        .optional()
        .describe("Optional request ID for tracing. Generated automatically if not provided."),
    },
    async (args, extra) => {
      try {
        // Generate requestId if not provided
        const requestId = args.requestId || randomUUID();

        // Get timestamp
        const timestamp = new Date().toISOString();
        const logFile = getLogFilePath();

        logActivity({
          timestamp,
          requestId,
          action: args.action,
          reason: args.reason,
          code_summary: args.code_summary || null,
          layer: "CLAUDE-INTENT",
          status: "logged",
        });

        return {
          content: [
            {
              type: "text",
              text: JSON.stringify(
                {
                  requestId,
                  status: "logged",
                  timestamp,
                  action: args.action,
                  logFile,
                },
                null,
                2
              ),
            },
          ],
        };
      } catch (error) {
        return {
          content: [
            {
              type: "text",
              text: `Log action failed: ${error instanceof Error ? error.message : String(error)}`,
            },
          ],
        };
      }
    }
  );
}
