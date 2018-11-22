﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using groundhog.Properties;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Rhino;
using Rhino.Geometry;

namespace groundhog
{
    public class GroundhogChannelRegionComponent : GroundHogComponent
    {
        public GroundhogChannelRegionComponent()
            : base("Channel Region", "CRegion", "Determine the submerged region of a channel given a quantity of water", "Groundhog", "Hydro")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override Bitmap Icon => Resources.icon_channel_region;

        public override Guid ComponentGuid => new Guid("{ffba138d-a959-4dbc-988d-c1ef761b8e79}");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Channel", "C", "The sectional curve profile of the channel; must be planar and vertically-aligned (i.e. it fills up in the Z-axis)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Area", "A", "The desired area of the flow body. If unspecified it will try to guess a sensible value to use that can serve as a reference", GH_ParamAccess.item);
            pManager[1].Optional = true;
            pManager.AddNumberParameter("Precision", "T", "The number of units to be accurate to in finding a matching area. If unspecified it will use 0.01% of the area. Smaller values will take longer to calculate.", GH_ParamAccess.item);
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Channel(s)", "C", "The perimeter(s) of the calculated water body or bodies", GH_ParamAccess.list);
            pManager.AddNumberParameter("Area(s)", "A", "The area of the calculated water body or bodies", GH_ParamAccess.list);
            // pManager.AddCurveParameter("DEBUG", "D", "The perimeter(s) of the calculated water body", GH_ParamAccess.tree); // DEBUG
        }

