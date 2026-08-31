using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RevitMCPCommandSet.Models.Common
{
    /// <summary>
    /// Filter settings that support combined conditions.
    /// </summary>
    public class FilterSetting
    {
        /// <summary>
        /// Gets or sets the Revit built-in category name to filter, such as "OST_Walls".
        /// No category filter is applied when null or empty.
        /// </summary>
        [JsonProperty("filterCategory")]
        public string FilterCategory { get; set; } = null;
        /// <summary>
        /// Gets or sets the Revit element type name to filter, such as "Wall" or "Autodesk.Revit.DB.Wall".
        /// No type filter is applied when null or empty.
        /// </summary>
        [JsonProperty("filterElementType")]
        public string FilterElementType { get; set; } = null;
        /// <summary>
        /// Gets or sets the ElementId of the FamilySymbol to filter.
        /// No family filter is applied when zero or negative.
        /// This filter applies only to element instances, not element types.
        /// </summary>
        [JsonProperty("filterFamilySymbolId")]
        public int FilterFamilySymbolId { get; set; } = -1;
        /// <summary>
        /// Gets or sets whether to include element types, such as wall and door types.
        /// </summary>
        [JsonProperty("includeTypes")]
        public bool IncludeTypes { get; set; } = false;
        /// <summary>
        /// Gets or sets whether to include element instances, such as placed walls and doors.
        /// </summary>
        [JsonProperty("includeInstances")]
        public bool IncludeInstances { get; set; } = true;
        /// <summary>
        /// Gets or sets whether to return only elements visible in the current view.
        /// This filter applies only to element instances, not element types.
        /// </summary>
        [JsonProperty("filterVisibleInCurrentView")]
        public bool FilterVisibleInCurrentView { get; set; }
        /// <summary>
        /// Gets or sets the minimum point for spatial filtering in millimeters.
        /// When set with BoundingBoxMax, returns elements intersecting the bounding box.
        /// </summary>
        [JsonProperty("boundingBoxMin")]
        public JZPoint BoundingBoxMin { get; set; } = null;
        /// <summary>
        /// Gets or sets the maximum point for spatial filtering in millimeters.
        /// When set with BoundingBoxMin, returns elements intersecting the bounding box.
        /// </summary>
        [JsonProperty("boundingBoxMax")]
        public JZPoint BoundingBoxMax { get; set; } = null;
        /// <summary>
        /// Maximum number of elements to return.
        /// </summary>
        [JsonProperty("maxElements")]
        public int MaxElements { get; set; } = 50; 
        /// <summary>
        /// Validates the filter settings and checks for conflicts.
        /// </summary>
        /// <returns>True when the settings are valid; otherwise, false.</returns>
        public bool Validate(out string errorMessage)
        {
            errorMessage = null;

            // Check that at least one element kind is included.
            if (!IncludeTypes && !IncludeInstances)
            {
                errorMessage = "Invalid filter settings: include at least one of element types or element instances.";
                return false;
            }

            // Check that at least one filter criterion is specified.
            if (string.IsNullOrWhiteSpace(FilterCategory) &&
                string.IsNullOrWhiteSpace(FilterElementType) &&
                FilterFamilySymbolId <= 0)
            {
                errorMessage = "Invalid filter settings: specify at least one filter criterion (category, element type, or family type).";
                return false;
            }

            // Check for filters that conflict with type-only results.
            if (IncludeTypes && !IncludeInstances)
            {
                List<string> invalidFilters = new List<string>();
                if (FilterFamilySymbolId > 0)
                    invalidFilters.Add("family instance filter");
                if (FilterVisibleInCurrentView)
                    invalidFilters.Add("view visibility filter");
                if (invalidFilters.Count > 0)
                {
                    errorMessage = $"The following filters do not apply when filtering element types only: {string.Join(", ", invalidFilters)}";
                    return false;
                }
            }
            // Check spatial filter validity.
            if (BoundingBoxMin != null && BoundingBoxMax != null)
            {
                // Ensure the minimum point is not greater than the maximum point.
                if (BoundingBoxMin.X > BoundingBoxMax.X ||
                    BoundingBoxMin.Y > BoundingBoxMax.Y ||
                    BoundingBoxMin.Z > BoundingBoxMax.Z)
                {
                    errorMessage = "Invalid spatial filter settings: minimum point coordinates must be less than or equal to maximum point coordinates.";
                    return false;
                }
            }
            else if (BoundingBoxMin != null || BoundingBoxMax != null)
            {
                errorMessage = "Invalid spatial filter settings: both minimum and maximum point coordinates must be set.";
                return false;
            }
            return true;
        }
    }
}
