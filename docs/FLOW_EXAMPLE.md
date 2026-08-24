# Complete Flow Example: Revision Cloud Color Override

This document traces a complete end-to-end flow through the mcp-servers-for-revit architecture using a real-world example: **overriding revision cloud colors in Revit**.

## Scenario

**User Request to Claude:**
> "In the current view, change all revision clouds to red"

**Expected Outcome:**
- All revision clouds in the active view are recolored to red
- Claude reports back how many clouds were modified

---

## Layer-by-Layer Flow

### Layer 1: Claude Desktop

**What Claude Does:**
1. Receives user request: *"change all revision clouds to red"*
2. Understands this requires executing code in Revit
3. Generates C# code that will perform this task
4. Calls the `send_code_to_revit` MCP tool

**Claude's Generated C# Code:**
```csharp
using Autodesk.Revit.DB;
using System.Linq;

Document doc = parameters["document"] as Document;
View activeView = doc.ActiveView;

// Filter all revision clouds in the view
var revisionClouds = new FilteredElementCollector(doc, activeView.Id)
    .OfClass(typeof(RevisionCloud))
    .Cast<RevisionCloud>()
    .ToList();

int modified = 0;
foreach (var cloud in revisionClouds)
{
    try
    {
        // Get the revision cloud's element ID
        ElementId cloudId = cloud.Id;
        
        // Override element color in the view's graphics
        OverrideGraphicsSettings ogs = new OverrideGraphicsSettings();
        ogs.SetProjectionLineColor(new Color(255, 0, 0)); // Red
        
        activeView.SetElementOverrides(cloudId, ogs);
        modified++;
    }
    catch (Exception ex)
    {
        // Log error but continue with other clouds
        continue;
    }
}

return new { success = true, cloudsModified = modified, view = activeView.Name };
```

**Log Entry (Layer 1):**
```
[MCP-CALL] 2026-08-24 10:15:30 | [REQ-001] | Tool: send_code_to_revit
           User: claude-user
           Code: 19 lines of C# (revision cloud override)
           Transaction Mode: auto
```

---

### Layer 2: MCP Server (Node.js)

**Location:** `server/src/tools/send_code_to_revit.ts`

**What Happens:**
1. Receives the tool call from Claude
2. Validates the C# code for basic syntax
3. Creates a JSON-RPC request payload
4. Sends to the Revit plugin via WebSocket connection

**MCP Tool Call Schema:**
```json
{
  "tool": "send_code_to_revit",
  "parameters": {
    "code": "using Autodesk.Revit.DB;...[19 lines of C#]...",
    "parameters": [],
    "transactionMode": "auto"
  }
}
```

**Log Entry (Layer 2):**
```
[TOOL-INVOKE] 2026-08-24 10:15:30 | [REQ-001] | Function: send_code_to_revit()
             Input Validation:
               - Code syntax: ✓ Valid
               - Transaction mode: ✓ 'auto' recognized
               - Parameters: ✓ Empty array (no external params needed)
             Status: PREPARING_REQUEST...
             Status: REQUEST_PREPARED, forwarding to Revit plugin
```

---

### Layer 3: WebSocket Communication

**Direction:** MCP Server → Revit Plugin (localhost:8080)

**JSON-RPC Message Sent:**
```json
{
  "jsonrpc": "2.0",
  "id": "REQ-001",
  "method": "send_code_to_revit",
  "params": {
    "code": "using Autodesk.Revit.DB;\nusing System.Linq;\n\nDocument doc = parameters[\"document\"] as Document;\nView activeView = doc.ActiveView;\n\nvar revisionClouds = new FilteredElementCollector(doc, activeView.Id)\n    .OfClass(typeof(RevisionCloud))\n    .Cast<RevisionCloud>()\n    .ToList();\n\nint modified = 0;\nforeach (var cloud in revisionClouds)\n{\n    try\n    {\n        ElementId cloudId = cloud.Id;\n        OverrideGraphicsSettings ogs = new OverrideGraphicsSettings();\n        ogs.SetProjectionLineColor(new Color(255, 0, 0));\n        activeView.SetElementOverrides(cloudId, ogs);\n        modified++;\n    }\n    catch (Exception ex)\n    {\n        continue;\n    }\n}\n\nreturn new { success = true, cloudsModified = modified, view = activeView.Name };",
    "parameters": [],
    "transactionMode": "auto"
  }
}
```

