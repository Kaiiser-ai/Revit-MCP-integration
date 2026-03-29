"""
Revit MCP Server
================
Python MCP server that bridges Claude Desktop to the Revit HTTP add-in.
Translates MCP tool calls into HTTP requests to the Revit add-in running
on localhost:8765.

Requirements: pip install mcp httpx
"""

import asyncio
import json
import httpx
from mcp.server import Server
from mcp.server.stdio import stdio_server
from mcp import types

REVIT_URL = "http://localhost:8765/"


async def call_revit(action: str, **kwargs) -> dict:
    """Send a command to the Revit HTTP add-in."""
    async with httpx.AsyncClient(timeout=20.0) as client:
        r = await client.post(REVIT_URL, json={"action": action, **kwargs})
        return r.json()


server = Server("revit-mcp")


@server.list_tools()
async def list_tools() -> list[types.Tool]:
    return [
        # ── READ ──
        types.Tool(
            name="get_model_info",
            description="Get active Revit model title, path, version and element count",
            inputSchema={"type": "object", "properties": {}}
        ),
        types.Tool(
            name="get_elements",
            description="Get elements, optionally filtered by category (e.g. 'Walls', 'Doors')",
            inputSchema={"type": "object", "properties": {"category": {"type": "string"}}}
        ),
        types.Tool(
            name="get_element",
            description="Get all parameters of a specific element by ID",
            inputSchema={"type": "object", "properties": {"id": {"type": "integer"}}, "required": ["id"]}
        ),
        types.Tool(
            name="get_rooms",
            description="Get all rooms with name, number and area in sqm",
            inputSchema={"type": "object", "properties": {}}
        ),
        types.Tool(
            name="get_levels",
            description="Get all levels sorted by elevation in meters",
            inputSchema={"type": "object", "properties": {}}
        ),
        types.Tool(
            name="get_sheets",
            description="Get all drawing sheets",
            inputSchema={"type": "object", "properties": {}}
        ),
        types.Tool(
            name="get_walls",
            description="Get all walls with type, length, level and fire rating",
            inputSchema={"type": "object", "properties": {}}
        ),
        types.Tool(
            name="get_doors",
            description="Get all doors with mark and level",
            inputSchema={"type": "object", "properties": {}}
        ),
        types.Tool(
            name="get_windows",
            description="Get all windows with mark and level",
            inputSchema={"type": "object", "properties": {}}
        ),
        types.Tool(
            name="get_floors",
            description="Get all floors with area",
            inputSchema={"type": "object", "properties": {}}
        ),
        types.Tool(
            name="get_views",
            description="Get all views in the model",
            inputSchema={"type": "object", "properties": {}}
        ),
        types.Tool(
            name="count_by_category",
            description="Count elements in a category (e.g. 'Walls', 'Doors')",
            inputSchema={"type": "object", "properties": {"category": {"type": "string"}}, "required": ["category"]}
        ),

        # ── WRITE - Parameters ──
        types.Tool(
            name="set_parameter",
            description="Set a parameter on a specific element by ID",
            inputSchema={
                "type": "object",
                "properties": {
                    "id": {"type": "integer"},
                    "parameter": {"type": "string"},
                    "value": {"type": "string"}
                },
                "required": ["id", "parameter", "value"]
            }
        ),
        types.Tool(
            name="bulk_set_parameter",
            description="Set a parameter on ALL elements of a category at once",
            inputSchema={
                "type": "object",
                "properties": {
                    "category": {"type": "string"},
                    "parameter": {"type": "string"},
                    "value": {"type": "string"}
                },
                "required": ["category", "parameter", "value"]
            }
        ),

        # ── WRITE - Geometry ──
        types.Tool(
            name="create_wall",
            description="Create a wall between two points. Coordinates in meters.",
            inputSchema={
                "type": "object",
                "properties": {
                    "x1": {"type": "number"}, "y1": {"type": "number"},
                    "x2": {"type": "number"}, "y2": {"type": "number"},
                    "level_id": {"type": "integer"}
                },
                "required": ["x1", "y1", "x2", "y2", "level_id"]
            }
        ),
        types.Tool(
            name="delete_elements",
            description="Permanently delete elements from Revit by ID list",
            inputSchema={
                "type": "object",
                "properties": {"ids": {"type": "array", "items": {"type": "integer"}}},
                "required": ["ids"]
            }
        ),
        types.Tool(
            name="change_wall_type",
            description="Change the type of a wall by ID.",
            inputSchema={
                "type": "object",
                "properties": {"id": {"type": "integer"}, "new_type": {"type": "string"}},
                "required": ["id", "new_type"]
            }
        ),
        types.Tool(
            name="move_element",
            description="Move an element by offset in meters (dx, dy, dz)",
            inputSchema={
                "type": "object",
                "properties": {
                    "id": {"type": "integer"},
                    "dx": {"type": "number"}, "dy": {"type": "number"}, "dz": {"type": "number"}
                },
                "required": ["id", "dx", "dy", "dz"]
            }
        ),
        types.Tool(
            name="copy_element",
            description="Copy an element with offset in meters (dx, dy, dz)",
            inputSchema={
                "type": "object",
                "properties": {
                    "id": {"type": "integer"},
                    "dx": {"type": "number"}, "dy": {"type": "number"}, "dz": {"type": "number"}
                },
                "required": ["id", "dx", "dy", "dz"]
            }
        ),

        # ── UI - View / Selection ──
        types.Tool(
            name="select_element",
            description="Select and zoom to a single element in Revit",
            inputSchema={"type": "object", "properties": {"id": {"type": "integer"}}, "required": ["id"]}
        ),
        types.Tool(
            name="select_elements",
            description="Select multiple elements in Revit by ID list",
            inputSchema={
                "type": "object",
                "properties": {"ids": {"type": "array", "items": {"type": "integer"}}},
                "required": ["ids"]
            }
        ),
        types.Tool(
            name="highlight_elements",
            description="Highlight multiple elements in Revit by ID list",
            inputSchema={
                "type": "object",
                "properties": {"ids": {"type": "array", "items": {"type": "integer"}}},
                "required": ["ids"]
            }
        ),
        types.Tool(
            name="zoom_to_element",
            description="Zoom Revit view to a specific element",
            inputSchema={"type": "object", "properties": {"id": {"type": "integer"}}, "required": ["id"]}
        ),
        types.Tool(
            name="open_view",
            description="Open a specific view in Revit by view ID",
            inputSchema={"type": "object", "properties": {"id": {"type": "integer"}}, "required": ["id"]}
        ),
        types.Tool(
            name="isolate_element",
            description="Isolate a single element in the current Revit view",
            inputSchema={"type": "object", "properties": {"id": {"type": "integer"}}, "required": ["id"]}
        ),
        types.Tool(
            name="isolate_category",
            description="Isolate all elements of a category in current view (e.g. 'Walls')",
            inputSchema={"type": "object", "properties": {"category": {"type": "string"}}, "required": ["category"]}
        ),
        types.Tool(
            name="reset_view",
            description="Reset the view - clear all isolations and selections in Revit",
            inputSchema={"type": "object", "properties": {}}
        ),
        types.Tool(
            name="rename_view",
            description="Rename a view by ID",
            inputSchema={
                "type": "object",
                "properties": {"id": {"type": "integer"}, "name": {"type": "string"}},
                "required": ["id", "name"]
            }
        ),
        types.Tool(
            name="rename_sheet",
            description="Rename a sheet by ID",
            inputSchema={
                "type": "object",
                "properties": {"id": {"type": "integer"}, "name": {"type": "string"}},
                "required": ["id", "name"]
            }
        ),
    ]


