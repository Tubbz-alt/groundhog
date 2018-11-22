﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using groundhog.Properties;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Rhino;
using Rhino.Geometry;

namespace groundhog
{
    public class GroundhogChannelInfoComponent : GroundHogComponent
    {
        public GroundhogChannelInfoComponent()
            : base("Channel Info", "CInfo", "Calculate characteristics of water flow in a channel from its submerged region", "Groundhog", "Hydro")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override Bitmap Icon => Resources.icon_channel_info;

        public override Guid ComponentGuid => new Guid("{008255a6-edff-44d9-b96f-23eb050b4a1a}");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Channel", "C", "A closed planar curve representing a section of the water body; assumes a level top", GH_ParamAccess.item);
            pManager.AddNumberParameter("Roughness", "N", "Manning's roughness coefficient for the channel", GH_ParamAccess.item);
            pManager[1].Optional = true;
            pManager.AddNumberParameter("Slope", "S", "Slope of the hydraulic grade line as a decimal (i.e. rise/run = 0.5)", GH_ParamAccess.item);
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Area", "A", "Area of the channel", GH_ParamAccess.item);
            pManager.AddNumberParameter("Max Depth", "mD", "Maximum depth of the channel", GH_ParamAccess.item);
            pManager.AddNumberParameter("Mean Depth", "aD", "Mean depth of the channel", GH_ParamAccess.item);
            pManager.AddNumberParameter("Wetted Perimeter", "P", "Length of the channel in the boundary", GH_ParamAccess.item);
            pManager.AddNumberParameter("Hydraulic Radius", "R", "Ratio of area to wetted perimeter", GH_ParamAccess.item);
            pManager.AddNumberParameter("Velocity", "V", "Velocity of the water flow in the channel", GH_ParamAccess.item);
            pManager.AddNumberParameter("Discharge", "Q", "The rate of discharge in cubic document units per second", GH_ParamAccess.item);
        }

        protected override void GroundHogSolveInstance(IGH_DataAccess DA)
        {
            // Create holder variables for input parameters
            var CHANNEL_CURVE = default(Curve);
            var CHANNEL_PLANE = default(Plane);
            var TOLERANCE = RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
            double GAUCKLER_MANNING = 0.0; // Default value
            double SLOPE = 0.0; // Default value

            // Access and extract data from the input parameters individually
            if (!DA.GetData(0, ref CHANNEL_CURVE)) return;
            DA.GetData(1, ref GAUCKLER_MANNING);
            DA.GetData(2, ref SLOPE);

            // Validation
            if (CHANNEL_CURVE.TryGetPlane(out CHANNEL_PLANE, TOLERANCE) == false)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "A non-planar curve has been provided as the channel section; please ensure it is planar.");
                return;
            }
            if (CHANNEL_CURVE.IsClosed == false)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "A non-closed curve has been provided as the channel section; please ensure it is closed.");
                return;
            }
            if (GAUCKLER_MANNING > 0 && SLOPE <= 0 || GAUCKLER_MANNING <= 0 && SLOPE > 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Calculating velocity and discarge requires both the slope and the roughness coefficient.");
                return;
            }

            // Calculate area
            var area_calc = AreaMassProperties.Compute(CHANNEL_CURVE);
            double? area = null;
            if (area_calc != null)
            {
                if (area_calc.Area < 0.0)
                    area = area_calc.Area * -1; // Areas can be negative; same same
                else
                    area = area_calc.Area;
            }

            // Calculate the bounding box for the curve
            var bbox = CHANNEL_CURVE.GetBoundingBox(CHANNEL_PLANE);

            double maxDepth = bbox.Diagonal.X; // Distance from high to low
            double maxWidth = bbox.Diagonal.Y; // Distance from side to side

            // Mean Depth = cross-sectional area divided by the surface width
            double meanDepth = area.Value / maxWidth;

            // Wetted Perimeter = the channel curve that is not the top segment
            double wettedPerimeter = CHANNEL_CURVE.GetLength() - maxWidth; // Basically ignore the top channel

            // Hydraulic Radius = area divided by the wetted perimeter
            double hydraulicRadius = area.Value / wettedPerimeter;

            // Assign variables to output parameters
            DA.SetData(0, area);
            DA.SetData(1, maxDepth);
            DA.SetData(2, meanDepth);
            DA.SetData(3, wettedPerimeter);
            DA.SetData(4, hydraulicRadius);

            if (GAUCKLER_MANNING > 0 && SLOPE > 0)
            {
                // Manning's Formula
                double discharge = (1.00 / GAUCKLER_MANNING) * area.Value * Math.Pow(hydraulicRadius, 0.6666666) * Math.Sqrt(SLOPE);
                double velocity = discharge / area.Value;

                DA.SetData(5, velocity);
                DA.SetData(6, discharge);
            }
        }
    }
}
