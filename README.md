# Claude-to-Revit MCP Integration

A bidirectional integration that connects **Claude AI** to **Autodesk Revit** through the **Model Context Protocol (MCP)**, enabling architects to interact with live BIM models using natural language.

> **"Find all rooms on Level 2 and list their areas"** → Claude queries Revit and returns structured data  
> **"Delete the temporary partition in Room 204"** → Claude executes the modification in the live model

![Architecture](docs/architecture.png)

## Architecture

The system comprises three components connected in series:

```
┌─────────────────┐     stdio      ┌─────────────────┐    HTTP POST     ┌─────────────────┐
│  Claude Desktop  │◄─────────────►│  Python MCP      │◄───────────────►│  Revit C# Add-in │
│  (LLM Client)   │    MCP Tools   │  Server          │   localhost:8765 │  (HTTP Server)   │
└─────────────────┘                └─────────────────┘                  └─────────────────┘
                                                                              │
                                                                        Revit API
                                                                        (ExternalEvent)
                                                                              │
                                                                     ┌────────────────┐
                                                                     │   Live Revit    │
                                                                     │   BIM Model     │
                                                                     └────────────────┘
```

1. **C# Revit Add-in** — Runs inside Revit as a local HTTP server on `localhost:8765`. Uses `IExternalEventHandler` and `ExternalEvent` for thread-safe execution on Revit's main thread. Exposes model operations (read, write, UI) as JSON endpoints.

2. **Python MCP Server** — Translates MCP tool calls from Claude into HTTP requests to the Revit add-in. Runs via stdio, registered in Claude Desktop's config.

3. **Claude Desktop** — The LLM client. Receives natural language instructions from the architect, reasons about which tools to invoke, and executes actions through the MCP server.

## Features

### Read Operations
| Tool | Description |
|------|-------------|
| `get_model_info` | Model title, path, Revit version, element count |
| `get_elements` | Elements filtered by category |
| `get_element` | All parameters of a specific element |
| `get_rooms` | Rooms with name, number, area (sqm), level |
| `get_levels` | Levels sorted by elevation (meters) |
| `get_walls` | Walls with type, length, level, fire rating |
| `get_doors` | Doors with mark and level |
| `get_windows` | Windows with mark and level |
| `get_floors` | Floors with area |
| `get_sheets` | Drawing sheets |
| `get_views` | All views in the model |
| `count_by_category` | Count elements by category |

### Write Operations
| Tool | Description |
|------|-------------|
| `set_parameter` | Set a parameter on a specific element |
| `bulk_set_parameter` | Set a parameter on all elements of a category |
| `create_wall` | Create a wall between two points (meters) |
| `delete_elements` | Delete elements by ID list |
| `change_wall_type` | Change wall type by ID |
| `move_element` | Move element by offset (meters) |
| `copy_element` | Copy element with offset (meters) |

### UI / View Operations
| Tool | Description |
|------|-------------|
| `select_element` | Select and zoom to an element |
| `select_elements` | Select multiple elements |
| `highlight_elements` | Highlight and show elements |
| `zoom_to_element` | Zoom to a specific element |
| `open_view` | Open a view by ID |
| `isolate_element` | Isolate element in current view |
| `isolate_category` | Isolate all elements of a category |
| `reset_view` | Clear isolations and selections |
| `rename_view` | Rename a view |
| `rename_sheet` | Rename a sheet |

## Prerequisites

