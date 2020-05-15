﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Geometry;
using Grasshopper.Kernel.Geometry.Voronoi;
using groundhog.Properties;
using Rhino.Display;
using Rhino.Geometry;

namespace groundhog
{
    public class GroundhogCatchmentComponent : GroundHogComponent
    {
        public GroundhogCatchmentComponent()
            : base("Flow Catchments", "Catchments", "Identify the catchments within a set of flow paths", "Groundhog",
                "Hydro")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override Bitmap Icon => Resources.icon_flows_catchments;

        public override Guid ComponentGuid => new Guid("{2d241bdc-ecaa-4cf3-815a-c8001d1798d1}");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Flow Paths", "C", "The flow paths as generated by the flows component",
                GH_ParamAccess.list);
            pManager[0].Optional = false;
            pManager.AddNumberParameter("Proximity Threshold", "T",
                "The distance between end points required to form a catchment", GH_ParamAccess.item);
            pManager[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Catchments", "B", "The catchment boundaries identified", GH_ParamAccess.tree);
            pManager.AddCurveParameter("Flow Paths", "P", "The flow paths grouped by catchment", GH_ParamAccess.tree);
            pManager.AddColourParameter("Color Codes", "C", "Colour codes the uniquely identify each path and boundary",
                GH_ParamAccess.tree);
            pManager.AddNumberParameter("Volumes %", "V", "The % of the total flow paths that drain to this area",
                GH_ParamAccess.tree);
        }

        protected override void GroundHogSolveInstance(IGH_DataAccess DA)
        {
            // Create holder variables for input parameters
            var FLOW_PATHS = new List<Curve>();
            double MIN_PROXIMITY = 0;

            // Access and extract data from the input parameters individually
            DA.GetDataList(0, FLOW_PATHS);
            DA.GetData(1, ref MIN_PROXIMITY);

            // TODO: add a warning/note that these should be a flat list
            //AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Make a flat list");
            if (MIN_PROXIMITY < 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Proximity threshold must be a positive number.");
                return;
            }

            if (MIN_PROXIMITY == 0)
            {
                Polyline samplePath; // Attempt to guess a good proximity amount
                if (FLOW_PATHS[0].TryGetPolyline(out samplePath))
                {
                    MIN_PROXIMITY = samplePath.SegmentAt(0).Length * 2;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        $"Proximity Threshold parameter not provided; guessed {MIN_PROXIMITY} as a good value");
                }
            }