**Log Entry (Layer 3):**
```
[PORT-COMM] 2026-08-24 10:15:30 | [REQ-001] | WebSocket → Port 8080
           Direction: MCP Server → Revit Plugin
           Method: send_code_to_revit
           Code Size: 847 bytes
           Payload Total: 1.2 KB
           Status: SENDING...
           Delivery: ✓ SUCCESS
           Awaiting response from Revit...
```

---

### Layer 4: Revit Plugin Processing

**Location:** `plugin/Core/SocketService.cs` → `CommandExecutor.cs`

**What Happens:**
1. Plugin receives the JSON-RPC message on port 8080
2. `SocketService.HandleClientCommunication()` reads the message
3. `ProcessJsonRPCRequest()` parses and validates
4. Finds the `send_code_to_revit` command in the registry
5. Prepares the C# code for dynamic compilation

**Code Execution Path:**
```
SocketService.ListenForClients()
  ↓ (new client connection)
SocketService.HandleClientCommunication()
  ↓ (read JSON-RPC message)
SocketService.ProcessJsonRPCRequest()
  ↓ (parse JSON, find command)
CommandRegistry.TryGetCommand("send_code_to_revit", out command)
  ↓ (dispatch to handler)
CommandExecutor.Execute()
  ↓ (compile C# dynamically)
ExternalEventManager.Queue()
  ↓ (wait for Revit UI thread availability)
```

**Log Entry (Layer 4 - Plugin Received):**
```
[REVIT-EXEC] 2026-08-24 10:15:30 | [REQ-001] | Revit Plugin Received
            Command: send_code_to_revit
            Method: send_code_to_revit
            Status: MESSAGE_RECEIVED ✓
            Message Valid: ✓ JSON-RPC format correct
            Status: COMMAND_LOOKUP...
            Command Found: ✓ send_code_to_revit registered
            Status: QUEUING_FOR_EXECUTION...
            Queue Position: 1
            Awaiting Revit UI thread...
```

---

### Layer 5: Revit Execution

**Location:** Plugin compiles and executes C# code on Revit's main thread

**What Happens:**
1. Plugin's code compiler wraps your C# in a template with access to `Document`, `View`, etc.
2. Compiles the wrapped code dynamically (using Roslyn or similar)
3. Executes on Revit's main UI thread via `ExternalEventManager`
4. Within a transaction (because `transactionMode = "auto"`)
5. Your code iterates through revision clouds and applies color overrides

**Wrapped Execution Context:**
```csharp
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;

public class DynamicCommand : IExternalEventHandler
{
    public void Execute(UIApplication uiApp)
    {
        Document doc = uiApp.ActiveUIDocument.Document;
        
        // Start automatic transaction (because transactionMode = "auto")
        using (Transaction tx = new Transaction(doc, "send_code_to_revit"))
        {
            tx.Start();
            try
            {
                // YOUR CODE INSERTED HERE:
                View activeView = doc.ActiveView;
                
                var revisionClouds = new FilteredElementCollector(doc, activeView.Id)
                    .OfClass(typeof(RevisionCloud))
                    .Cast<RevisionCloud>()
                    .ToList();
                
                int modified = 0;
                foreach (var cloud in revisionClouds)
                {
                    try
                    {
                        ElementId cloudId = cloud.Id;
                        OverrideGraphicsSettings ogs = new OverrideGraphicsSettings();
                        ogs.SetProjectionLineColor(new Color(255, 0, 0));
                        activeView.SetElementOverrides(cloudId, ogs);
                        modified++;
                    }
                    catch (Exception ex)
                    {
                        continue;
                    }
                }
                
                this.Result = new { success = true, cloudsModified = modified, view = activeView.Name };
                // END YOUR CODE
                
                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.RollBack();
                this.Result = new { success = false, error = ex.Message };
            }
        }
    }
}
```

