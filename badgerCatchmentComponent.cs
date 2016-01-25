﻿using System;
using System.Linq;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;

namespace badger
{
    public class badgerCatchmentComponent : GH_Component
    {
        /// <summary>
        /// Each implementation of GH_Component must provide a public 
        /// constructor without any arguments.
        /// Category represents the Tab in which the component will appear, 
        /// Subcategory the panel. If you use non-existing tab or panel names, 
        /// new tabs/panels will automatically be created.
        /// </summary>
        public badgerCatchmentComponent()
            : base("Flow Catchments", "Catchments",
                "Identify the catchments within a set of flow paths",
                "Badger", "Hydro")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Flow Paths", "C", "The flow paths as generated by the flows component", GH_ParamAccess.list);
            pManager[0].Optional = false;
            pManager.AddNumberParameter("Proximty Threshold", "T", "The distance between end points required to form a catchment", GH_ParamAccess.item);
            pManager[1].Optional = true;


        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Catchments", "C", "The catchment boundaries identified", GH_ParamAccess.tree);
            pManager.AddCurveParameter("Flow Paths", "P", "The flow paths grouped by catchment", GH_ParamAccess.tree);

            // Sometimes you want to hide a specific parameter from the Rhino preview.
            // You can use the HideParameter() method as a quick way:
            //pManager.HideParameter(0);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Create holder variables for input parameters
            List<Curve> FLOW_PATHS = new List<Curve>();
            double MIN_PROXIMITY = 0;
            double TOLERANCE = Rhino.RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;

            // Access and extract data from the input parameters individually
            DA.GetDataList(0, FLOW_PATHS);
            if (!DA.GetData(1, ref MIN_PROXIMITY)) return;

            // TODO: add a warning/note that these should be a flat list
            //AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Make a flat list");
            
            // Create the flowStructure objects
            List<FlowStructure> flowStructures = new List<FlowStructure>();
            for (int i = 0; i < FLOW_PATHS.Count; i++)
            {
                flowStructures.Add(new FlowStructure(FLOW_PATHS[i], i, MIN_PROXIMITY));
            }

            // Create voronoi cells and associate with each flow object
            createVoronoi(flowStructures);

            // Search through each point, check distance apart and assign to groups based on this
            foreach (FlowStructure originFlowStructure in flowStructures)
            {
                Point3d originPt = originFlowStructure.end;
                foreach (FlowStructure searchFlowStructure in flowStructures)
                {
                    if (originFlowStructure != searchFlowStructure)
                    {
                        Point3d searchPt = searchFlowStructure.end;
                        double distance = originPt.DistanceTo(searchPt);
                        if (distance <= searchFlowStructure.groupDistance + TOLERANCE)
                        {
                            // Then check if its closer to the previous match (and by default: the proximity)
                            searchFlowStructure.groupIndex = originFlowStructure.groupIndex;
                            searchFlowStructure.groupDistance = distance; // We set the new distance to beat
                        }
                    }
                }
            }

            // Create an array of points lists
            List<Curve>[] holdingListCurves = new List<Curve>[flowStructures.Count];
            List<Polyline>[] holdingListBounds = new List<Polyline>[flowStructures.Count];
            Curve[][] holdingListBoundsCurves = new Curve[flowStructures.Count][];

            foreach (FlowStructure flowStructure in flowStructures)
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
            for (int v = 0; v < flowStructures.Count; v++)
            {
                if (holdingListBounds[v] != null) // Check the group exists
                {
                    // Explode all polylines into their segments
                    List<Line> groupEdges = new List<Line>();
                    for (int i = 0; i < holdingListBounds[v].Count; i++)
                    {
                        groupEdges.AddRange(holdingListBounds[v][i].GetSegments()); //REENABLE: THIS EXPLODES IT
                    }

                    List<Line> culledEdges = deduplicateLines(groupEdges, TOLERANCE);

                    List<Curve> sortedCurves = new List<Curve>();
                    foreach (Line line in culledEdges)
                    {
                        sortedCurves.Add(line.ToNurbsCurve());
                    }

                    holdingListBoundsCurves[v] = Curve.JoinCurves(sortedCurves);

                }
            }

            // Create the branch structures where each path is a catchment group and add the relevant geometry
            Grasshopper.DataTree<Curve> groupedCurves = new Grasshopper.DataTree<Curve>();
            Grasshopper.DataTree<Curve> groupedBounds = new Grasshopper.DataTree<Curve>();
            for (int i = 0; i < holdingListCurves.Length; i++)
            {
                if (holdingListCurves[i] != null)
                {
                    int nextPath = groupedCurves.Paths.Count;
                    groupedCurves.EnsurePath(nextPath);
                    groupedBounds.EnsurePath(nextPath);
                    groupedCurves.AddRange(holdingListCurves[i], groupedCurves.Path(nextPath));
                    groupedBounds.AddRange(holdingListBoundsCurves[i], groupedBounds.Path(nextPath));

                }
            }