        protected override void GroundHogSolveInstance(IGH_DataAccess DA)
        {
            // Create holder variables for input parameters
            double TOLERANCE = RhinoDoc.ActiveDoc.ModelAbsoluteTolerance; // Default; overwritten if set
            var CHANNEL_CURVE = default(Curve);

            // Curve Validation
            if (!DA.GetData(0, ref CHANNEL_CURVE)) return;
            if (CHANNEL_CURVE == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "A null item has been provided as the Channel input; please correct this input.");
                return;
            }
            if (CHANNEL_CURVE.IsPlanar(TOLERANCE * 100) == false) // Increase tolerance; too many false positives
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "A non-planar curve has been provided as the channel section.");
                return;
            }
            var bbox = CHANNEL_CURVE.GetBoundingBox(true);
            if (!bbox.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Could not calculate bounding box for the curve");
                return;
            }

            // Area Validation + Guessing
            double AREA_TARGET = 0.0;
            DA.GetData(1, ref AREA_TARGET);
            if (AREA_TARGET < 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "The area target must be greater than 0.");
                return;
            }
            if (AREA_TARGET == 0)
            {
                double[] boxDimensions = new double[3] { bbox.Diagonal.X, bbox.Diagonal.Y, bbox.Diagonal.Z };
                Array.Sort(boxDimensions); // If its the box of a planar curve one of the dimensions will be 0; need to sort + ignore
                AREA_TARGET = boxDimensions[1] * boxDimensions[2] * 0.05; // Set area at 5% of bounding box area (bbox.Area property unavailable)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"The area target was unpsecified so has been set to {AREA_TARGET}.");
            }

            // Precision Validation + Guessing
            double AREA_PRECISION = 0.0;
            DA.GetData(2, ref AREA_PRECISION);
            if (AREA_PRECISION < 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "The area precision must be greater than 0.");
                return;
            }
            if (AREA_PRECISION <= 0)
            {
                AREA_PRECISION = AREA_TARGET * 0.01; // Default; overwritten if set
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"The area precision was unpsecified or less than 0 so has been set to {AREA_PRECISION}.");
            }

            // Get the extremes of the curve
            var lowerParam = bbox.Corner(true, true, true).Z; // Minimum X/Y/Z corner's Z-value
            var upperParam = bbox.Corner(false, false, false).Z; // Maximum X/Y/Z corner's Z-value

            // Placeholders for outputs
            var outputProfiles = new List<Curve>();
            var outputAreas = new List<double>();
            // var debugProfiles = new DataTree<Curve>();  // DEBUG

            // Use bisect method to refine to the approximate area
            double intervalBegin = lowerParam;
            double intervalEnd = upperParam; 
            double middle;
            double lastArea = 0.0;
            int iterations = 0;
            double zPrecision = 0.00001;

            while ((intervalEnd - intervalBegin) > zPrecision)
            {
                // Test parameter is halfway between the current upper and lower Z-bounds
                iterations += 1;
                middle = intervalBegin + ((intervalEnd - intervalBegin) / 2);

                // Find the actual channel geometries at that parameter
                var testChannels = GetWaterChannelsAtZHeight(middle, CHANNEL_CURVE, TOLERANCE);
                if (testChannels == null)
                {
                    break; // No test curve when <2 intersections; i.e. overflown perimeter
                }

                // Calculate its area
                var calculatedAreas = GetAreasForWaterChannels(testChannels);
                var totalArea = calculatedAreas.Sum();
                lastArea = totalArea;

                // DEBUG
                // var nextPath = debugProfiles.Paths.Count;
                // debugProfiles.EnsurePath(nextPath);
                // debugProfiles.AddRange(testChannels);

                // Refine the interval
                if (Math.Abs(AREA_TARGET - totalArea) <= AREA_PRECISION)
                {
                    // SUCCESS
                    outputProfiles.AddRange(testChannels);
                    outputAreas.AddRange(calculatedAreas);
                    break;
                }

                if (AREA_TARGET < totalArea)
                    intervalEnd = middle; // Reduce the upper bound as the sectional area is larger than desired
                else
                    intervalBegin = middle; // Reduce the lower bound as the sectional area is smaller than desired
            }

            // AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Completed in {iterations} iterations"); // DEBUG

            if (!outputProfiles.Any())
            {
                middle = intervalBegin + ((intervalEnd - intervalBegin) / 2);
                var errorArea = Math.Round(lastArea, 2); // Round for concision
                if (errorArea > 99)
                {
                    errorArea = Math.Round(errorArea);  // Round further for concision
                }

                if ((upperParam - middle) < (middle - lowerParam))
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"Area of {AREA_TARGET} exceeded the profile's capacity (largest area found was {errorArea}). Decrease the area to produce a solution.");
                else
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"Area of {AREA_TARGET} could not be found in the profile (smallest area found was {errorArea}; which was not in the margin of {AREA_PRECISION}). Increase the area or precision to produce a solution.");
            }

            // Assign variables to output parameters
            DA.SetDataList(0, outputProfiles);
            DA.SetDataList(1, outputAreas);
            // DA.SetDataTree(2, debugProfiles); // DEBUG
        }

        private List<double> GetAreasForWaterChannels(List<Curve> channels)
        {
            var areas = new List<double>();
            for (var i = 0; i < channels.Count; i = i + 1)
            {
                var area_calc = AreaMassProperties.Compute(channels[i]);
                if (area_calc != null)
                {
                    areas.Add(Math.Abs(area_calc.Area));
                }
            }
            return areas;
        }

        private List<Curve> GetWaterChannelsAtZHeight(double zHeight, Curve CHANNEL_CURVE, double TOLERANCE)
        {
            // Create an XY plane positioned vertically at the test point
            var test_plane = new Plane(new Point3d(0, 0, zHeight), new Vector3d(0, 0, 1));

            // Intersect Plane with the Curve to get water level(s)
            var intersections = Rhino.Geometry.Intersect.Intersection.CurvePlane(CHANNEL_CURVE, test_plane, TOLERANCE);
            if (intersections == null)
                return null;
            if (intersections.Count < 2)
                return null; // One or fewer intersections is not solvable

            var validIntersections = new List<Tuple<Point3d, double>>();
            // For each intersection event we check evaluate the curve slightly ahead and check whether the 
            // Z-position is down or up. A higher Z is not allowable as a starting point is a valid end
            for (var i = 0; i < intersections.Count; i = i + 1)
            {
                var currentCurvePoint = intersections[i].PointA;
                var currentCurveParameter = intersections[i].ParameterA;
                var pointFurtherAlongCurve = CHANNEL_CURVE.PointAt(currentCurveParameter + 0.01);
                if (pointFurtherAlongCurve.Z <= currentCurvePoint.Z)
                {
                    validIntersections.Add(Tuple.Create(currentCurvePoint, currentCurveParameter));
                    // Also add the next valid point along and skip processing it
                    i += 1;
                    if (i >= intersections.Count)
                    {
                        break; // Only add to the list when there is another point to pair with
                    }
                    validIntersections.Add(Tuple.Create(intersections[i].PointA, intersections[i].ParameterA));
                }
            }

            var channelCurves = new List<Curve>();
            // Loop over the valid interesections pairs and make them into bounded sub-curves (ala channels)
            for (var i = 0; i < validIntersections.Count; i = i + 2)
            {
                if (i + 1 >= validIntersections.Count)
                {
                    break; // Only add to the list when there is another point to pair with
                }
                var ixA = validIntersections[i];
                var ixB = validIntersections[i + 1];

                // Make an array of top water line and the sub channel then join to close
                var wettedLine = CHANNEL_CURVE.Trim(ixA.Item2, ixB.Item2); // Get the sub-curve of the channel
                var waterLine = new Line(ixA.Item1, ixB.Item1).ToNurbsCurve();

                Curve[] channel = Curve.JoinCurves(new Curve[] { wettedLine, waterLine });
                if (channel.Length > 0)
                    channelCurves.Add(channel[0]);
            }
            return channelCurves;
        }
    }
}