using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace RevitMCP
{
    public class RevitCommandHandler : IExternalEventHandler
    {
        private readonly ConcurrentQueue<PendingCommand> _queue = new ConcurrentQueue<PendingCommand>();

        public string GetName() { return "RevitMCP"; }

        public void Execute(UIApplication uiApp)
        {
            if (uiApp == null) return;
            if (uiApp.ActiveUIDocument == null) return;

            while (_queue.TryDequeue(out PendingCommand cmd))
            {
                try { cmd.Result = RunCommand(uiApp, cmd.Request); }
                catch (Exception ex) { cmd.Result = new Dictionary<string, object> { ["error"] = ex.Message }; }
                finally { cmd.CompletedEvent.Set(); }
            }
        }

        public PendingCommand EnqueueCommand(Dictionary<string, object> request)
        {
            PendingCommand cmd = new PendingCommand(request);
            _queue.Enqueue(cmd);
            return cmd;
        }

        private Dictionary<string, object> RunCommand(UIApplication uiApp, Dictionary<string, object> req)
        {
            Document doc = uiApp.ActiveUIDocument.Document;
            UIDocument uidoc = uiApp.ActiveUIDocument;
            string action = req.ContainsKey("action") ? req["action"].ToString() : "";

            switch (action)
            {
                // READ
                case "get_model_info":      return GetModelInfo(doc);
                case "get_elements":        return GetElements(doc, req);
                case "get_element":         return GetElement(doc, req);
                case "get_rooms":           return GetRooms(doc);
                case "get_levels":          return GetLevels(doc);
                case "get_sheets":          return GetSheets(doc);
                case "get_walls":           return GetWalls(doc);
                case "get_doors":           return GetDoors(doc);
                case "get_windows":         return GetWindows(doc);
                case "get_floors":          return GetFloors(doc);
                case "get_views":           return GetViews(doc);
                case "count_by_category":   return CountByCategory(doc, req);

                // WRITE - Parameters
                case "set_parameter":       return SetParameter(doc, req);
                case "bulk_set_parameter":  return BulkSetParameter(doc, req);

                // WRITE - Geometry
                case "create_wall":         return CreateWall(doc, req);
                case "delete_elements":     return DeleteElements(doc, req);
                case "change_wall_type":    return ChangeWallType(doc, req);
                case "move_element":        return MoveElement(doc, req);
                case "copy_element":        return CopyElement(doc, req);

                // UI - View / Selection
                case "select_element":      return SelectElement(uidoc, req);
                case "select_elements":     return SelectElements(uidoc, req);
                case "highlight_elements":  return HighlightElements(uidoc, req);
                case "zoom_to_element":     return ZoomToElement(uidoc, req);
                case "open_view":           return OpenView(uidoc, req);
                case "isolate_element":     return IsolateElement(uidoc, req);
                case "isolate_category":    return IsolateCategory(uidoc, doc, req);
                case "reset_view":          return ResetView(uidoc);
                case "rename_view":         return RenameView(doc, req);
                case "rename_sheet":        return RenameSheet(doc, req);

                default:
                    return new Dictionary<string, object> { ["error"] = "Unknown action: " + action };
            }
        }

        // ===================== READ =====================

        private Dictionary<string, object> GetModelInfo(Document doc)
        {
            return new Dictionary<string, object>
            {
                ["title"] = doc.Title,
                ["path"] = doc.PathName,
                ["version"] = doc.Application.VersionNumber,
                ["element_count"] = new FilteredElementCollector(doc).WhereElementIsNotElementType().GetElementCount()
            };
        }

        private Dictionary<string, object> GetElements(Document doc, Dictionary<string, object> req)
        {
            string category = req.ContainsKey("category") ? req["category"].ToString() : "";
            var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();

            var elements = collector
                .Where(e => e.Category != null && e.Category.Name.IndexOf(category, StringComparison.OrdinalIgnoreCase) >= 0)
                .Take(200)
                .Select(e => new Dictionary<string, object>
                {
                    ["id"] = e.Id.Value,
                    ["name"] = e.Name,
                    ["category"] = e.Category?.Name
                }).ToList();

            return new Dictionary<string, object> { ["elements"] = elements, ["count"] = elements.Count };
        }

        private Dictionary<string, object> GetElement(Document doc, Dictionary<string, object> req)
        {
            long id = Convert.ToInt64(req["id"]);
            var element = doc.GetElement(new ElementId(id));
            if (element == null) return new Dictionary<string, object> { ["error"] = "Element not found" };

            var parameters = new List<Dictionary<string, string>>();
            foreach (Parameter p in element.Parameters)
            {
                if (p.HasValue)
                    parameters.Add(new Dictionary<string, string>
                    {
                        ["name"] = p.Definition.Name,
                        ["value"] = p.AsValueString() ?? p.AsString() ?? p.AsDouble().ToString(),
                        ["readonly"] = p.IsReadOnly.ToString()
                    });
            }

            return new Dictionary<string, object>
            {
                ["id"] = element.Id.Value,
                ["name"] = element.Name,
                ["category"] = element.Category?.Name,
                ["parameters"] = parameters
            };
        }

        private Dictionary<string, object> GetRooms(Document doc)
        {
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Select(r => new Dictionary<string, object>
                {
                    ["id"] = r.Id.Value,
                    ["name"] = r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? r.Name,
                    ["number"] = r.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "",
                    ["area_sqm"] = Math.Round((r.get_Parameter(BuiltInParameter.ROOM_AREA)?.AsDouble() ?? 0) * 0.092903, 2),
                    ["level"] = r.get_Parameter(BuiltInParameter.ROOM_LEVEL_ID) != null
                        ? doc.GetElement(r.get_Parameter(BuiltInParameter.ROOM_LEVEL_ID).AsElementId())?.Name ?? ""
                        : ""
                }).ToList();

            return new Dictionary<string, object> { ["rooms"] = rooms, ["count"] = rooms.Count };
        }

        private Dictionary<string, object> GetLevels(Document doc)
        {
            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .Select(l => new Dictionary<string, object>
                {
                    ["id"] = l.Id.Value,
                    ["name"] = l.Name,
                    ["elevation_m"] = Math.Round(l.Elevation * 0.3048, 2)
                }).ToList();

            return new Dictionary<string, object> { ["levels"] = levels };
        }

        private Dictionary<string, object> GetSheets(Document doc)
        {
            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Select(s => new Dictionary<string, object>
                {
                    ["id"] = s.Id.Value,
                    ["number"] = s.SheetNumber,
                    ["name"] = s.Name
                }).ToList();

            return new Dictionary<string, object> { ["sheets"] = sheets };
        }

        private Dictionary<string, object> GetWalls(Document doc)
        {
            var walls = new FilteredElementCollector(doc)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .Select(w => new Dictionary<string, object>
                {
                    ["id"] = w.Id.Value,
                    ["type"] = w.WallType.Name,
                    ["length_m"] = Math.Round(w.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble() * 0.3048 ?? 0, 2),
                    ["level"] = doc.GetElement(w.LevelId)?.Name ?? "",
                    ["fire_rating"] = w.get_Parameter(BuiltInParameter.FIRE_RATING)?.AsString() ?? ""
                }).ToList();

            return new Dictionary<string, object> { ["walls"] = walls, ["count"] = walls.Count };
        }

        private Dictionary<string, object> GetDoors(Document doc)
        {
            var doors = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Doors)
                .WhereElementIsNotElementType()
                .Select(d => new Dictionary<string, object>
                {
                    ["id"] = d.Id.Value,
                    ["type"] = d.Name,
                    ["mark"] = d.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "",
                    ["level"] = d.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)?.AsValueString() ?? ""
                }).ToList();

            return new Dictionary<string, object> { ["doors"] = doors, ["count"] = doors.Count };
        }

        private Dictionary<string, object> GetWindows(Document doc)
        {
            var windows = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Windows)
                .WhereElementIsNotElementType()
                .Select(w => new Dictionary<string, object>
                {
                    ["id"] = w.Id.Value,
                    ["type"] = w.Name,
                    ["mark"] = w.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "",
                    ["level"] = w.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)?.AsValueString() ?? ""
                }).ToList();

            return new Dictionary<string, object> { ["windows"] = windows, ["count"] = windows.Count };
        }

        private Dictionary<string, object> GetFloors(Document doc)
        {
            var floors = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Floors)
                .WhereElementIsNotElementType()
                .Select(f => new Dictionary<string, object>
                {
                    ["id"] = f.Id.Value,
                    ["type"] = f.Name,
                    ["area_sqm"] = Math.Round((f.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() ?? 0) * 0.092903, 2)
                }).ToList();

            return new Dictionary<string, object> { ["floors"] = floors, ["count"] = floors.Count };
        }

        private Dictionary<string, object> GetViews(Document doc)
        {
            var views = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate)
                .Select(v => new Dictionary<string, object>
                {
                    ["id"] = v.Id.Value,
                    ["name"] = v.Name,
                    ["type"] = v.ViewType.ToString()
                }).ToList();

            return new Dictionary<string, object> { ["views"] = views, ["count"] = views.Count };
        }

        private Dictionary<string, object> CountByCategory(Document doc, Dictionary<string, object> req)
        {
            string category = req.ContainsKey("category") ? req["category"].ToString() : "";
            int count = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && e.Category.Name.IndexOf(category, StringComparison.OrdinalIgnoreCase) >= 0)
                .Count();

            return new Dictionary<string, object> { ["category"] = category, ["count"] = count };
        }

        // ===================== WRITE - Parameters =====================

        private Dictionary<string, object> SetParameter(Document doc, Dictionary<string, object> req)
        {
            long id = Convert.ToInt64(req["id"]);
            string paramName = req["parameter"].ToString();
            string value = req["value"].ToString();

            var element = doc.GetElement(new ElementId(id));
            if (element == null)
                return new Dictionary<string, object> { ["error"] = "Element not found" };

            using (var tx = new Transaction(doc, "MCP: Set Parameter"))
            {
                tx.Start();
                var param = element.LookupParameter(paramName);
                if (param == null || param.IsReadOnly)
                {
                    tx.RollBack();
                    return new Dictionary<string, object> { ["error"] = "Parameter not found or read-only" };
                }
                param.SetValueString(value);
                tx.Commit();
            }

            return new Dictionary<string, object> { ["success"] = true, ["element_id"] = id };
        }

        private Dictionary<string, object> BulkSetParameter(Document doc, Dictionary<string, object> req)
        {
            string category = req["category"].ToString();
            string paramName = req["parameter"].ToString();
            string value = req["value"].ToString();

            var elements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && e.Category.Name.Equals(category, StringComparison.OrdinalIgnoreCase))
                .ToList();

            int updated = 0;
            using (var tx = new Transaction(doc, "MCP: Bulk Set Parameter"))
            {
                tx.Start();
                foreach (var el in elements)
                {
                    var param = el.LookupParameter(paramName);
                    if (param != null && !param.IsReadOnly)
                    {
                        param.SetValueString(value);
                        updated++;
                    }
                }
                tx.Commit();
            }

            return new Dictionary<string, object> { ["success"] = true, ["updated"] = updated, ["total"] = elements.Count };
        }

        // ===================== WRITE - Geometry =====================

        private Dictionary<string, object> CreateWall(Document doc, Dictionary<string, object> req)
        {
            double x1 = Convert.ToDouble(req["x1"]) / 0.3048;
            double y1 = Convert.ToDouble(req["y1"]) / 0.3048;
            double x2 = Convert.ToDouble(req["x2"]) / 0.3048;
            double y2 = Convert.ToDouble(req["y2"]) / 0.3048;
            long levelId = Convert.ToInt64(req["level_id"]);

            var line = Line.CreateBound(new XYZ(x1, y1, 0), new XYZ(x2, y2, 0));
            var level = doc.GetElement(new ElementId(levelId)) as Level;
            if (level == null)
                return new Dictionary<string, object> { ["error"] = "Level not found" };

            Wall wall;
            using (var tx = new Transaction(doc, "MCP: Create Wall"))
            {
                tx.Start();
                wall = Wall.Create(doc, line, level.Id, false);
                tx.Commit();
            }

            return new Dictionary<string, object> { ["success"] = true, ["wall_id"] = wall.Id.Value };
        }

        private Dictionary<string, object> DeleteElements(Document doc, Dictionary<string, object> req)
        {
            var ids = (req["ids"] as Newtonsoft.Json.Linq.JArray)
                ?.Select(x => new ElementId(Convert.ToInt64(x)))
                .ToList() ?? new List<ElementId>();

            if (ids.Count == 0)
                return new Dictionary<string, object> { ["error"] = "No IDs provided" };

            using (Transaction tx = new Transaction(doc, "MCP: Delete Elements"))
            {
                tx.Start();
                doc.Delete(ids);
                tx.Commit();
            }

            return new Dictionary<string, object> { ["success"] = true, ["deleted_count"] = ids.Count };
        }

        private Dictionary<string, object> ChangeWallType(Document doc, Dictionary<string, object> req)
        {
            long id = Convert.ToInt64(req["id"]);
            string newTypeName = req["new_type"].ToString();

            var wall = doc.GetElement(new ElementId(id)) as Wall;
            if (wall == null)
                return new Dictionary<string, object> { ["error"] = "Wall not found" };

            var newType = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .FirstOrDefault(wt => wt.Name.Equals(newTypeName, StringComparison.OrdinalIgnoreCase));

            if (newType == null)
                return new Dictionary<string, object> { ["error"] = "Wall type not found: " + newTypeName };

            using (var tx = new Transaction(doc, "MCP: Change Wall Type"))
            {
                tx.Start();
                wall.WallType = newType;
                tx.Commit();
            }

            return new Dictionary<string, object> { ["success"] = true, ["wall_id"] = id, ["new_type"] = newType.Name };
        }

        private Dictionary<string, object> MoveElement(Document doc, Dictionary<string, object> req)
        {
            long id = Convert.ToInt64(req["id"]);
            double dx = Convert.ToDouble(req["dx"]) / 0.3048;
            double dy = Convert.ToDouble(req["dy"]) / 0.3048;
            double dz = Convert.ToDouble(req["dz"]) / 0.3048;

            var element = doc.GetElement(new ElementId(id));
            if (element == null)
                return new Dictionary<string, object> { ["error"] = "Element not found" };

            using (var tx = new Transaction(doc, "MCP: Move Element"))
            {
                tx.Start();
                ElementTransformUtils.MoveElement(doc, element.Id, new XYZ(dx, dy, dz));
                tx.Commit();
            }

            return new Dictionary<string, object> { ["success"] = true, ["element_id"] = id };
        }

        private Dictionary<string, object> CopyElement(Document doc, Dictionary<string, object> req)
        {
            long id = Convert.ToInt64(req["id"]);
            double dx = Convert.ToDouble(req["dx"]) / 0.3048;
            double dy = Convert.ToDouble(req["dy"]) / 0.3048;
            double dz = Convert.ToDouble(req["dz"]) / 0.3048;

            var element = doc.GetElement(new ElementId(id));
            if (element == null)
                return new Dictionary<string, object> { ["error"] = "Element not found" };

            ICollection<ElementId> newIds;
            using (var tx = new Transaction(doc, "MCP: Copy Element"))
            {
                tx.Start();
                newIds = ElementTransformUtils.CopyElement(doc, element.Id, new XYZ(dx, dy, dz));
                tx.Commit();
            }

            return new Dictionary<string, object>
            {
                ["success"] = true,
                ["new_ids"] = newIds.Select(eid => eid.Value).ToList()
            };
        }

        // ===================== UI - View / Selection =====================

        private Dictionary<string, object> SelectElement(UIDocument uidoc, Dictionary<string, object> req)
        {
            long id = Convert.ToInt64(req["id"]);
            var elemId = new ElementId(id);
            uidoc.Selection.SetElementIds(new List<ElementId> { elemId });
            uidoc.ShowElements(elemId);
            return new Dictionary<string, object> { ["success"] = true, ["selected"] = id };
        }

        private Dictionary<string, object> SelectElements(UIDocument uidoc, Dictionary<string, object> req)
        {
            var ids = (req["ids"] as Newtonsoft.Json.Linq.JArray)
                ?.Select(x => new ElementId(Convert.ToInt64(x)))
                .ToList() ?? new List<ElementId>();

            uidoc.Selection.SetElementIds(ids);
            return new Dictionary<string, object> { ["success"] = true, ["selected_count"] = ids.Count };
        }

        private Dictionary<string, object> HighlightElements(UIDocument uidoc, Dictionary<string, object> req)
        {
            var ids = (req["ids"] as Newtonsoft.Json.Linq.JArray)
                ?.Select(x => new ElementId(Convert.ToInt64(x)))
                .ToList() ?? new List<ElementId>();

            uidoc.Selection.SetElementIds(ids);
            if (ids.Count > 0) uidoc.ShowElements(ids);
            return new Dictionary<string, object> { ["success"] = true, ["highlighted_count"] = ids.Count };
        }

        private Dictionary<string, object> ZoomToElement(UIDocument uidoc, Dictionary<string, object> req)
        {
            long id = Convert.ToInt64(req["id"]);
            uidoc.ShowElements(new ElementId(id));
            return new Dictionary<string, object> { ["success"] = true, ["zoomed_to"] = id };
        }

        private Dictionary<string, object> OpenView(UIDocument uidoc, Dictionary<string, object> req)
        {
            long id = Convert.ToInt64(req["id"]);
            var view = uidoc.Document.GetElement(new ElementId(id)) as View;
            if (view == null)
                return new Dictionary<string, object> { ["error"] = "View not found" };

            uidoc.ActiveView = view;
            return new Dictionary<string, object> { ["success"] = true, ["view"] = view.Name };
        }

        private Dictionary<string, object> IsolateElement(UIDocument uidoc, Dictionary<string, object> req)
        {
            long id = Convert.ToInt64(req["id"]);
            var view = uidoc.ActiveView;
            view.IsolateElementTemporary(new ElementId(id));
            return new Dictionary<string, object> { ["success"] = true, ["isolated"] = id };
        }

        private Dictionary<string, object> IsolateCategory(UIDocument uidoc, Document doc, Dictionary<string, object> req)
        {
            string category = req["category"].ToString();
            var ids = new FilteredElementCollector(doc, uidoc.ActiveView.Id)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && e.Category.Name.IndexOf(category, StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(e => e.Id).ToList();

            uidoc.ActiveView.IsolateElementsTemporary(ids);
            return new Dictionary<string, object> { ["success"] = true, ["isolated_count"] = ids.Count };
        }

        private Dictionary<string, object> ResetView(UIDocument uidoc)
        {
            uidoc.ActiveView.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
            uidoc.Selection.SetElementIds(new List<ElementId>());
            return new Dictionary<string, object> { ["success"] = true };
        }

        private Dictionary<string, object> RenameView(Document doc, Dictionary<string, object> req)
        {
            long id = Convert.ToInt64(req["id"]);
            string newName = req["name"].ToString();
            var view = doc.GetElement(new ElementId(id)) as View;
            if (view == null) return new Dictionary<string, object> { ["error"] = "View not found" };

            using (var tx = new Transaction(doc, "MCP: Rename View"))
            {
                tx.Start();
                view.Name = newName;
                tx.Commit();
            }

            return new Dictionary<string, object> { ["success"] = true, ["new_name"] = newName };
        }

        private Dictionary<string, object> RenameSheet(Document doc, Dictionary<string, object> req)
        {
            long id = Convert.ToInt64(req["id"]);
            string newName = req["name"].ToString();
            var sheet = doc.GetElement(new ElementId(id)) as ViewSheet;
            if (sheet == null) return new Dictionary<string, object> { ["error"] = "Sheet not found" };

            using (var tx = new Transaction(doc, "MCP: Rename Sheet"))
            {
                tx.Start();
                sheet.Name = newName;
                tx.Commit();
            }

            return new Dictionary<string, object> { ["success"] = true, ["new_name"] = newName };
        }
    }

    public class PendingCommand
    {
        public Dictionary<string, object> Request { get; }
        public Dictionary<string, object> Result { get; set; }
        public ManualResetEventSlim CompletedEvent { get; } = new ManualResetEventSlim(false);

        public PendingCommand(Dictionary<string, object> request)
        {
            Request = request;
        }
    }
}
