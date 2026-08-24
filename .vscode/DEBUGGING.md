# VS Code Quick Reference

## ⚠️ This server uses STDIO, not HTTP

There is no `localhost:3000` HTTP endpoint. `test.http` / REST Client won't
work - use the stdio test harness or MCP Inspector instead (below).

## Keyboard Shortcuts for Testing

| Action | Shortcut |
|--------|----------|
| Start Debugger | `F5` |
| Stop Debugger | `Shift+F5` |
| Step Over | `F10` |
| Step Into | `F11` |
| Step Out | `Shift+F11` |
| Toggle Breakpoint | `Ctrl+K Ctrl+B` or click line number |
| Continue (after breakpoint) | `F5` |
| Open Command Palette | `Ctrl+Shift+P` |
| Open Terminal | `` Ctrl+` `` |
| Switch File | `Ctrl+P` then type filename |

## Testing Workflow

### Quick Test (No Revit Required)
```
1. cd server && npm install (first time) && npm run build
2. cd ../tests && npm install (first time)
3. Press F5, choose "Manual Test (stdio, auto-attach to server)"
4. Watch Debug Console for pass/fail summary
5. Set breakpoints in server/src/tools/*.ts before pressing F5 to step through
```

### Interactive Exploration
```
npx @modelcontextprotocol/inspector@latest node server/build/index.js
```
Opens a browser UI to call tools by hand and inspect raw JSON-RPC responses.

### Full Test (With Revit)
```
1. Start Revit
2. Press F5, choose "Manual Test (stdio, auto-attach to server)"
   (or MCP Inspector, calling send_code_to_revit / say_hello)
3. If debugging plugin: open Visual Studio → Debug → Attach to Revit.exe
```

## Common Commands

```bash
# In terminal (Ctrl+`):

# Build TypeScript
cd server && npm run build

# Run stdio test harness
node tests/integration/manual-test.mjs

# Interactive tool explorer
npx @modelcontextprotocol/inspector@latest node server/build/index.js

# Watch logs (JSON Lines - one JSON object per line)
Get-Content -Wait "$env:LOCALAPPDATA\mcp-server-for-revit\logs\revit-activity.jsonl"
```

## Debugging Checklist

❌ **Breakpoint not hitting?**
- [ ] Did you launch via the "Manual Test (stdio, auto-attach to server)" config (not a plain terminal run)?
- [ ] Did you save the file and rebuild (`npm run build` in `server/`)?
- [ ] Is `sourceMaps: true` in `.vscode/launch.json`?

❌ **Server won't start / "Cannot find module"?**
- [ ] Did you run `npm install` in `server/`?
- [ ] Did you run `npm run build` in `server/` after the last edit?
- [ ] Did you run `npm install` in `tests/` (needed for the harness's SDK import)?

❌ **"connect ECONNREFUSED ::1:8080"?**
- This is expected when Revit / the plugin isn't running - the tool call
  still completes at the MCP layer with a handled error.

❌ **Can't attach to Revit in Visual Studio?**
- [ ] Is Revit running?
- [ ] Debug → Attach to Process → search for "Revit"
- [ ] Make sure Visual Studio is elevated (Run as Admin)

## File Navigation

| File | Purpose |
|------|---------|
| `.vscode/launch.json` | Debug configurations (F5) |
| `.vscode/settings.json` | VS Code settings |
| `test.http` | ⚠️ Deprecated - kept for reference only (server has no HTTP endpoint) |
| `tests/integration/manual-test.mjs` | Automated stdio integration test script |
| `tests/README.md` | Full testing documentation |
| `server/src/index.ts` | MCP Server entry point (stdio transport) |
| `server/src/tools/` | Individual tool implementations (auto-registered) |

---

**Pro Tip:** Use Ctrl+Shift+P → "Focus on Debug Console" to see all server
output while debugging. 🚀