            // Remove null items; can be due to passing in the points not the path
            FLOW_PATHS.RemoveAll(curve => curve == null);
            if (FLOW_PATHS.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "No Flow Paths provided or they were provided as an inappropriate geometry.");
                return;
            }

            // Create the flowStructure objects
            var flowStructures = new List<FlowStructure>();
            for (var i = 0; i < FLOW_PATHS.Count; i++)
                flowStructures.Add(new FlowStructure(FLOW_PATHS[i], i, MIN_PROXIMITY));

            // Create voronoi cells and associate with each flow object
            CreateVoronoi(flowStructures);

            // Search through each point, check distance apart and assign to groups based on this
            foreach (var originFlowStructure in flowStructures)
            {
                var originPt = originFlowStructure.end;
                foreach (var searchFlowStructure in flowStructures)
                    if (originFlowStructure != searchFlowStructure)
                    {
                        var searchPt = searchFlowStructure.end;
                        var distance = originPt.DistanceTo(searchPt);
                        if (distance <= searchFlowStructure.groupDistance + docUnitTolerance)
                        {
                            // Then check if its closer to the previous match (and by default: the proximity)
                            searchFlowStructure.groupIndex = originFlowStructure.groupIndex;
                            searchFlowStructure.groupDistance = distance; // We set the new distance to beat
                        }
                    }
            }

            // Create an array of points lists
            var holdingListCurves = new List<Curve>[flowStructures.Count];
            var holdingListBounds = new List<Polyline>[flowStructures.Count];
            var holdingListBoundsCurves = new Curve[flowStructures.Count][];

            foreach (var flowStructure in flowStructures)
            {
                // For each catchment group instantiate a list and add paths that match that group
                if (holdingListCurves[flowStructure.groupIndex] == null)
                {
                    // Need to add the list if it doesn't exist
                    holdingListCurves[flowStructure.groupIndex] = new List<Curve>();
                    holdingListBounds[flowStructure.groupIndex] = new List<Polyline>();
                }

                holdingListCurves[flowStructure.groupIndex].Add(flowStructure.curve);
                holdingListBounds[flowStructure.groupIndex].Add(flowStructure.catchment);
            }

            // To unify adjacent and connected catchment curves we explode all the cells, delete duplicates and rejoin
            for (var v = 0; v < flowStructures.Count; v++)
                if (holdingListBounds[v] != null) // Check the group exists
                {
                    // Explode all polylines into their segments
                    var groupEdges = new List<Line>();
                    for (var i = 0; i < holdingListBounds[v].Count; i++)
                        if (holdingListBounds[v][i] != null
                        ) // Null when just a point? Or provided a blank data tree item?
                        {
                            var test = holdingListBounds[v][i];
                            var segmentLengths = holdingListBounds[v][i].GetSegments();
                            var segmentCounts = segmentLengths.Length;
                            groupEdges.AddRange(holdingListBounds[v][i].GetSegments()); //REENABLE: THIS EXPLODES IT
                        }

                    var culledEdges = DeduplicateLines(groupEdges);

                    var sortedCurves = new List<Curve>();
                    foreach (var line in culledEdges)
                        sortedCurves.Add(line.ToNurbsCurve());

                    holdingListBoundsCurves[v] = Curve.JoinCurves(sortedCurves);
                }


            // Create the branch structures where each path is a catchment group and add the relevant geometry
            var groupedCurves = new DataTree<Curve>();
            var groupedBounds = new DataTree<Curve>();
            var groupedColors = new DataTree<Color>();
            var groupedVolumes = new DataTree<double>();

            // We want the colors to randomly pick from an available index as their seed; otherwise adjacent cells have similar valuesList<int> iList = new List<int>();
            var colorIndices = Enumerable.Range(0, holdingListCurves.Length).ToList();
            var shuffledIndices = colorIndices.OrderBy(a => Guid.NewGuid());

            for (var i = 0; i < holdingListCurves.Length; i++)
                if (holdingListCurves[i] != null)
                {
                    var nextPath = groupedCurves.Paths.Count;
                    groupedCurves.EnsurePath(nextPath);
                    groupedBounds.EnsurePath(nextPath);
                    groupedColors.EnsurePath(nextPath);
                    groupedVolumes.EnsurePath(nextPath);
                    groupedCurves.AddRange(holdingListCurves[i], groupedCurves.Path(nextPath));
                    groupedBounds.AddRange(holdingListBoundsCurves[i], groupedBounds.Path(nextPath));
                    groupedColors.AddRange(
                        GenerateGroupColors(shuffledIndices.ElementAt(i), holdingListCurves[i].Count,
                            holdingListCurves.Length), groupedColors.Path(nextPath)
                    );
                    double flowVolumesPercent = (double)holdingListCurves[i].Count / FLOW_PATHS.Count;
                    groupedVolumes.Add(flowVolumesPercent);
                }

            // Assign variables to output parameters
            DA.SetDataTree(0, groupedBounds);
            DA.SetDataTree(1, groupedCurves);
            DA.SetDataTree(2, groupedColors);
            DA.SetDataTree(3, groupedVolumes);
        }

        private List<Color> GenerateGroupColors(int groupIndex, int groupSize, int groupsCount)
        {
            // Here are want to create a rectangularly shaped matrix to try and maximise contrast
            // Given an area of X, we want to find x/y lengths given an 2:1 ratio
            var ratio = groupsCount / 2.0;
            var square = Math.Sqrt(ratio);
            var xMax = (int) Math.Floor(square * 2.0) + 1;
            var yMax = (int) Math.Floor(square * 1.0);

            // Once we have the lengths we go from the index to the x/y position
            int x;
            if (groupIndex == 0)
                x = 0;
            else
                x = groupIndex % xMax; // Get position on x axis
            int y;
            if (groupIndex == 0)
            {
                y = 0;
            }
            else
            {
                double yPos = groupIndex / xMax;
                y = (int) Math.Floor(yPos); // Get position on y axis
            }

            //Print("{2}: {0} {1} maxes: {3} {4}", x.ToString(), y.ToString(), groupIndex.ToString(), xMax.ToString(), yMax.ToString());

            // Create a color from within a given range (set bounds to ensure things are relatively bright/distinct)
            var hue = ColorDistributionInRange(0.0, 1.0, x, xMax); // Not -1 as 0.0 and 1.0 are equivalent
            var saturation = 1.0; // Maximise contrast
            var luminance =
                ColorDistributionInRange(0.2, 0.6, y, yMax - 1); // -1 as we want to use the full range or 0.2-0.6
            var groupColorHSL = new ColorHSL(hue, saturation, luminance);

            // Convert to RGB and make a list with a color for each item in the branch
            var groupColorARGB = groupColorHSL.ToArgbColor();
            var groupColors = Enumerable.Repeat(groupColorARGB, groupSize).ToList();

            return groupColors;
        }

        private double ColorDistributionInRange(double lower, double upper, int index, int count)
        {
            var step = (upper - lower) / count;
            var value = lower + step * index;
            return value;
        }

        private List<Line> DeduplicateLines(List<Line> inputLines)
        {
            var lines = inputLines.OrderBy(o => o.Length).ToList(); // Sort so we can break after a no match
            var duplicateIndices = new List<int>();

            for (var i = lines.Count - 1; i >= 0; i--)
            for (var j = i - 1; j >= 0; j--)
            {
                var d1 = Math.Abs(lines[i].From.DistanceTo(lines[j].From));
                var d2 = Math.Abs(lines[i].From.DistanceTo(lines[j].To));
                var d3 = Math.Abs(lines[i].To.DistanceTo(lines[j].From));
                var d4 = Math.Abs(lines[i].To.DistanceTo(lines[j].To));

                if ((d1 < docUnitTolerance || d2 < docUnitTolerance) &&
                    (d3 < docUnitTolerance || d4 < docUnitTolerance))
                {
                    duplicateIndices.Add(j);
                    duplicateIndices.Add(i);
                }
                else
                {
                    break;
                }
            }

            // Remove duplicate indices and cull the list
            foreach (var indice in duplicateIndices.Distinct().ToList().OrderByDescending(x => x))
                lines.RemoveAt(indice);
            return lines;
        }

        private void CreateVoronoi(List<FlowStructure> flowStructures)
        {
            // Get the starts from everything
            var allStartPoints = flowStructures.Select(x => x.start).ToList();
            // Make a 2d list of all the start points
            var nodes = new Node2List(allStartPoints);

            // Create a 2d list that forms the boundary
            var outline = new List<Node2>();
            // Get sorted lists of coordinates to guestimate a boundary rectangle

            var sortedX = allStartPoints.OrderByDescending(item => item.X);
            var sortedY = allStartPoints.OrderByDescending(item => item.Y);
            outline.Add(new Node2(sortedX.First().X, sortedY.First().Y));
            outline.Add(new Node2(sortedX.First().X, sortedY.Last().Y));
            outline.Add(new Node2(sortedX.Last().X, sortedY.Last().Y));
            outline.Add(new Node2(sortedX.Last().X, sortedY.First().Y));

            // TODO: a delauney first so the brute force isn't needed, and the topology can be used to group

            var voronoiCells = Solver.Solve_BruteForce(nodes, outline);
            // Cells are order as the nodes were; so we can loop through both
            for (var i = 0; i < voronoiCells.Count; i++)
                flowStructures[i].catchment = voronoiCells[i].ToPolyline();
        }

        private class FlowStructure
        {
            public readonly Curve curve;
            public readonly Point3d end;
            public readonly Point3d start;
            public Polyline catchment;
            public double groupDistance;
            public int groupIndex;

            // Constructor
            public FlowStructure(Curve flowCurve, int i, double minProximity)
            {
                curve = flowCurve;
                start = flowCurve.PointAtStart;
                end = flowCurve.PointAtEnd;
                groupIndex = i;
                groupDistance = minProximity;
            }
        }
    }
}