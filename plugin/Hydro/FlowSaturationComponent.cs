﻿// With thanks for Anders Holden Deleuran for providing his Python implementation and visualisation ideas

namespace Groundhog
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Linq;
    using System.Runtime.InteropServices;
    using Grasshopper;
    using Grasshopper.Kernel;
    using Grasshopper.Kernel.Geometry;
    using Grasshopper.Kernel.Geometry.Voronoi;
    using Groundhog.Properties;
    using Rhino.Display;
    using Rhino.Geometry;

    public class FlowSaturationComponent : GroundHogComponent
    {
        public FlowSaturationComponent()
            : base("Flow Saturation", "Saturation", "Identify the saturation levels across terrain given flow paths", "Groundhog",
                "Hydro")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        public override Guid ComponentGuid => new Guid("{48cae0b9-2c40-4030-92de-4e622db09f88}");
        protected override Bitmap Icon => Resources.icon_flows_saturation;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter(
                "Mesh", "M", "Base landscape form (as a mesh) for the flow calculation", GH_ParamAccess.item);
            pManager[0].Optional = false;
            pManager.AddCurveParameter(
                "Flow Paths", "C", "The flow paths as generated by the flows component", GH_ParamAccess.list);
            pManager[1].Optional = false;
            pManager.AddNumberParameter(
                "Start Volume", "T", "The quantity of water that each path 'starts with'", GH_ParamAccess.item);
            pManager[2].Optional = true;
            pManager.AddNumberParameter(
                "Segment Loss", "T", "The decimal percent (e.g. 0.15 for 15%) of the initial volume that is lost in each segment", GH_ParamAccess.item);
            pManager[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddIntegerParameter("Flow Overlaps", "O", "TODO", GH_ParamAccess.list);
            pManager.AddNumberParameter("Flow Volumes", "V", "TODO", GH_ParamAccess.list);
            pManager.AddMeshParameter("Mesh Faces", "F", "The sub mesh faces (for coloring)", GH_ParamAccess.list);
            pManager.AddPointParameter("Face Centers", "C", "The centers of each mesh face (for vector previews)", GH_ParamAccess.list);
        }

        protected override void GroundHogSolveInstance(IGH_DataAccess DA)
        {
            var FLOW_MESH = default(Mesh);
            DA.GetData(0, ref FLOW_MESH);
            if (FLOW_MESH == null)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "A null item has been provided as the Mesh input; please correct this input.");
                return;
            }

            var FLOW_PATHS = new List<Curve>();
            DA.GetDataList(1, FLOW_PATHS);
            FLOW_PATHS.RemoveAll(curve => curve == null); // Remove null items; can be due to passing in the points not the path
            if (FLOW_PATHS.Count == 0)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No Flow Paths provided or they were provided as an inappropriate geometry.");
                return;
            }

            double START_VOLUME = 100;
            DA.GetData(2, ref START_VOLUME);
            if (START_VOLUME < 0)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Start volume must be a positive number.");
                return;
            }

            double SEGMENT_LOSS = 0.00;
            DA.GetData(3, ref SEGMENT_LOSS);

            /* End initial variable setup */

            Point3d[] meshFacePoints = new Point3d[FLOW_MESH.Faces.Count]; // The center points of each mesh face
            int[] meshAreaOverlaps = new int[FLOW_MESH.Faces.Count]; // Count of the overlaps of flow points to this mesh face
            double[] meshAreaVolumes = new double[FLOW_MESH.Faces.Count]; // Count of the overlaps of flow points to this mesh face
            for (var i = 0; i < FLOW_MESH.Faces.Count; i++)
            {
                meshFacePoints[i] = FLOW_MESH.Faces.GetFaceCenter(i);
            }

            // Loop through all the points that makeup a flow path
            double drainAtPoint = START_VOLUME * SEGMENT_LOSS;
            double removeFromVolume = drainAtPoint;
            if (SEGMENT_LOSS == 0.0)
            {
                drainAtPoint = START_VOLUME; // If no segment loss (i.e. 0%) then still need to show something
            }

            for (var i = 0; i < FLOW_PATHS.Count; i++)
            {
                Polyline flowPath;
                FLOW_PATHS[i].TryGetPolyline(out flowPath);
                var flowVolume = START_VOLUME; // Per path volume tracking

                for (var j = 0; j < flowPath.Count; j++)
                {
                    flowVolume -= removeFromVolume; // Remove deposited water from remaining
                    if (flowVolume <= 0.0)
                    {
                        break; // All volumes drained; no need for further calculations
                    }

                    var closestMeshPoint = FLOW_MESH.ClosestMeshPoint(flowPath[j], 0.0); // TODO: Set maximum distance for performance?
                    meshAreaOverlaps[closestMeshPoint.FaceIndex] += 1; // Add the number of intersections
                    if (j == flowPath.Count - 1)
                    {
                        // If at the end point then drain remaining volume
                        meshAreaVolumes[closestMeshPoint.FaceIndex] += flowVolume;
                    }
                    else
                    {
                        // ...otherwise flow paths loose x% of original volume at each segment they cross
                        meshAreaVolumes[closestMeshPoint.FaceIndex] += drainAtPoint;
                    }
                }
            }

            // Assign variables to output parameters
            DA.SetDataList(0, meshAreaOverlaps);
            DA.SetDataList(1, meshAreaVolumes);
            DA.SetDataList(2, TerrainCalculations.Explode(FLOW_MESH));
            DA.SetDataList(3, meshFacePoints);
        }
    }
}