**Detailed Execution Log (Layer 5):**
```
[REVIT-EXEC] 2026-08-24 10:15:31 | [REQ-001] | Revit Execution Started
            Thread: Revit UI Main Thread
            Status: COMPILATION...
            Compile Result: ✓ SUCCESS (0 errors)
            
            Status: TRANSACTION_START...
            Transaction: "send_code_to_revit"
            Status: EXECUTING_USER_CODE...
            
            Step 1: Get Active View
              Active View: "Architectural - Level 01" ✓
            
            Step 2: Query Revision Clouds
              Using FilteredElementCollector
              Filter: OfClass(typeof(RevisionCloud))
              Filter: InView(activeView.Id)
              Result: Found 7 revision clouds ✓
            
            Step 3: Iterate and Override
              Cloud #1: ID=445821
                Current Color: Blue (RGB 0, 0, 255)
                Setting to: Red (RGB 255, 0, 0)
                Override Applied: ✓
              
              Cloud #2: ID=445822
                Current Color: Blue (RGB 0, 0, 255)
                Setting to: Red (RGB 255, 0, 0)
                Override Applied: ✓
              
              [... 5 more clouds processed identically ...]
              
              Cloud #7: ID=445827
                Current Color: Blue (RGB 0, 0, 255)
                Setting to: Red (RGB 255, 0, 0)
                Override Applied: ✓
            
            Step 4: Build Result Object
              Result JSON: {
                "success": true,
                "cloudsModified": 7,
                "view": "Architectural - Level 01"
              }
            
            Status: TRANSACTION_COMMIT...
            Changes Persisted: ✓
            
            Status: EXECUTION_COMPLETE
            Duration: 1.8 seconds
            Elements Modified: 7 revision clouds
```

---

### Layer 6: Response from Revit Plugin

**Direction:** Revit Plugin → MCP Server (WebSocket)

**JSON-RPC Response:**
```json
{
  "jsonrpc": "2.0",
  "id": "REQ-001",
  "result": {
    "success": true,
    "cloudsModified": 7,
    "view": "Architectural - Level 01"
  }
}
```

**Log Entry (Layer 6):**
```
[PORT-COMM] 2026-08-24 10:15:32 | [REQ-001] | WebSocket ← Port 8080
           Direction: Revit Plugin → MCP Server
           Status: RESPONSE_RECEIVED ✓
           Response Status: success
           Result Size: 89 bytes
           Payload Total: 156 bytes
           Response Time: 1.8s
           Status: DATA_FORWARDING_TO_MCP...
```

---

### Layer 7: MCP Server Processes Result

**Location:** `server/src/tools/send_code_to_revit.ts` (callback completion)

**What Happens:**
1. Receives the response from Revit plugin
2. Validates the result structure
3. Formats it for Claude
4. Returns to Claude

**Log Entry (Layer 7):**
```
[TOOL-INVOKE] 2026-08-24 10:15:32 | [REQ-001] | Function: send_code_to_revit()
             Status: RESPONSE_RECEIVED ✓
             Result: {
               "success": true,
               "cloudsModified": 7,
               "view": "Architectural - Level 01"
             }
             
             Status: FORMATTING_FOR_CLAUDE...
             Formatted Response: "Code execution successful! Modified 7 revision clouds."
             Status: RETURNING_TO_CLAUDE...
```

---

### Layer 8: Claude Receives and Responds

**What Claude Does:**
1. Receives the result: `cloudsModified: 7, success: true`
2. Interprets the data
3. Formulates a natural language response to the user

**User-Facing Response:**
```
Claude: "Done! I've successfully changed all 7 revision clouds in the view 
'Architectural - Level 01' to red. The changes have been applied to your 
Revit document."
```