            // Assign variables to output parameters
            DA.SetDataTree(0, groupedBounds);
            DA.SetDataTree(1, groupedCurves);

        }



        private List<Line> deduplicateLines(List<Line> inputLines, double tolerance)
        {
            List<Line> lines = inputLines.OrderBy(o => o.Length).ToList(); // Sort so we can break after a no match
            List<int> duplicateIndices = new List<int>();

            for (int i = lines.Count - 1; i >= 0; i--)
            {
                for (int j = i - 1; j >= 0; j--)
                {
                    double d1 = Math.Abs(lines[i].From.DistanceTo(lines[j].From));
                    double d2 = Math.Abs(lines[i].From.DistanceTo(lines[j].To));
                    double d3 = Math.Abs(lines[i].To.DistanceTo(lines[j].From));
                    double d4 = Math.Abs(lines[i].To.DistanceTo(lines[j].To));

                    if ((d1 < tolerance || d2 < tolerance) && (d3 < tolerance || d4 < tolerance))
                    {
                        duplicateIndices.Add(j);
                        duplicateIndices.Add(i);
                    }
                    else
                    {
                        break;
                    }

                }

            }

            // Remove duplicate indices and cull the list
            foreach (int indice in duplicateIndices.Distinct().ToList().OrderByDescending(x => x))
            {
                lines.RemoveAt(indice);
            }
            return lines;

        }



        private void createVoronoi(List<FlowStructure> flowStructures)
        {
            // Get the starts from everything
            List<Point3d> allStartPoints = flowStructures.Select(x => x.start).ToList();
            // Make a 2d list of all the start points
            Grasshopper.Kernel.Geometry.Node2List nodes = new Grasshopper.Kernel.Geometry.Node2List(allStartPoints);

            // Create a 2d list that forms the boundary
            List<Grasshopper.Kernel.Geometry.Node2> outline = new List<Grasshopper.Kernel.Geometry.Node2>();
            // Get sorted lists of coordinates to guestimate a boundary rectangle

            var sortedX = allStartPoints.OrderByDescending(item => item.X);
            var sortedY = allStartPoints.OrderByDescending(item => item.Y);
            outline.Add(new Grasshopper.Kernel.Geometry.Node2(sortedX.First().X, sortedY.First().Y));
            outline.Add(new Grasshopper.Kernel.Geometry.Node2(sortedX.First().X, sortedY.Last().Y));
            outline.Add(new Grasshopper.Kernel.Geometry.Node2(sortedX.Last().X, sortedY.Last().Y));
            outline.Add(new Grasshopper.Kernel.Geometry.Node2(sortedX.Last().X, sortedY.First().Y));

            // TODO: a delauney first so the brute force isn't needed, and the topology can be used to group

            var voronoiCells = Grasshopper.Kernel.Geometry.Voronoi.Solver.Solve_BruteForce(nodes, outline);
            // Cells are order as the nodes were; so we can loop through both
            for (int i = 0; i < voronoiCells.Count; i++)
            {
                flowStructures[i].catchment = voronoiCells[i].ToPolyline();
            }

        }

        class FlowStructure
        {
            public Curve curve;
            public Point3d start;
            public Point3d end;
            public int groupIndex;
            public double groupDistance;
            public Polyline catchment;

            // Constructor
            public FlowStructure(Curve flowCurve, int i, double minProximity)
            {
                curve = flowCurve;
                start = flowCurve.PointAtStart;
                end = flowCurve.PointAtEnd;
                groupIndex = i;
                groupDistance = minProximity;
            }

            // Instance Method
            //public Point3d start()
            //{
            //  return startPoint;
            //}


        }

        /// <summary>
        /// The Exposure property controls where in the panel a component icon 
        /// will appear. There are seven possible locations (primary to septenary), 
        /// each of which can be combined with the GH_Exposure.obscure flag, which 
        /// ensures the component will only be visible on panel dropdowns.
        /// </summary>
        public override GH_Exposure Exposure
        {
            get { return GH_Exposure.primary; }
        }

        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return badger.Properties.Resources.icon_flows;
            }
        }

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("{2d241bdc-ecaa-4cf3-815a-c8001d1798d1}"); }
        }
    }
}
