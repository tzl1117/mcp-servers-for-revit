import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";
import { withActivityLogging } from "../utils/ActivityLogger.js";

export async function registerTools(server: McpServer) {
  // Intercept every server.tool(...) call so all tool invocations (from any
  // tool file, without touching each one individually) get logged automatically.
  const originalTool = server.tool.bind(server);
  server.tool = ((name: string, ...rest: unknown[]) => {
    const handlerIndex = rest.map((arg) => typeof arg === "function").lastIndexOf(true);
    if (handlerIndex !== -1) {
      rest[handlerIndex] = withActivityLogging(name, rest[handlerIndex] as (...a: unknown[]) => Promise<unknown>);
    }
    return (originalTool as (...a: unknown[]) => unknown)(name, ...rest);
  }) as typeof server.tool;

  // Get the current file directory.
  const __filename = fileURLToPath(import.meta.url);
  const __dirname = path.dirname(__filename);

  // Read all files in the tools directory.
  const files = fs.readdirSync(__dirname);

  // Keep TypeScript and JavaScript files, excluding index and register modules.
  const toolFiles = files.filter(
    (file) =>
      (file.endsWith(".ts") || file.endsWith(".js")) &&
      file !== "index.ts" &&
      file !== "index.js" &&
      file !== "register.ts" &&
      file !== "register.js"
  );

  // Dynamically import and register each tool.
  for (const file of toolFiles) {
    try {
      // Build the import path.
      const importPath = `./${file.replace(/\.(ts|js)$/, ".js")}`;

      // Dynamically import the module.
      const module = await import(importPath);

      // Find and invoke the registration function.
      const registerFunctionName = Object.keys(module).find(
        (key) => key.startsWith("register") && typeof module[key] === "function"
      );

      if (registerFunctionName) {
        module[registerFunctionName](server);
        console.error(`Registered tool: ${file}`);
      } else {
        console.warn(`Warning: no registration function found in ${file}`);
      }
    } catch (error) {
      console.error(`Error registering tool ${file}:`, error);
    }
  }
}
