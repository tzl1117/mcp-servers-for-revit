using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Commands;
using RevitMCPCommandSet.Models.Common;
using System.IO;
using System.Reflection;

namespace RevitMCPCommandSet.Utils
{
    public static class ProjectUtils
    {
        /// <summary>
        /// Creates a family instance.
        /// </summary>
        /// <param name="doc">Current document.</param>
        /// <param name="familySymbol">Family type.</param>
        /// <param name="locationPoint">Placement point.</param>
        /// <param name="locationLine">Placement line.</param>
        /// <param name="baseLevel">Base level.</param>
        /// <param name="topLevel">Second level for TwoLevelsBased families.</param>
        /// <param name="baseOffset">Base offset in feet.</param>
        /// <param name="topOffset">Top offset in feet.</param>
        /// <param name="faceDirection">Facing direction.</param>
        /// <param name="handDirection">Hand direction.</param>
        /// <param name="view">View.</param>
        /// <returns>The created family instance, or null on failure.</returns>
        public static FamilyInstance CreateInstance(
            this Document doc,
            FamilySymbol familySymbol,
            XYZ locationPoint = null,
            Line locationLine = null,
            Level baseLevel = null,
            Level topLevel = null,
            double baseOffset = -1,
            double topOffset = -1,
            XYZ faceDirection = null,
            XYZ handDirection = null,
            View view = null,
            Element explicitHost = null,
            bool snapToHostCenter = true)
        {
            // Validate required parameters.
            if (doc == null)
                throw new ArgumentNullException($"Required parameter {typeof(Document)} {nameof(doc)} is missing.");
            if (familySymbol == null)
                throw new ArgumentNullException($"Required parameter {typeof(FamilySymbol)} {nameof(familySymbol)} is missing.");

            // Activate the family symbol.
            if (!familySymbol.IsActive)
                familySymbol.Activate();

            FamilyInstance instance = null;

            // Choose the creation method based on family placement type.
            switch (familySymbol.Family.FamilyPlacementType)
            {
                // Single-level families, such as generic models.
                case FamilyPlacementType.OneLevelBased:
                    if (locationPoint == null)
                        throw new ArgumentNullException($"Required parameter {typeof(XYZ)} {nameof(locationPoint)} is missing.");
                    // With level information.
                    if (baseLevel != null)
                    {
                        instance = doc.Create.NewFamilyInstance(
                            locationPoint,                  // Physical placement location.
                            familySymbol,                   // FamilySymbol representing the instance type.
                            baseLevel,                      // Level used as the base level.
                            StructuralType.NonStructural);  // Structural type when applicable.
                    }
                    // Without level information.
                    else
                    {
                        instance = doc.Create.NewFamilyInstance(
                            locationPoint,                  // Physical placement location.
                            familySymbol,                   // FamilySymbol representing the instance type.
                            StructuralType.NonStructural);  // Structural type when applicable.
                    }
                    break;

                // Hosted single-level families, such as doors and windows.
                case FamilyPlacementType.OneLevelBasedHosted:
                    if (locationPoint == null)
                        throw new ArgumentNullException($"Required parameter {typeof(XYZ)} {nameof(locationPoint)} is missing.");

                    Element host = explicitHost;
                    XYZ placementPoint = locationPoint;

                    // If explicit host provided and it's a wall, snap to its centerline
                    if (host != null && snapToHostCenter && host is Wall explicitWall)
                    {
                        LocationCurve eLoc = explicitWall.Location as LocationCurve;
                        if (eLoc != null)
                        {
                            IntersectionResult eIr = eLoc.Curve.Project(locationPoint);
                            if (eIr != null)
                                placementPoint = new XYZ(eIr.XYZPoint.X, eIr.XYZPoint.Y, locationPoint.Z);
                        }
                    }

                    // Auto-detect host wall if not explicitly provided
                    if (host == null)
                    {
                        // Try geometric wall-centerline proximity first
                        var wallResult = doc.GetNearestWallByLocationLine(locationPoint, baseLevel);
                        if (wallResult.HasValue)
                        {
                            host = wallResult.Value.wall;
                            if (snapToHostCenter)
                                placementPoint = wallResult.Value.projectedPoint;
                        }
                        else
                        {
                            // Fall back to original ray-casting method
                            host = doc.GetNearestHostElement(locationPoint, familySymbol);
                        }
                    }

                    if (host == null)
                        throw new ArgumentNullException("No suitable host was found.");

                    if (baseLevel != null)
                    {
                        instance = doc.Create.NewFamilyInstance(
                            placementPoint,
                            familySymbol,
                            host,
                            baseLevel,
                            StructuralType.NonStructural);
                    }
                    else
                    {
                        instance = doc.Create.NewFamilyInstance(
                            placementPoint,
                            familySymbol,
                            host,
                            StructuralType.NonStructural);
                    }

                    // Set sill height for windows (baseOffset maps to sill height for hosted elements)
                    if (instance != null && baseOffset != -1)
                    {
                        Parameter sillParam = instance.get_Parameter(BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM);
                        if (sillParam != null && !sillParam.IsReadOnly)
                        {
                            sillParam.Set(baseOffset);
                        }
                    }
                    break;

                // Two-level families, such as columns.
                case FamilyPlacementType.TwoLevelsBased:
                    if (locationPoint == null)
                        throw new ArgumentNullException($"Required parameter {typeof(XYZ)} {nameof(locationPoint)} is missing.");
                    if (baseLevel == null)
                        throw new ArgumentNullException($"Required parameter {typeof(Level)} {nameof(baseLevel)} is missing.");
                    // Determine whether this is a structural or architectural column.
                    StructuralType structuralType = StructuralType.NonStructural;
                    if (familySymbol.Category.Id.GetIntValue() == (int)BuiltInCategory.OST_StructuralColumns)
                        structuralType = StructuralType.Column;
                    instance = doc.Create.NewFamilyInstance(
                        locationPoint,              // Physical placement location.
                        familySymbol,               // FamilySymbol representing the instance type.
                        baseLevel,                  // Level used as the base level.
                        structuralType);            // Structural type when applicable.
                    // Set base/top levels and offsets.
                    if (instance != null)
                    {
                        // Set the column base and top levels.
                        if (baseLevel != null)
                        {
                            Parameter baseLevelParam = instance.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM);
                            if (baseLevelParam != null)
                                baseLevelParam.Set(baseLevel.Id);
                        }
                        if (topLevel != null)
                        {
                            Parameter topLevelParam = instance.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM);
                            if (topLevelParam != null)
                                topLevelParam.Set(topLevel.Id);
                        }
                        // Get the base offset parameter.
                        if (baseOffset != -1)
                        {
                            Parameter baseOffsetParam = instance.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM);
                            if (baseOffsetParam != null && baseOffsetParam.StorageType == StorageType.Double)
                            {
                                // Convert millimeters to Revit internal units.
                                double baseOffsetInternal = baseOffset;
                                baseOffsetParam.Set(baseOffsetInternal);
                            }
                        }
                        // Get the top offset parameter.
                        if (topOffset != -1)
                        {
                            Parameter topOffsetParam = instance.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM);
                            if (topOffsetParam != null && topOffsetParam.StorageType == StorageType.Double)
                            {
                                // Convert millimeters to Revit internal units.
                                double topOffsetInternal = topOffset;
                                topOffsetParam.Set(topOffsetInternal);
                            }
                        }
                    }
                    break;

                // View-specific families, such as detail annotations.
                case FamilyPlacementType.ViewBased:
                    if (locationPoint == null)
                        throw new ArgumentNullException($"Required parameter {typeof(XYZ)} {nameof(locationPoint)} is missing.");
                    instance = doc.Create.NewFamilyInstance(
                        locationPoint,  // Family instance origin, projected onto ViewPlan when applicable.
                        familySymbol,   // FamilySymbol representing the instance type.
                        view);          // 2D view that hosts the family instance.
                    break;

                // Work-plane-based families, including face- and wall-based generic models.
                case FamilyPlacementType.WorkPlaneBased:
                    if (locationPoint == null)
                        throw new ArgumentNullException($"Required parameter {typeof(XYZ)} {nameof(locationPoint)} is missing.");
                    // Get the nearest host face.
                    Reference hostFace = doc.GetNearestFaceReference(locationPoint, 1000 / 304.8);
                    if (hostFace == null)
                        throw new ArgumentNullException("No suitable host was found.");
                    if (faceDirection == null || faceDirection == XYZ.Zero)
                    {
                        var result = doc.GenerateDefaultOrientation(hostFace);
                        faceDirection = result.FacingOrientation;
                    }
                    // Create the family instance on the face using the point and direction.
                    instance = doc.Create.NewFamilyInstance(
                        hostFace,               // Reference to the host face.
                        locationPoint,          // Placement point on the face.
                        faceDirection,          // Direction defining the face rotation; it cannot be parallel to the face normal.
                        familySymbol);          // FamilySymbol for a WorkPlaneBased family.
                    break;

                // Curve-based work-plane families, such as line-based generic models.
                case FamilyPlacementType.CurveBased:
                    if (locationLine == null)
                        throw new ArgumentNullException($"Required parameter {typeof(Line)} {nameof(locationLine)} is missing.");

                    // Get the nearest host face without tolerance.
                    Reference lineHostFace = doc.GetNearestFaceReference(locationLine.Evaluate(0.5, true), 1e-5);
                    if (lineHostFace != null)
                    {
                        instance = doc.Create.NewFamilyInstance(
                            lineHostFace,   // Reference to the host face.
                            locationLine,   // Curve on which to place the family instance.
                            familySymbol);  // FamilySymbol for a WorkPlaneBased or CurveBased family.
                    }
                    else
                    {
                        instance = doc.Create.NewFamilyInstance(
                            locationLine,                   // Curve on which to place the family instance.
                            familySymbol,                   // FamilySymbol for a WorkPlaneBased or CurveBased family.
                            baseLevel,                      // Level used as the base level.
                            StructuralType.NonStructural);  // Structural type when applicable.
                    }
                    if (instance != null)
                    {
                        // Get the base offset parameter.
                        if (baseOffset != -1)
                        {
                            Parameter baseOffsetParam = instance.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);
                            if (baseOffsetParam != null && baseOffsetParam.StorageType == StorageType.Double)
                            {
                                // Convert millimeters to Revit internal units.
                                double baseOffsetInternal = baseOffset;
                                baseOffsetParam.Set(baseOffsetInternal);
                            }
                        }
                    }
                    break;

                // Curve-based families in a specific view, such as detail components.
                case FamilyPlacementType.CurveBasedDetail:
                    if (locationLine == null)
                        throw new ArgumentNullException($"Required parameter {typeof(Line)} {nameof(locationLine)} is missing.");
                    if (view == null)
                        throw new ArgumentNullException($"Required parameter {typeof(View)} {nameof(view)} is missing.");
                    instance = doc.Create.NewFamilyInstance(
                        locationLine,   // Instance line location, which must lie in the view plane.
                        familySymbol,   // FamilySymbol representing the instance type.
                        view);          // 2D view that hosts the family instance.
                    break;

                // Structural curve-driven families, such as beams, braces, or slanted columns.
                case FamilyPlacementType.CurveDrivenStructural:
                    if (locationLine == null)
                        throw new ArgumentNullException($"Required parameter {typeof(Line)} {nameof(locationLine)} is missing.");
                    if (baseLevel == null)
                        throw new ArgumentNullException($"Required parameter {typeof(Level)} {nameof(baseLevel)} is missing.");
                    instance = doc.Create.NewFamilyInstance(
                        locationLine,                   // Curve on which to place the family instance.
                        familySymbol,                   // FamilySymbol for a WorkPlaneBased or CurveBased family.
                        baseLevel,                      // Level used as the base level.
                        StructuralType.Beam);           // Structural type when applicable.
                    break;

                // Adaptive families, such as adaptive generic models and curtain panels.
                case FamilyPlacementType.Adaptive:
                    throw new NotImplementedException("FamilyPlacementType.Adaptive creation is not implemented.");

                default:
                    break;
            }
            return instance;
        }

        /// <summary>
        /// Generates default facing and hand orientations.
        /// </summary>
        /// <param name="hostFace"></param>
        /// <returns></returns>
        public static (XYZ FacingOrientation, XYZ HandOrientation) GenerateDefaultOrientation(this Document doc, Reference hostFace)
        {
            var facingOrientation = new XYZ();  // Positive family Y-axis direction after loading.
            var handOrientation = new XYZ();    // Positive family X-axis direction after loading.

            // Step 1: Get the face from the reference.
            Face face = doc.GetElement(hostFace.ElementId).GetGeometryObjectFromReference(hostFace) as Face;

            // Step 2: Get the face profile.
            List<Curve> profile = null;
            // Each sub-list represents a closed profile; the first is usually the outer profile.
            List<List<Curve>> profiles = new List<List<Curve>>();
            // Get all profile loops, including the outer profile and internal voids.
            EdgeArrayArray edgeLoops = face.EdgeLoops;
            // Iterate through each profile loop.
            foreach (EdgeArray loop in edgeLoops)
            {
                List<Curve> currentLoop = new List<Curve>();
                // Get each edge in the loop.
                foreach (Edge edge in loop)
                {
                    Curve curve = edge.AsCurve();
                    currentLoop.Add(curve);
                }
                // Add loops containing edges to the result set.
                if (currentLoop.Count > 0)
                {
                    profiles.Add(currentLoop);
                }
            }
            // The first profile is usually the outer profile.
            if (profiles != null && profiles.Any())
                profile = profiles.FirstOrDefault();

            // Step 3: Get the face normal.
            XYZ faceNormal = null;
            // Planar faces expose their normal directly.
            if (face is PlanarFace planarFace)
                faceNormal = planarFace.FaceNormal;

            // Step 4: Get the face's two right-hand-rule-compliant primary directions.
            var result = face.GetMainDirections();
            var primaryDirection = result.PrimaryDirection;
            var secondaryDirection = result.SecondaryDirection;

            // Use the long direction as hand orientation and short direction as facing orientation.
            facingOrientation = primaryDirection;
            handOrientation = secondaryDirection;

            // Validate the right-hand rule: thumb=hand, index=facing, middle=normal.
            if (!facingOrientation.IsRightHandRuleCompliant(handOrientation, faceNormal))
            {
                var newHandOrientation = facingOrientation.GenerateIndexFinger(faceNormal);
                if (newHandOrientation != null)
                {
                    handOrientation = newHandOrientation;
                }
            }

            return (facingOrientation, handOrientation);
        }

        /// <summary>
        /// </summary>
        public static Reference GetNearestFaceReference(this Document doc, XYZ location, double radius = 1000 / 304.8)
        {
            try
            {
                location = new XYZ(location.X, location.Y, location.Z + 0.1 / 304.8);

                View3D view3D = null;
                FilteredElementCollector collector = new FilteredElementCollector(doc)
                    .OfClass(typeof(View3D));

                foreach (View3D v in collector)
                {
                    if (!v.IsTemplate)
                    {
                        view3D = v;
                        break;
                    }
                }

                if (view3D == null)
                {
                    using (Transaction trans = new Transaction(doc, "Create 3D View"))
                    {
                        trans.Start();
                        ViewFamilyType vft = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional);

                        if (vft != null)
                        {
                            view3D = View3D.CreateIsometric(doc, vft.Id);
                        }
                        trans.Commit();
                    }
                }

                if (view3D == null)
                {
                    TaskDialog.Show("Error", "Unable to create or retrieve a 3D view.");
                    return null;
                }

                XYZ[] directions = new XYZ[]
                {
                  XYZ.BasisX,
                  -XYZ.BasisX,
                  XYZ.BasisY,
                  -XYZ.BasisY,
                  XYZ.BasisZ,
                  -XYZ.BasisZ
                };

                ElementClassFilter wallFilter = new ElementClassFilter(typeof(Wall));
                ElementClassFilter floorFilter = new ElementClassFilter(typeof(Floor));
                ElementClassFilter ceilingFilter = new ElementClassFilter(typeof(Ceiling));
                ElementClassFilter instanceFilter = new ElementClassFilter(typeof(FamilyInstance));

                LogicalOrFilter categoryFilter = new LogicalOrFilter(
                    new ElementFilter[] { wallFilter, floorFilter, ceilingFilter, instanceFilter });


                //ElementFilter filter = new ElementIsElementTypeFilter(true);

                ReferenceIntersector refIntersector = new ReferenceIntersector(categoryFilter,
                    FindReferenceTarget.Face, view3D);
                refIntersector.FindReferencesInRevitLinks = true;

                double minDistance = double.MaxValue;
                Reference nearestFace = null;

                foreach (XYZ direction in directions)
                {
                    IList<ReferenceWithContext> references = refIntersector.Find(location, direction);

                    foreach (ReferenceWithContext rwc in references)
                    {
                        double distance = rwc.Proximity;

                        if (distance <= radius && distance < minDistance)
                        {
                            minDistance = distance;
                            nearestFace = rwc.GetReference();
                        }
                    }
                }

                return nearestFace;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", $"An error occurred while retrieving the nearest face: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// </summary>
        public static Element GetNearestHostElement(this Document doc, XYZ location, FamilySymbol familySymbol, double radius = 5.0)
        {
            try
            {
                if (doc == null || location == null || familySymbol == null)
                    return null;

                Parameter hostParam = familySymbol.Family.get_Parameter(BuiltInParameter.FAMILY_HOSTING_BEHAVIOR);
                int hostingBehavior = hostParam?.AsInteger() ?? 0;

                View3D view3D = null;
                FilteredElementCollector viewCollector = new FilteredElementCollector(doc)
                    .OfClass(typeof(View3D));
                foreach (View3D v in viewCollector)
                {
                    if (!v.IsTemplate)
                    {
                        view3D = v;
                        break;
                    }
                }

                if (view3D == null)
                {
                    using (Transaction trans = new Transaction(doc, "Create 3D View"))
                    {
                        trans.Start();
                        ViewFamilyType vft = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional);

                        if (vft != null)
                        {
                            view3D = View3D.CreateIsometric(doc, vft.Id);
                        }
                        trans.Commit();
                    }
                }

                if (view3D == null)
                {
                    TaskDialog.Show("Error", "Unable to create or retrieve a 3D view.");
                    return null;
                }

                ElementFilter classFilter;
                switch (hostingBehavior)
                {
                    case 1: // Wall based
                        classFilter = new ElementClassFilter(typeof(Wall));
                        break;
                    case 2: // Floor based
                        classFilter = new ElementClassFilter(typeof(Floor));
                        break;
                    case 3: // Ceiling based
                        classFilter = new ElementClassFilter(typeof(Ceiling));
                        break;
                    case 4: // Roof based
                        classFilter = new ElementClassFilter(typeof(RoofBase));
                        break;
                    default:
                        return null;
                }

                XYZ[] directions = new XYZ[]
                {
                    XYZ.BasisX,
                    -XYZ.BasisX,
                    XYZ.BasisY,
                    -XYZ.BasisY,
                    XYZ.BasisZ,
                    -XYZ.BasisZ
                };

                ReferenceIntersector refIntersector = new ReferenceIntersector(classFilter,
                    FindReferenceTarget.Element, view3D);
                refIntersector.FindReferencesInRevitLinks = true;

                double minDistance = double.MaxValue;
                Element nearestHost = null;

                foreach (XYZ direction in directions)
                {
                    IList<ReferenceWithContext> references = refIntersector.Find(location, direction);

                    foreach (ReferenceWithContext rwc in references)
                    {
                        double distance = rwc.Proximity;

                        if (distance <= radius && distance < minDistance)
                        {
                            minDistance = distance;
                            nearestHost = doc.GetElement(rwc.GetReference().ElementId);
                        }
                    }
                }

                return nearestHost;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", $"An error occurred while retrieving the nearest host element: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Finds the nearest wall to a point using wall location-line distance calculation.
        /// More reliable than ray-casting for door/window placement.
        /// </summary>
        /// <param name="doc">Current Revit document</param>
        /// <param name="point">Target point (internal units, feet)</param>
        /// <param name="level">Level to filter walls on</param>
        /// <param name="tolerance">Extra tolerance beyond half wall width (feet). Default ~5mm.</param>
        /// <returns>Tuple of (wall, projectedPoint, wallDirection, distance) or null</returns>
        public static (Wall wall, XYZ projectedPoint, XYZ wallDirection, double distance)?
            GetNearestWallByLocationLine(
                this Document doc,
                XYZ point,
                Level level,
                double tolerance = 5.0 / 304.8)
        {
            if (doc == null || point == null || level == null)
                return null;

            // Collect all walls on the given level
            var walls = new FilteredElementCollector(doc)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .Where(w =>
                {
                    Parameter baseLevelParam = w.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
                    return baseLevelParam != null && baseLevelParam.AsElementId() == level.Id;
                })
                .ToList();

            Wall bestWall = null;
            XYZ bestProjection = null;
            XYZ bestDirection = null;
            double bestDistance = double.MaxValue;

            foreach (Wall wall in walls)
            {
                LocationCurve locCurve = wall.Location as LocationCurve;
                if (locCurve == null) continue;

                Curve curve = locCurve.Curve;
                if (curve == null) continue;

                // Use Curve.Project() which handles both lines and arcs
                IntersectionResult ir = curve.Project(new XYZ(point.X, point.Y, curve.GetEndPoint(0).Z));
                if (ir == null) continue;

                XYZ projectedPt = ir.XYZPoint;
                double distance = new XYZ(point.X - projectedPt.X, point.Y - projectedPt.Y, 0).GetLength();

                // Check if point is within half the wall width + tolerance
                double halfWidth = wall.Width / 2.0;
                if (distance <= halfWidth + tolerance && distance < bestDistance)
                {
                    bestDistance = distance;
                    bestWall = wall;
                    bestProjection = new XYZ(projectedPt.X, projectedPt.Y, point.Z);

                    // Compute wall direction from curve tangent at projected parameter
                    XYZ p0 = curve.GetEndPoint(0);
                    XYZ p1 = curve.GetEndPoint(1);
                    bestDirection = new XYZ(p1.X - p0.X, p1.Y - p0.Y, 0).Normalize();
                }
            }

            if (bestWall == null)
                return null;

            return (bestWall, bestProjection, bestDirection, bestDistance);
        }

        /// <summary>
        /// </summary>
        public static void HighlightFace(this Document doc, Reference faceRef)
        {
            if (faceRef == null) return;

            FillPatternElement solidFill = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(x => x.GetFillPattern().IsSolidFill);

            if (solidFill == null)
            {
                TaskDialog.Show("Error", "No solid fill pattern was found.");
                return;
            }

            OverrideGraphicSettings ogs = new OverrideGraphicSettings();
            ogs.SetSurfaceForegroundPatternColor(new Color(255, 0, 0));
            ogs.SetSurfaceForegroundPatternId(solidFill.Id);
            ogs.SetSurfaceTransparency(0);

            doc.ActiveView.SetElementOverrides(faceRef.ElementId, ogs);
        }

        /// <summary>
        /// </summary>
        public static (XYZ PrimaryDirection, XYZ SecondaryDirection) GetMainDirections(this Face face)
        {
            if (face == null)
                throw new ArgumentNullException(nameof(face), "Face cannot be null.");

            XYZ faceNormal = face.ComputeNormal(new UV(0.5, 0.5));

            EdgeArrayArray edgeLoops = face.EdgeLoops;
            if (edgeLoops.Size == 0)
                throw new ArgumentException("Face has no valid edge loops.", nameof(face));

            EdgeArray outerLoop = edgeLoops.get_Item(0);

            List<XYZ> edgeDirections = new List<XYZ>();
            List<double> edgeLengths = new List<double>();

            foreach (Edge edge in outerLoop)
            {
                Curve curve = edge.AsCurve();
                XYZ startPoint = curve.GetEndPoint(0);
                XYZ endPoint = curve.GetEndPoint(1);

                XYZ direction = endPoint - startPoint;
                double length = direction.GetLength();

                if (length > 1e-10)
                {
                    edgeDirections.Add(direction.Normalize());
                    edgeLengths.Add(length);
                }
            }

            if (edgeDirections.Count < 4)
            {
                throw new ArgumentException("The provided face does not have enough edges to form a valid shape.", nameof(face));
            }

            List<List<int>> directionGroups = new List<List<int>>();

            for (int i = 0; i < edgeDirections.Count; i++)
            {
                bool foundGroup = false;
                XYZ currentDirection = edgeDirections[i];

                for (int j = 0; j < directionGroups.Count; j++)
                {
                    var group = directionGroups[j];
                    XYZ groupAvgDir = CalculateWeightedAverageDirection(group, edgeDirections, edgeLengths);

                    double dotProduct = Math.Abs(groupAvgDir.DotProduct(currentDirection));
                    if (dotProduct > 0.8)
                    {
                        group.Add(i);
                        foundGroup = true;
                        break;
                    }
                }

                if (!foundGroup)
                {
                    List<int> newGroup = new List<int> { i };
                    directionGroups.Add(newGroup);
                }
            }

            List<double> groupWeights = new List<double>();
            List<XYZ> groupDirections = new List<XYZ>();

            foreach (var group in directionGroups)
            {
                double totalLength = 0;
                foreach (int edgeIndex in group)
                {
                    totalLength += edgeLengths[edgeIndex];
                }
                groupWeights.Add(totalLength);

                groupDirections.Add(CalculateWeightedAverageDirection(group, edgeDirections, edgeLengths));
            }

            int[] sortedIndices = Enumerable.Range(0, groupDirections.Count)
                .OrderByDescending(i => groupWeights[i])
                .ToArray();

            if (groupDirections.Count >= 2)
            {
                int primaryIndex = sortedIndices[0];
                int secondaryIndex = sortedIndices[1];

                return (
                    PrimaryDirection: groupDirections[primaryIndex],
                    SecondaryDirection: groupDirections[secondaryIndex]
                );
            }
            else if (groupDirections.Count == 1)
            {
                XYZ primaryDirection = groupDirections[0];
                XYZ secondaryDirection = faceNormal.CrossProduct(primaryDirection).Normalize();

                return (
                    PrimaryDirection: primaryDirection,
                    SecondaryDirection: secondaryDirection
                );
            }
            else
            {
                throw new InvalidOperationException("Unable to extract valid directions from the face.");
            }
        }

        /// <summary>
        /// </summary>
        public static XYZ CalculateWeightedAverageDirection(List<int> edgeIndices, List<XYZ> directions, List<double> lengths)
        {
            if (edgeIndices.Count == 0)
                return null;

            double sumX = 0, sumY = 0, sumZ = 0;
            XYZ referenceDir = directions[edgeIndices[0]];

            foreach (int i in edgeIndices)
            {
                XYZ currentDir = directions[i];

                double dot = referenceDir.DotProduct(currentDir);

                double factor = (dot >= 0) ? lengths[i] : -lengths[i];

                sumX += currentDir.X * factor;
                sumY += currentDir.Y * factor;
                sumZ += currentDir.Z * factor;
            }

            XYZ avgDir = new XYZ(sumX, sumY, sumZ);
            double magnitude = avgDir.GetLength();

            if (magnitude < 1e-10)
                return referenceDir;

            return avgDir.Normalize();
        }

        /// <summary>
        /// </summary>
        public static bool IsRightHandRuleCompliant(this XYZ thumb, XYZ indexFinger, XYZ middleFinger, double tolerance = 1e-6)
        {
            double dotThumbIndex = Math.Abs(thumb.DotProduct(indexFinger));
            double dotThumbMiddle = Math.Abs(thumb.DotProduct(middleFinger));
            double dotIndexMiddle = Math.Abs(indexFinger.DotProduct(middleFinger));

            bool areOrthogonal = (dotThumbIndex <= tolerance) &&
                                  (dotThumbMiddle <= tolerance) &&
                                  (dotIndexMiddle <= tolerance);

            if (!areOrthogonal)
                return false;

            XYZ crossProduct = indexFinger.CrossProduct(middleFinger);
            double rightHandTest = crossProduct.DotProduct(thumb);

            return rightHandTest > tolerance;
        }

        /// <summary>
        /// </summary>
        public static XYZ GenerateIndexFinger(this XYZ thumb, XYZ middleFinger, double tolerance = 1e-6)
        {
            XYZ normalizedThumb = thumb.Normalize();
            XYZ normalizedMiddleFinger = middleFinger.Normalize();

            double dotProduct = normalizedThumb.DotProduct(normalizedMiddleFinger);

            if (Math.Abs(dotProduct) > tolerance)
            {
                return null;
            }

            XYZ indexFinger = normalizedMiddleFinger.CrossProduct(normalizedThumb).Negate();

            return indexFinger.Normalize();
        }

        /// <summary>
        /// </summary>
        /// <returns></returns>
        public static Level CreateOrGetLevel(this Document doc, double elevation, string levelName)
        {
            Level existingLevel = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault(l => Math.Abs(l.Elevation - elevation) < 0.1 / 304.8);

            if (existingLevel != null)
                return existingLevel;

            Level newLevel = Level.Create(doc, elevation);
            Level namesakeLevel = new FilteredElementCollector(doc)
                 .OfClass(typeof(Level))
                 .Cast<Level>()
                 .FirstOrDefault(l => l.Name == levelName);
            if (namesakeLevel != null)
            {
                levelName = $"{levelName}_{newLevel.Id.GetValue()}";
            }
            newLevel.Name = levelName;

            return newLevel;
        }

        /// <summary>
        /// </summary>
        public static Level FindNearestLevel(this Document doc, double height)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc), "Document cannot be null.");

            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(level => Math.Abs(level.Elevation - height))
                .FirstOrDefault();
        }

        ///// <summary>
        ///// </summary>
        //public static void Refresh(this Document doc, int waitingTime = 0, bool allowOperation = true)
        //{
        //    UIApplication uiApp = new UIApplication(doc.Application);
        //    UIDocument uiDoc = uiApp.ActiveUIDocument;

        //    if (uiDoc.Document.IsModifiable)
        //    {
        //        uiDoc.Document.Regenerate();
        //    }
        //    uiDoc.RefreshActiveView();

        //    if (waitingTime != 0)
        //    {
        //        System.Threading.Thread.Sleep(waitingTime);
        //    }

        //    if (allowOperation)
        //    {
        //        System.Windows.Forms.Application.DoEvents();
        //    }
        //}

        /// <summary>
        /// </summary>
        public static void SaveToDesktop(this string message, string fileName = "temp.json", bool isAppend = false)
        {
            if (!Path.HasExtension(fileName))
            {
                fileName += ".txt";
            }

            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

            string filePath = Path.Combine(desktopPath, fileName);

            using (StreamWriter sw = new StreamWriter(filePath, isAppend))
            {
                sw.WriteLine($"{message}");
            }
        }

    }
}