- **Autodesk Revit 2024+** (tested on Revit 2024)
- **Visual Studio 2022** with .NET Framework 4.8 targeting
- **Python 3.10+** with `mcp` and `httpx` packages
- **Claude Desktop** ([download](https://claude.ai/downloads))
- **Newtonsoft.Json** NuGet package (for the C# add-in)

## Installation

### 1. Build the Revit Add-in

1. Open Visual Studio → Create a new **Class Library (.NET Framework 4.8)** project named `RevitMCP`
2. Add NuGet package: `Newtonsoft.Json`
3. Add references to Revit API DLLs:
   - `RevitAPI.dll` (typically at `C:\Program Files\Autodesk\Revit 2024\`)
   - `RevitAPIUI.dll`
   - Set **Copy Local = False** for both
4. Copy `src/RevitMCP/App.cs` and `src/RevitMCP/RevitCommandHandler.cs` into the project
5. Build the solution (**Ctrl+Shift+B**)
6. Copy the output `RevitMCP.dll` to:
   ```
   C:\Users\<username>\AppData\Roaming\Autodesk\Revit\Addins\2024\
   ```
7. Copy `config/RevitMCP.addin` to the same folder

### 2. Set Up the Python MCP Server

```bash
pip install mcp httpx
```

Place `src/MCP-Server/revit_mcp_server.py` in a known location (e.g., your Desktop or a project folder).

### 3. Configure Claude Desktop

Edit `claude_desktop_config.json` at:
```
Windows: %APPDATA%\Claude\claude_desktop_config.json
macOS:   ~/Library/Application Support/Claude/claude_desktop_config.json
```

Add the MCP server configuration:
```json
{
  "mcpServers": {
    "revit": {
      "command": "python",
      "args": [
        "C:\\path\\to\\revit_mcp_server.py"
      ]
    }
  }
}
```

### 4. Launch

1. Open **Revit** and load a project
2. Restart **Claude Desktop**
3. Verify the Revit MCP tools appear in Claude's tool list
4. Start issuing natural language commands

## Usage Examples

```
"How many walls do I have on each level?"
"Select all exterior walls and show them to me"
"What is the total area of rooms on Level 2?"
"Delete the padel court elements"
"Change all WA_Int_Solid-Block_150 walls to WA_Int_Solid-Block_300"
"Rename sheet A101 to 'Ground Floor Plan - Revised'"
"Isolate all doors in the current view"
```

## Safety & Ethical Considerations

This system gives an AI the ability to **modify a live BIM production model**. Responsible deployment requires careful attention to ethics, governance, and risk management.

### Risk Classification (EU AI Act)

Under the EU AI Act risk-based framework, this system qualifies as **high-risk** because it directly impacts safety-critical building elements such as structural walls, fire egress components, and MEP systems. High-risk classification mandates formal risk management, technical documentation, transparency obligations, and human oversight throughout the system's lifecycle.

### Hallucination Risk

LLM hallucination — where the AI generates plausible but incorrect outputs — is the primary ethical concern. Unlike text-based assistants where a wrong answer stays on screen, a hallucination in this system translates into a **physical modification** to a shared production model. The AI may misidentify elements, fabricate parameter values, or misinterpret ambiguous instructions, potentially corrupting coordination workflows across disciplines.

The risk is further amplified by **inconsistent BIM data**, where poor naming conventions or missing parameters increase the likelihood of misinterpretation by the AI system.

### Mitigation Layers

- **Technical safeguards** — `IExternalEventHandler` ensures thread-safe execution on Revit's main thread. The HTTP server runs on `localhost:8765` only. A confirmation step is enforced before destructive operations (delete, modify), presenting element ID, name, and category for architect approval.
- **Governance & compliance** — All AI-initiated model changes should be logged with timestamps and element IDs for full auditability. A **DPIA (Data Protection Impact Assessment)** or **FRIA (Fundamental Rights Impact Assessment)** should be conducted prior to deployment to evaluate potential risks to safety and project stakeholders.
- **Human oversight** — The architect must initiate every interaction and retains full override authority. The system is designed as an intelligent assistant, not an autonomous agent. All AI-driven modifications should be isolated in a dedicated Revit workset or design option before merging into the shared central model.
- **Transparency & accountability** — Users must be informed that AI is generating model modifications. The responsible professional (architect/BIM manager) remains accountable for all changes applied to the production model, regardless of whether they were AI-assisted.
- **Data privacy** — BIM models contain proprietary project data including design intent, client information, and commercial specifications. The localhost-only architecture ensures no model data leaves the local machine. No data is sent to external APIs.

> ⚠️ **In safety-critical AECO environments, AI must remain a controlled tool under professional supervision — never an autonomous decision-maker.**

## Project Structure

```
revit-mcp-integration/
├── README.md
├── LICENSE
├── src/
│   ├── RevitMCP/                    # C# Revit Add-in
│   │   ├── App.cs                   # Entry point, HTTP listener, ExternalEvent
│   │   └── RevitCommandHandler.cs   # All model operations (read/write/UI)
│   └── MCP-Server/                  # Python MCP Server
│       ├── revit_mcp_server.py      # MCP tool definitions and routing
│       └── requirements.txt         # Python dependencies
├── config/
│   ├── RevitMCP.addin               # Revit add-in manifest
│   └── claude_desktop_config.example.json
└── docs/
    └── architecture.png             # Architecture diagram
```

## Tech Stack

- **C# / .NET Framework 4.8** — Revit add-in with HTTP listener
- **Revit API** — `IExternalEventHandler`, `ExternalEvent`, `FilteredElementCollector`, `Transaction`
- **Python** — MCP server using `mcp` SDK and `httpx`
- **Model Context Protocol (MCP)** — Anthropic's open standard for LLM-tool integration
- **Claude Desktop** — LLM client by Anthropic

## Author

**Mohamad Jaber**  
Senior Architect & BIM Manager | MSc in AI for Construction Engineering (MAICEN)  
[LinkedIn](https://linkedin.com/in/mohamad-jaber) · [GitHub](https://github.com/Kaiiser-ai)

## License

MIT License — see [LICENSE](LICENSE) for details.