**Log Entry (Layer 8):**
```
[RESULT] 2026-08-24 10:15:32 | [REQ-001] | FINAL SUCCESS ✓
         Tool: send_code_to_revit
         Status: COMPLETED
         Elements Changed: 7 revision clouds
         View: "Architectural - Level 01"
         Operation: override_revision_clouds_to_red
         
         Data Size: 156 bytes
         Total Flow Duration: 2.1 seconds
         
         Flow Summary:
           1. Claude generated C# code (✓ 0.1s)
           2. MCP tool invocation (✓ 0.1s)
           3. WebSocket send (✓ 0.01s)
           4. Revit plugin processing (✓ 0.2s)
           5. Revit execution (✓ 1.8s)
           6. Response WebSocket (✓ 0.01s)
           7. MCP formatting (✓ 0.1s)
           8. Claude response (✓ 0.01s)
         
         Final Status: USER SEES RESULT ✓
```

---

## Request ID Tracing

Throughout this entire flow, the request ID **[REQ-001]** was attached to every log entry. This allows you to:

- **Grep the logs** for `REQ-001` to see the complete journey
- **Identify bottlenecks** (which layer took the longest?)
- **Debug failures** (where in the flow did it break?)
- **Correlate errors** across MCP Server and Revit Plugin logs

Example grep:
```powershell
Select-String -Path "*.log" -Pattern "REQ-001"
```

Output shows the complete ordered flow from entry to completion.

---

## Key Takeaways

### 1. **Code is Text**
- Claude generates C# code as a **plain string**
- It's NOT compiled by Claude; it's sent verbatim to Revit

### 2. **Plugin Wraps Your Code**
- The Revit plugin inserts your code into a template
- Provides automatic transaction handling
- Access to `Document`, `View`, and all Revit API objects

### 3. **Visibility is Available**
- Each layer can log what it's doing
- The return value (JSON) tells the MCP server what happened in Revit
- Claude sees the result and can report success/failure

### 4. **Complete Tracing**
- Request IDs allow tracking across distributed components
- Timestamps correlate events
- Structured logging enables debugging

### 5. **Performance**
- Most time spent in Revit (1.8s for 7 element modifications)
- Network overhead minimal (0.01s for WebSocket sends)
- Total round-trip: ~2 seconds for this operation

---

## Variations on This Flow

### Scenario A: Error in User Code
If the C# code has a Revit API error:
```csharp
// This would fail because RevisionCloud doesn't have a SetColor method
cloud.SetColor(new Color(255, 0, 0)); // ❌ Method doesn't exist
```

**Result:**
```json
{
  "jsonrpc": "2.0",
  "id": "REQ-001",
  "error": {
    "code": -32603,
    "message": "Internal error: Method 'SetColor' not found",
    "data": "System.MissingMethodException: ..."
  }
}
```

Claude sees the error and can rewrite the code or explain the limitation.

### Scenario B: Transaction Conflicts
If the Revit document is locked by another operation:
```
[REVIT-EXEC] Transaction.Start() blocked
Error: "Cannot start transaction while another transaction is in progress"
```

The plugin returns an error, Claude retries or reports the issue to the user.

### Scenario C: Custom Parameters
If you pass `parameters` array:
```json
{
  "code": "...",
  "parameters": ["param1", "param2"],
  "transactionMode": "auto"
}
```

Your code can access them:
```csharp
string param1 = parameters["param1"] as string;
```

---

## Logging Implementation

For a production system, you'd log at each layer:

- **MCP Server** (Node.js): Use `winston`, `pino`, or similar
- **Revit Plugin** (C#): Use `log4net` or similar
- **Central Log Aggregation**: Send all logs to a common location with request ID correlation

Example log line template:
```
[TIMESTAMP] [REQUEST-ID] [LAYER] [STATUS] [MESSAGE]
2026-08-24 10:15:30 [REQ-001] [REVIT-EXEC] [INFO] Cloud #1: Color override applied ✓
```

This enables troubleshooting by grepping for a single request ID across all components.