@server.call_tool()
async def call_tool(name: str, arguments: dict) -> list[types.TextContent]:
    try:
        # READ
        if   name == "get_model_info":      result = await call_revit("get_model_info")
        elif name == "get_elements":        result = await call_revit("get_elements", category=arguments.get("category", ""))
        elif name == "get_element":         result = await call_revit("get_element", id=arguments["id"])
        elif name == "get_rooms":           result = await call_revit("get_rooms")
        elif name == "get_levels":          result = await call_revit("get_levels")
        elif name == "get_sheets":          result = await call_revit("get_sheets")
        elif name == "get_walls":           result = await call_revit("get_walls")
        elif name == "get_doors":           result = await call_revit("get_doors")
        elif name == "get_windows":         result = await call_revit("get_windows")
        elif name == "get_floors":          result = await call_revit("get_floors")
        elif name == "get_views":           result = await call_revit("get_views")
        elif name == "count_by_category":   result = await call_revit("count_by_category", category=arguments["category"])

        # WRITE - Parameters
        elif name == "set_parameter":       result = await call_revit("set_parameter", id=arguments["id"], parameter=arguments["parameter"], value=arguments["value"])
        elif name == "bulk_set_parameter":  result = await call_revit("bulk_set_parameter", category=arguments["category"], parameter=arguments["parameter"], value=arguments["value"])

        # WRITE - Geometry
        elif name == "create_wall":         result = await call_revit("create_wall", x1=arguments["x1"], y1=arguments["y1"], x2=arguments["x2"], y2=arguments["y2"], level_id=arguments["level_id"])
        elif name == "delete_elements":     result = await call_revit("delete_elements", ids=arguments["ids"])
        elif name == "change_wall_type":    result = await call_revit("change_wall_type", id=arguments["id"], new_type=arguments["new_type"])
        elif name == "move_element":        result = await call_revit("move_element", id=arguments["id"], dx=arguments["dx"], dy=arguments["dy"], dz=arguments["dz"])
        elif name == "copy_element":        result = await call_revit("copy_element", id=arguments["id"], dx=arguments["dx"], dy=arguments["dy"], dz=arguments["dz"])

        # UI - View / Selection
        elif name == "select_element":      result = await call_revit("select_element", id=arguments["id"])
        elif name == "select_elements":     result = await call_revit("select_elements", ids=arguments["ids"])
        elif name == "highlight_elements":  result = await call_revit("highlight_elements", ids=arguments["ids"])
        elif name == "zoom_to_element":     result = await call_revit("zoom_to_element", id=arguments["id"])
        elif name == "open_view":           result = await call_revit("open_view", id=arguments["id"])
        elif name == "isolate_element":     result = await call_revit("isolate_element", id=arguments["id"])
        elif name == "isolate_category":    result = await call_revit("isolate_category", category=arguments["category"])
        elif name == "reset_view":          result = await call_revit("reset_view")
        elif name == "rename_view":         result = await call_revit("rename_view", id=arguments["id"], name=arguments["name"])
        elif name == "rename_sheet":        result = await call_revit("rename_sheet", id=arguments["id"], name=arguments["name"])
        else:                               result = {"error": "Unknown tool: " + name}
    except Exception as e:
        result = {"error": str(e)}

    return [types.TextContent(type="text", text=json.dumps(result, indent=2))]


async def main():
    async with stdio_server() as (read_stream, write_stream):
        await server.run(read_stream, write_stream, server.create_initialization_options())


if __name__ == "__main__":
    asyncio.run(main())
