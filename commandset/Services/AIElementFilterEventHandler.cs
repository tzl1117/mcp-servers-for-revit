using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using RevitMCPSDK.API.Interfaces;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace RevitMCPCommandSet.Services
{
    public class AIElementFilterEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication uiApp;
        private UIDocument uiDoc => uiApp.ActiveUIDocument;
        private Document doc => uiDoc.Document;
        private Autodesk.Revit.ApplicationServices.Application app => uiApp.Application;
        /// <summary>
        /// </summary>
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);
        /// <summary>
        /// </summary>
        public FilterSetting FilterSetting { get; private set; }
        /// <summary>
        /// </summary>
        public AIResult<List<object>> Result { get; private set; }

        /// <summary>
        /// </summary>
        public void SetParameters(FilterSetting data)
        {
            FilterSetting = data;
            _resetEvent.Reset();
        }
        public void Execute(UIApplication uiapp)
        {
            uiApp = uiapp;

            try
            {
                var elementInfoList = new List<object>();
                if (!FilterSetting.Validate(out string errorMessage))
                    throw new Exception(errorMessage);
                var elementList = GetFilteredElements(doc, FilterSetting);
                if (elementList == null || !elementList.Any())
                    throw new Exception("未在项目中找到指定元素，请检查过滤器设置是否正确");
                string message = "";
                if (FilterSetting.MaxElements > 0)
                {
                    if (elementList.Count > FilterSetting.MaxElements)
                    {
                        elementList = elementList.Take(FilterSetting.MaxElements).ToList();
                        message = $"。此外，符合过滤条件的共有 {elementList.Count} 个元素，仅显示前 {FilterSetting.MaxElements} 个";
                    }
                }

                elementInfoList = GetElementFullInfo(doc, elementList);

                Result = new AIResult<List<object>>
                {
                    Success = true,
                    Message = $"成功获取{elementInfoList.Count}个元素信息，具体信息储存在Response属性中"+ message,
                    Response = elementInfoList,
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<List<object>>
                {
                    Success = false,
                    Message = $"获取元素信息时出错: {ex.Message}",
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        /// <summary>
        /// </summary>
        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        /// <summary>
        /// </summary>
        public string GetName()
        {
            return "获取元素信息";
        }

        /// <summary>
        /// </summary>
        public static IList<Element> GetFilteredElements(Document doc, FilterSetting settings)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (!settings.Validate(out string errorMessage))
            {
                System.Diagnostics.Trace.WriteLine($"过滤器设置无效: {errorMessage}");
                return new List<Element>();
            }
            List<string> appliedFilters = new List<string>();
            List<Element> result = new List<Element>();
            if (settings.IncludeTypes && settings.IncludeInstances)
            {
                result.AddRange(GetElementsByKind(doc, settings, true, appliedFilters));

                result.AddRange(GetElementsByKind(doc, settings, false, appliedFilters));
            }
            else if (settings.IncludeInstances)
            {
                result = GetElementsByKind(doc, settings, false, appliedFilters);
            }
            else if (settings.IncludeTypes)
            {
                result = GetElementsByKind(doc, settings, true, appliedFilters);
            }

            if (appliedFilters.Count > 0)
            {
                System.Diagnostics.Trace.WriteLine($"已应用 {appliedFilters.Count} 个过滤条件: {string.Join(", ", appliedFilters)}");
                System.Diagnostics.Trace.WriteLine($"最终筛选结果: 共找到 {result.Count} 个元素");
            }
            return result;

        }

        /// <summary>
        /// </summary>
        private static List<Element> GetElementsByKind(Document doc, FilterSetting settings, bool isElementType, List<string> appliedFilters)
        {
            FilteredElementCollector collector;
            if (!isElementType && settings.FilterVisibleInCurrentView && doc.ActiveView != null)
            {
                collector = new FilteredElementCollector(doc, doc.ActiveView.Id);
                appliedFilters.Add("当前视图可见元素");
            }
            else
            {
                collector = new FilteredElementCollector(doc);
            }
            if (isElementType)
            {
                collector = collector.WhereElementIsElementType();
                appliedFilters.Add("仅元素类型");
            }
            else
            {
                collector = collector.WhereElementIsNotElementType();
                appliedFilters.Add("仅元素实例");
            }
            List<ElementFilter> filters = new List<ElementFilter>();
            if (!string.IsNullOrWhiteSpace(settings.FilterCategory))
            {
                BuiltInCategory category;
                if (!Enum.TryParse(settings.FilterCategory, true, out category))
                {
                    throw new ArgumentException($"无法将 '{settings.FilterCategory}' 转换为有效的Revit类别。");
                }
                ElementCategoryFilter categoryFilter = new ElementCategoryFilter(category);
                filters.Add(categoryFilter);
                appliedFilters.Add($"类别：{settings.FilterCategory}");
            }
            if (!string.IsNullOrWhiteSpace(settings.FilterElementType))
            {

                Type elementType = null;
                string[] possibleTypeNames = new string[]
                {
                    settings.FilterElementType,
                    $"Autodesk.Revit.DB.{settings.FilterElementType}, RevitAPI",
                    $"{settings.FilterElementType}, RevitAPI"
                };
                foreach (string typeName in possibleTypeNames)
                {
                    elementType = Type.GetType(typeName);
                    if (elementType != null)
                        break;
                }
                if (elementType != null)
                {
                    ElementClassFilter classFilter = new ElementClassFilter(elementType);
                    filters.Add(classFilter);
                    appliedFilters.Add($"元素类型：{elementType.Name}");
                }
                else
                {
                    throw new Exception($"警告：无法找到类型 '{settings.FilterElementType}'");
                }
            }
            if (!isElementType && settings.FilterFamilySymbolId > 0)
            {
                ElementId symbolId = new ElementId(settings.FilterFamilySymbolId);
                Element symbolElement = doc.GetElement(symbolId);
                if (symbolElement != null && symbolElement is FamilySymbol)
                {
                    FamilyInstanceFilter familyFilter = new FamilyInstanceFilter(doc, symbolId);
                    filters.Add(familyFilter);
                    FamilySymbol symbol = symbolElement as FamilySymbol;
                    string familyName = symbol.Family?.Name ?? "未知族";
                    string symbolName = symbol.Name ?? "未知类型";
                    appliedFilters.Add($"族类型：{familyName} - {symbolName} (ID: {settings.FilterFamilySymbolId})");
                }
                else
                {
                    string elementType = symbolElement != null ? symbolElement.GetType().Name : "不存在";
                    System.Diagnostics.Trace.WriteLine($"警告：ID为 {settings.FilterFamilySymbolId} 的元素{(symbolElement == null ? "不存在" : "不是有效的FamilySymbol")} (实际类型: {elementType})");
                }
            }
            if (settings.BoundingBoxMin != null && settings.BoundingBoxMax != null)
            {
                XYZ minXYZ = JZPoint.ToXYZ(settings.BoundingBoxMin);
                XYZ maxXYZ = JZPoint.ToXYZ(settings.BoundingBoxMax);
                Outline outline = new Outline(minXYZ, maxXYZ);
                BoundingBoxIntersectsFilter boundingBoxFilter = new BoundingBoxIntersectsFilter(outline);
                filters.Add(boundingBoxFilter);
                appliedFilters.Add($"空间范围过滤：Min({settings.BoundingBoxMin.X:F2}, {settings.BoundingBoxMin.Y:F2}, {settings.BoundingBoxMin.Z:F2}), " +
                                  $"Max({settings.BoundingBoxMax.X:F2}, {settings.BoundingBoxMax.Y:F2}, {settings.BoundingBoxMax.Z:F2}) mm");
            }
            if (filters.Count > 0)
            {
                ElementFilter combinedFilter = filters.Count == 1
                    ? filters[0]
                    : new LogicalAndFilter(filters);
                collector = collector.WherePasses(combinedFilter);
                if (filters.Count > 1)
                {
                    System.Diagnostics.Trace.WriteLine($"应用了{filters.Count}个过滤条件的组合过滤器 (逻辑AND关系)");
                }
            }
            return collector.ToElements().ToList();
        }

        /// <summary>
        /// </summary>
        public static List<object> GetElementFullInfo(Document doc, IList<Element> elementCollector)
        {
            List<object> infoList = new List<object>();

            foreach (var element in elementCollector)
            {
                if (element?.Category?.HasMaterialQuantities ?? false)
                {
                    var info = CreateElementFullInfo(doc, element);
                    if (info != null)
                    {
                        infoList.Add(info);
                    }
                }
                else if (element is ElementType elementType)
                {
                    var info = CreateTypeFullInfo(doc, elementType);
                    if (info != null)
                    {
                        infoList.Add(info);
                    }
                }
                else if (element is Level || element is Grid)
                {
                    var info = CreatePositioningElementInfo(doc, element);
                    if (info != null)
                    {
                        infoList.Add(info);
                    }
                }
                else if (element is SpatialElement)
                {
                    var info = CreateSpatialElementInfo(doc, element);
                    if (info != null)
                    {
                        infoList.Add(info);
                    }
                }
                else if (element is View)
                {
                    var info = CreateViewInfo(doc, element);
                    if (info != null)
                    {
                        infoList.Add(info);
                    }
                }
                else if (element is TextNote || element is Dimension ||
                         element is IndependentTag || element is AnnotationSymbol ||
                         element is SpotDimension)
                {
                    var info = CreateAnnotationInfo(doc, element);
                    if (info != null)
                    {
                        infoList.Add(info);
                    }
                }
                else if (element is Group || element is RevitLinkInstance)
                {
                    var info = CreateGroupOrLinkInfo(doc, element);
                    if (info != null)
                    {
                        infoList.Add(info);
                    }
                }
                else
                {
                    var info = CreateElementBasicInfo(doc, element);
                    if (info != null)
                    {
                        infoList.Add(info);
                    }
                }
            }

            return infoList;
        }

        /// <summary>
        /// </summary>
        public static ElementInstanceInfo CreateElementFullInfo(Document doc, Element element)
        {
            try
            {
                if (element?.Category == null)
                    return null;

                ElementInstanceInfo elementInfo = new ElementInstanceInfo();
                // ID
                elementInfo.Id = element.Id.GetIntValue();
                // UniqueId
                elementInfo.UniqueId = element.UniqueId;
                elementInfo.Name = element.Name;
                elementInfo.FamilyName = element?.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString();
                elementInfo.Category = element.Category.Name;
                elementInfo.BuiltInCategory = Enum.GetName(typeof(BuiltInCategory), element.Category.Id.GetIntValue());
                elementInfo.TypeId = element.GetTypeId().GetIntValue();
                if (element is FamilyInstance instance)
                    elementInfo.RoomId = instance.Room?.Id.GetIntValue() ?? -1;
                elementInfo.Level = GetElementLevel(doc, element);
                BoundingBoxInfo boundingBoxInfo = new BoundingBoxInfo();
                elementInfo.BoundingBox = GetBoundingBoxInfo(element);
                //elementInfo.Parameters = GetDimensionParameters(element);
                ParameterInfo thicknessParam = GetThicknessInfo(element);
                if (thicknessParam != null)
                {
                    elementInfo.Parameters.Add(thicknessParam);
                }
                ParameterInfo heightParam = GetBoundingBoxHeight(elementInfo.BoundingBox);
                if (heightParam != null)
                {
                    elementInfo.Parameters.Add(heightParam);
                }

                return elementInfo;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// </summary>
        /// <param name="doc"></param>
        /// <param name="elementType"></param>
        /// <returns></returns>
        public static ElementTypeInfo CreateTypeFullInfo(Document doc, ElementType elementType)
        {
            ElementTypeInfo typeInfo = new ElementTypeInfo();
            // Id
            typeInfo.Id = elementType.Id.GetIntValue();
            // UniqueId
            typeInfo.UniqueId = elementType.UniqueId;
            typeInfo.Name = elementType.Name;
            typeInfo.FamilyName = elementType.FamilyName;
            typeInfo.Category = elementType.Category.Name;
            typeInfo.BuiltInCategory = Enum.GetName(typeof(BuiltInCategory), elementType.Category.Id.GetIntValue());
            typeInfo.Parameters = GetDimensionParameters(elementType);
            ParameterInfo thicknessParam = GetThicknessInfo(elementType);
            if (thicknessParam != null)
            {
                typeInfo.Parameters.Add(thicknessParam);
            }
            return typeInfo;
        }

        /// <summary>
        /// </summary>
        public static PositioningElementInfo CreatePositioningElementInfo(Document doc, Element element)
        {
            try
            {
                if (element == null)
                    return null;
                PositioningElementInfo info = new PositioningElementInfo
                {
                    Id = element.Id.GetIntValue(),
                    UniqueId = element.UniqueId,
                    Name = element.Name,
                    FamilyName = element?.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString(),
                    Category = element.Category?.Name,
                    BuiltInCategory = element.Category != null ?
                        Enum.GetName(typeof(BuiltInCategory), element.Category.Id.GetIntValue()) : null,
                    ElementClass = element.GetType().Name,
                    BoundingBox = GetBoundingBoxInfo(element)
                };

                if (element is Level level)
                {
                    info.Elevation = level.Elevation * 304.8;
                }
                else if (element is Grid grid)
                {
                    Curve curve = grid.Curve;
                    if (curve != null)
                    {
                        XYZ start = curve.GetEndPoint(0);
                        XYZ end = curve.GetEndPoint(1);
                        info.GridLine = new JZLine(
                            start.X * 304.8, start.Y * 304.8, start.Z * 304.8,
                            end.X * 304.8, end.Y * 304.8, end.Z * 304.8);
                    }
                }

                info.Level = GetElementLevel(doc, element);

                return info;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"创建空间定位元素信息时出错: {ex.Message}");
                return null;
            }
        }
        /// <summary>
        /// </summary>
        public static SpatialElementInfo CreateSpatialElementInfo(Document doc, Element element)
        {
            try
            {
                if (element == null || !(element is SpatialElement))
                    return null;
                SpatialElement spatialElement = element as SpatialElement;
                SpatialElementInfo info = new SpatialElementInfo
                {
                    Id = element.Id.GetIntValue(),
                    UniqueId = element.UniqueId,
                    Name = element.Name,
                    FamilyName = element?.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString(),
                    Category = element.Category?.Name,
                    BuiltInCategory = element.Category != null ?
                        Enum.GetName(typeof(BuiltInCategory), element.Category.Id.GetIntValue()) : null,
                    ElementClass = element.GetType().Name,
                    BoundingBox = GetBoundingBoxInfo(element)
                };

                if (element is Room room)
                {
                    info.Number = room.Number;
                    info.Volume = room.Volume * Math.Pow(304.8, 3);
                }
                else if (element is Area area)
                {
                    info.Number = area.Number;
                }

                Parameter areaParam = element.get_Parameter(BuiltInParameter.ROOM_AREA);
                if (areaParam != null && areaParam.HasValue)
                {
                    info.Area = areaParam.AsDouble() * Math.Pow(304.8, 2);
                }

                Parameter perimeterParam = element.get_Parameter(BuiltInParameter.ROOM_PERIMETER);
                if (perimeterParam != null && perimeterParam.HasValue)
                {
                    info.Perimeter = perimeterParam.AsDouble() * 304.8;
                }

                info.Level = GetElementLevel(doc, element);

                return info;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"创建空间元素信息时出错: {ex.Message}");
                return null;
            }
        }
        /// <summary>
        /// </summary>
        public static ViewInfo CreateViewInfo(Document doc, Element element)
        {
            try
            {
                if (element == null || !(element is View))
                    return null;
                View view = element as View;

                ViewInfo info = new ViewInfo
                {
                    Id = element.Id.GetIntValue(),
                    UniqueId = element.UniqueId,
                    Name = element.Name,
                    FamilyName = element?.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString(),
                    Category = element.Category?.Name,
                    BuiltInCategory = element.Category != null ?
                        Enum.GetName(typeof(BuiltInCategory), element.Category.Id.GetIntValue()) : null,
                    ElementClass = element.GetType().Name,
                    ViewType = view.ViewType.ToString(),
                    Scale = view.Scale,
                    IsTemplate = view.IsTemplate,
                    DetailLevel = view.DetailLevel.ToString(),
                    BoundingBox = GetBoundingBoxInfo(element)
                };

                if (view is ViewPlan viewPlan && viewPlan.GenLevel != null)
                {
                    Level level = viewPlan.GenLevel;
                    info.AssociatedLevel = new LevelInfo
                    {
                        Id = level.Id.GetIntValue(),
                        Name = level.Name,
                        Height = level.Elevation * 304.8
                    };
                }

                UIDocument uidoc = new UIDocument(doc);

                IList<UIView> openViews = uidoc.GetOpenUIViews();

                foreach (UIView uiView in openViews)
                {
                    if (uiView.ViewId.GetValue() == view.Id.GetValue())
                    {
                        info.IsOpen = true;

                        if (uidoc.ActiveView.Id.GetValue() == view.Id.GetValue())
                        {
                            info.IsActive = true;
                        }
                        break;
                    }
                }

                return info;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"创建视图元素信息时出错: {ex.Message}");
                return null;
            }
        }
        /// <summary>
        /// </summary>
        public static AnnotationInfo CreateAnnotationInfo(Document doc, Element element)
        {
            try
            {
                if (element == null)
                    return null;
                AnnotationInfo info = new AnnotationInfo
                {
                    Id = element.Id.GetIntValue(),
                    UniqueId = element.UniqueId,
                    Name = element.Name,
                    FamilyName = element?.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString(),
                    Category = element.Category?.Name,
                    BuiltInCategory = element.Category != null ?
                        Enum.GetName(typeof(BuiltInCategory), element.Category.Id.GetIntValue()) : null,
                    ElementClass = element.GetType().Name,
                    BoundingBox = GetBoundingBoxInfo(element)
                };

                Parameter viewParam = element.get_Parameter(BuiltInParameter.VIEW_NAME);
                if (viewParam != null && viewParam.HasValue)
                {
                    info.OwnerView = viewParam.AsString();
                }
                else if (element.OwnerViewId != ElementId.InvalidElementId)
                {
                    View ownerView = doc.GetElement(element.OwnerViewId) as View;
                    info.OwnerView = ownerView?.Name;
                }

                if (element is TextNote textNote)
                {
                    info.TextContent = textNote.Text;
                    XYZ position = textNote.Coord;
                    info.Position = new JZPoint(
                        position.X * 304.8,
                        position.Y * 304.8,
                        position.Z * 304.8);
                }
                else if (element is Dimension dimension)
                {
                    info.DimensionValue = dimension.Value.ToString();
                    XYZ origin = dimension.Origin;
                    info.Position = new JZPoint(
                        origin.X * 304.8,
                        origin.Y * 304.8,
                        origin.Z * 304.8);
                }
                else if (element is AnnotationSymbol annotationSymbol)
                {
                    if (annotationSymbol.Location is LocationPoint locationPoint)
                    {
                        XYZ position = locationPoint.Point;
                        info.Position = new JZPoint(
                            position.X * 304.8,
                            position.Y * 304.8,
                            position.Z * 304.8);
                    }
                }
                return info;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"创建注释元素信息时出错: {ex.Message}");
                return null;
            }
        }
        /// <summary>
        /// </summary>
        public static GroupOrLinkInfo CreateGroupOrLinkInfo(Document doc, Element element)
        {
            try
            {
                if (element == null)
                    return null;
                GroupOrLinkInfo info = new GroupOrLinkInfo
                {
                    Id = element.Id.GetIntValue(),
                    UniqueId = element.UniqueId,
                    Name = element.Name,
                    FamilyName = element?.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString(),
                    Category = element.Category?.Name,
                    BuiltInCategory = element.Category != null ?
                        Enum.GetName(typeof(BuiltInCategory), element.Category.Id.GetIntValue()) : null,
                    ElementClass = element.GetType().Name,
                    BoundingBox = GetBoundingBoxInfo(element)
                };

                if (element is Group group)
                {
                    ICollection<ElementId> memberIds = group.GetMemberIds();
                    info.MemberCount = memberIds?.Count;
                    info.GroupType = group.GroupType?.Name;
                }
                else if (element is RevitLinkInstance linkInstance)
                {
                    RevitLinkType linkType = doc.GetElement(linkInstance.GetTypeId()) as RevitLinkType;
                    if (linkType != null)
                    {
                        ExternalFileReference extFileRef = linkType.GetExternalFileReference();
                        string absPath = ModelPathUtils.ConvertModelPathToUserVisiblePath(extFileRef.GetAbsolutePath());
                        info.LinkPath = absPath;

                        LinkedFileStatus linkStatus = linkType.GetLinkedFileStatus();
                        info.LinkStatus = linkStatus.ToString();
                    }
                    else
                    {
                        info.LinkStatus = LinkedFileStatus.Invalid.ToString();
                    }

                    LocationPoint location = linkInstance.Location as LocationPoint;
                    if (location != null)
                    {
                        XYZ point = location.Point;
                        info.Position = new JZPoint(
                            point.X * 304.8,
                            point.Y * 304.8,
                            point.Z * 304.8);
                    }
                }

                return info;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"创建组和链接信息时出错: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// </summary>
        public static ElementBasicInfo CreateElementBasicInfo(Document doc, Element element)
        {
            try
            {
                if (element == null)
                    return null;
                ElementBasicInfo basicInfo = new ElementBasicInfo
                {
                    Id = element.Id.GetIntValue(),
                    UniqueId = element.UniqueId,
                    Name = element.Name,
                    FamilyName = element?.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString(),
                    Category = element.Category?.Name,
                    BuiltInCategory = element.Category != null ?
                        Enum.GetName(typeof(BuiltInCategory), element.Category.Id.GetIntValue()) : null,
                    BoundingBox = GetBoundingBoxInfo(element)
                };
                return basicInfo;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"创建元素基础信息时出错: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// </summary>
        public static ParameterInfo GetThicknessInfo(Element element)
        {
            if (element == null)
            {
                return null;
            }

            ElementType elementType = element.Document.GetElement(element.GetTypeId()) as ElementType;
            if (elementType == null)
            {
                return null;
            }

            Parameter thicknessParam = null;

            if (elementType is WallType)
            {
                thicknessParam = elementType.get_Parameter(BuiltInParameter.WALL_ATTR_WIDTH_PARAM);
            }
            else if (elementType is FloorType)
            {
                thicknessParam = elementType.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM);
            }
            else if (elementType is FamilySymbol familySymbol)
            {
                switch (familySymbol.Category?.Id.GetIntValue())
                {
                    case (int)BuiltInCategory.OST_Doors:
                    case (int)BuiltInCategory.OST_Windows:
                        thicknessParam = elementType.get_Parameter(BuiltInParameter.FAMILY_THICKNESS_PARAM);
                        break;
                }
            }
            else if (elementType is CeilingType)
            {
                thicknessParam = elementType.get_Parameter(BuiltInParameter.CEILING_THICKNESS);
            }

            if (thicknessParam != null && thicknessParam.HasValue)
            {
                return new ParameterInfo
                {
                    Name = "厚度",
                    Value = $"{thicknessParam.AsDouble() * 304.8}"
                };
            }
            return null;
        }

        /// <summary>
        /// </summary>
        public static LevelInfo GetElementLevel(Document doc, Element element)
        {
            try
            {
                Level level = null;

                if (element is Wall wall)
                {
                    level = doc.GetElement(wall.LevelId) as Level;
                }
                else if (element is Floor floor)
                {
                    Parameter levelParam = floor.get_Parameter(BuiltInParameter.LEVEL_PARAM);
                    if (levelParam != null && levelParam.HasValue)
                    {
                        level = doc.GetElement(levelParam.AsElementId()) as Level;
                    }
                }
                else if (element is FamilyInstance familyInstance)
                {
                    Parameter levelParam = familyInstance.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
                    if (levelParam != null && levelParam.HasValue)
                    {
                        level = doc.GetElement(levelParam.AsElementId()) as Level;
                    }
                    if (level == null)
                    {
                        levelParam = familyInstance.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);
                        if (levelParam != null && levelParam.HasValue)
                        {
                            level = doc.GetElement(levelParam.AsElementId()) as Level;
                        }
                    }
                }
                else
                {
                    Parameter levelParam = element.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM);
                    if (levelParam != null && levelParam.HasValue)
                    {
                        level = doc.GetElement(levelParam.AsElementId()) as Level;
                    }
                }

                if (level != null)
                {
                    LevelInfo levelInfo = new LevelInfo
                    {
                        Id = level.Id.GetIntValue(),
                        Name = level.Name,
                        Height = level.Elevation * 304.8
                    };
                    return levelInfo;
                }
                else
                {
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// </summary>
        public static BoundingBoxInfo GetBoundingBoxInfo(Element element)
        {
            try
            {
                BoundingBoxXYZ bbox = element.get_BoundingBox(null);
                if (bbox == null)
                    return null;
                return new BoundingBoxInfo
                {
                    Min = new JZPoint(
                        bbox.Min.X * 304.8,
                        bbox.Min.Y * 304.8,
                        bbox.Min.Z * 304.8),
                    Max = new JZPoint(
                        bbox.Max.X * 304.8,
                        bbox.Max.Y * 304.8,
                        bbox.Max.Z * 304.8)
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// </summary>
        public static ParameterInfo GetBoundingBoxHeight(BoundingBoxInfo boundingBoxInfo)
        {
            try
            {
                if (boundingBoxInfo?.Min == null || boundingBoxInfo?.Max == null)
                {
                    return null;
                }

                double height = Math.Abs(boundingBoxInfo.Max.Z - boundingBoxInfo.Min.Z);

                return new ParameterInfo
                {
                    Name = "高度",
                    Value = $"{height}"
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// </summary>
        public static List<ParameterInfo> GetDimensionParameters(Element element)
        {
            if (element == null)
            {
                return new List<ParameterInfo>();
            }

            var parameters = new List<ParameterInfo>();

            foreach (Parameter param in element.Parameters)
            {
                try
                {
                    if (!param.HasValue || param.IsReadOnly)
                    {
                        continue;
                    }

                    if (IsDimensionParameter(param))
                    {
                        string value = param.AsValueString();

                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            parameters.Add(new ParameterInfo
                            {
                                Name = param.Definition.Name,
                                Value = value
                            });
                        }
                    }
                }
                catch
                {
                    continue;
                }
            }

            return parameters.OrderBy(p => p.Name).ToList();
        }

        /// <summary>
        /// </summary>
        public static bool IsDimensionParameter(Parameter param)
        {

#if REVIT2023_OR_GREATER
            ForgeTypeId paramTypeId = param.Definition.GetDataType();

            bool isDimensionType = paramTypeId.Equals(SpecTypeId.Length) ||
                                   paramTypeId.Equals(SpecTypeId.Angle) ||
                                   paramTypeId.Equals(SpecTypeId.Area) ||
                                   paramTypeId.Equals(SpecTypeId.Volume);
            return isDimensionType;
#else
            bool isDimensionType = param.Definition.ParameterType == ParameterType.Length ||
                                   param.Definition.ParameterType == ParameterType.Angle ||
                                   param.Definition.ParameterType == ParameterType.Area ||
                                   param.Definition.ParameterType == ParameterType.Volume;

            return isDimensionType;
#endif
        }

    }

    /// <summary>
    /// </summary>
    public class ElementInstanceInfo
    {
        /// <summary>
        /// Id
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// Id
        /// </summary>
        public string UniqueId { get; set; }
        /// <summary>
        /// </summary>
        public int TypeId { get; set; }
        /// <summary>
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// </summary>
        public string FamilyName { get; set; }
        /// <summary>
        /// </summary>
        public string Category { get; set; }
        /// <summary>
        /// </summary>
        public string BuiltInCategory { get; set; }
        /// <summary>
        /// </summary>
        public int RoomId { get; set; }
        /// <summary>
        /// </summary>
        public LevelInfo Level { get; set; }
        /// <summary>
        /// </summary>
        public BoundingBoxInfo BoundingBox { get; set; }
        /// <summary>
        /// </summary>
        public List<ParameterInfo> Parameters { get; set; } = new List<ParameterInfo>();

    }

    /// <summary>
    /// </summary>
    public class ElementTypeInfo
    {
        /// <summary>
        /// ID
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// Id
        /// </summary>
        public string UniqueId { get; set; }
        /// <summary>
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// </summary>
        public string FamilyName { get; set; }
        /// <summary>
        /// </summary>
        public string Category { get; set; }
        /// <summary>
        /// </summary>
        public string BuiltInCategory { get; set; }
        /// <summary>
        /// </summary>
        public List<ParameterInfo> Parameters { get; set; } = new List<ParameterInfo>();

    }

    /// <summary>
    /// </summary>
    public class PositioningElementInfo
    {
        /// <summary>
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// </summary>
        public string UniqueId { get; set; }
        /// <summary>
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// </summary>
        public string FamilyName { get; set; }
        /// <summary>
        /// </summary>
        public string Category { get; set; }
        /// <summary>
        /// </summary>
        public string BuiltInCategory { get; set; }
        /// <summary>
        /// </summary>
        public string ElementClass { get; set; }
        /// <summary>
        /// </summary>
        public double? Elevation { get; set; }
        /// <summary>
        /// </summary>
        public LevelInfo Level { get; set; }
        /// <summary>
        /// </summary>
        public BoundingBoxInfo BoundingBox { get; set; }
        /// <summary>
        /// </summary>
        public JZLine GridLine { get; set; }
    }
    /// <summary>
    /// </summary>
    public class SpatialElementInfo
    {
        /// <summary>
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// </summary>
        public string UniqueId { get; set; }
        /// <summary>
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// </summary>
        public string FamilyName { get; set; }
        /// <summary>
        /// </summary>
        public string Number { get; set; }
        /// <summary>
        /// </summary>
        public string Category { get; set; }
        /// <summary>
        /// </summary>
        public string BuiltInCategory { get; set; }
        /// <summary>
        /// </summary>
        public string ElementClass { get; set; }
        /// <summary>
        /// </summary>
        public double? Area { get; set; }
        /// <summary>
        /// </summary>
        public double? Volume { get; set; }
        /// <summary>
        /// </summary>
        public double? Perimeter { get; set; }
        /// <summary>
        /// </summary>
        public LevelInfo Level { get; set; }

        /// <summary>
        /// </summary>
        public BoundingBoxInfo BoundingBox { get; set; }
    }
    /// <summary>
    /// </summary>
    public class ViewInfo
    {
        /// <summary>
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// </summary>
        public string UniqueId { get; set; }
        /// <summary>
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// </summary>
        public string FamilyName { get; set; }
        /// <summary>
        /// </summary>
        public string Category { get; set; }
        /// <summary>
        /// </summary>
        public string BuiltInCategory { get; set; }
        /// <summary>
        /// </summary>
        public string ElementClass { get; set; }

        /// <summary>
        /// </summary>
        public string ViewType { get; set; }

        /// <summary>
        /// </summary>
        public int? Scale { get; set; }

        /// <summary>
        /// </summary>
        public bool IsTemplate { get; set; }

        /// <summary>
        /// </summary>
        public string DetailLevel { get; set; }

        /// <summary>
        /// </summary>
        public LevelInfo AssociatedLevel { get; set; }

        /// <summary>
        /// </summary>
        public BoundingBoxInfo BoundingBox { get; set; }

        /// <summary>
        /// </summary>
        public bool IsOpen { get; set; }

        /// <summary>
        /// </summary>
        public bool IsActive { get; set; }
    }
    /// <summary>
    /// </summary>
    public class AnnotationInfo
    {
        /// <summary>
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// </summary>
        public string UniqueId { get; set; }
        /// <summary>
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// </summary>
        public string FamilyName { get; set; }
        /// <summary>
        /// </summary>
        public string Category { get; set; }
        /// <summary>
        /// </summary>
        public string BuiltInCategory { get; set; }
        /// <summary>
        /// </summary>
        public string ElementClass { get; set; }
        /// <summary>
        /// </summary>
        public string OwnerView { get; set; }
        /// <summary>
        /// </summary>
        public string TextContent { get; set; }
        /// <summary>
        /// </summary>
        public JZPoint Position { get; set; }

        /// <summary>
        /// </summary>
        public BoundingBoxInfo BoundingBox { get; set; }
        /// <summary>
        /// </summary>
        public string DimensionValue { get; set; }
    }
    /// <summary>
    /// </summary>
    public class GroupOrLinkInfo
    {
        /// <summary>
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// </summary>
        public string UniqueId { get; set; }
        /// <summary>
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// </summary>
        public string FamilyName { get; set; }
        /// <summary>
        /// </summary>
        public string Category { get; set; }
        /// <summary>
        /// </summary>
        public string BuiltInCategory { get; set; }
        /// <summary>
        /// </summary>
        public string ElementClass { get; set; }
        /// <summary>
        /// </summary>
        public int? MemberCount { get; set; }
        /// <summary>
        /// </summary>
        public string GroupType { get; set; }
        /// <summary>
        /// </summary>
        public string LinkStatus { get; set; }
        /// <summary>
        /// </summary>
        public string LinkPath { get; set; }
        /// <summary>
        /// </summary>
        public JZPoint Position { get; set; }

        /// <summary>
        /// </summary>
        public BoundingBoxInfo BoundingBox { get; set; }
    }
    /// <summary>
    /// </summary>
    public class ElementBasicInfo
    {
        /// <summary>
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// </summary>
        public string UniqueId { get; set; }
        /// <summary>
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// </summary>
        public string FamilyName { get; set; }
        /// <summary>
        /// </summary>
        public string Category { get; set; }
        /// <summary>
        /// </summary>
        public string BuiltInCategory { get; set; }

        /// <summary>
        /// </summary>
        public BoundingBoxInfo BoundingBox { get; set; }
    }



    /// <summary>
    /// </summary>
    public class ParameterInfo
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }

    /// <summary>
    /// </summary>
    public class BoundingBoxInfo
    {
        public JZPoint Min { get; set; }
        public JZPoint Max { get; set; }
    }

    /// <summary>
    /// </summary>
    public class LevelInfo
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public double Height { get; set; }
    }



}
