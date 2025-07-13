using System;
using System.Collections.Generic;
using UnityEngine;

using Den.Tools;
using Den.Tools.GUI;
using Den.Tools.Matrices;
using Den.Tools.Splines;

using MapMagic.Core;
using MapMagic.Products;
using MapMagic.Clusters;

namespace MapMagic.Nodes.Biomes //allgenerators should be in Nodes namespace
{
	[System.Serializable]
	[GeneratorMenu (menu="Biomes/Cluster", name ="Cluster", iconName="GeneratorIcons/Import", disengageable = true, disabled = false, 
		helpLink = "https://gitlab.com/denispahunov/mapmagic/-/wikis/MatrixGenerators/Import")]
	public class ClusterRead230 : BaseFunctionGenerator, IMultiOutlet, IBiome, ICustomClear //, IPrepare, IBiome, ICustomClear
	{
		public Cluster cluster = new Cluster();

		public override void Generate (TileData data, StopToken stop) 
		{
			if (stop!=null && stop.stop) return;
			if (subGraph == null || !enabled) 
			{
				foreach (IOutlet<object> outlet in outlets)
					data.RemoveProduct(outlet); 
				return; 
			}

			//updating cluster
			cluster.RefreshArea(subGraph, data.area, stop);

			//copy results to outlets
			if (stop!=null && stop.stop) return;
			
			for (int o=0; o<outlets.Length; o++)
			{
				IFnOutlet<object> outlet = outlets[o];
				IFnExit<object> fnExit = (IFnExit<object>)outlet.GetInternalPortal(subGraph);
				//IOutlet<object> outlet = idsOutlets[id];

				object tileProduct = cluster.GetCroppedMergedProduct(fnExit.Id, GetGenericType(fnExit), data.area, data.globals.height, stop);

				if (stop!=null && stop.stop) return;
				data.StoreProduct(outlet, tileProduct);

				#if MM_DEBUG
				if (tileProduct is MatrixWorld m)
					Log.AddThreaded("ClusterRead.Generate", ("coord:",data.area.Coord), ("value:",m.arr[100]), ("stop:",stop.stop));
				#endif
			}
		}


		public void OnClearing (Graph graph, TileData data, ref bool isReady, bool totalRebuild=false) 
		{
			// What should be cleared and when:
			// - On this graph modification (inlet change):			this node (done by default), all subgraph outputs (for biomes)
			// - Subgraph modification (any subgraph node change):	this node, all subgraph relevants
			// - Exposed values change (this node change):			this node (done by default), exp related subgraph node, subgraph relevants

			//clarifying whether this generator changed directly or recursively
			bool versionChanged = data.VersionChanged(this);
			bool thisChanged = !isReady && versionChanged;
			bool inletChanged = !isReady && !versionChanged;

			if (subGraph == null) return;
			TileData subData = data.CreateLoadSubData(id);

			//resetting exposed related nodes on this node change
			if (thisChanged)
			{
				//TODO: reset only changed generators
				//foreach (IUnit expUnit in subGraph.exposed.AllUnits(subGraph))
				//	subData.ClearReady((Generator)expUnit);
			}

			//resetting outputs/relevants on inlet or this changed
			if (inletChanged || thisChanged)
			{
				foreach (Generator relGen in subGraph.RelevantGenerators(data.isDraft)) 
					subData.ClearReady(relGen);
			}

			//iterating in sub-graph after
			subGraph.ClearChanged(subData);

			//at the end clearing this if any subgraph relevant changed
			if (isReady)
			{
				foreach (Generator relGen in subGraph.RelevantGenerators(data.isDraft)) 
					if (!subData.IsReady(relGen))
						isReady = false;
			}
		}
	}









	[System.Serializable]
	[GeneratorMenu (menu="Biomes/Cluster", name ="Cluster Outdated", iconName="GeneratorIcons/Import", disengageable = true, disabled = false, 
		helpLink = "https://gitlab.com/denispahunov/mapmagic/-/wikis/MatrixGenerators/Import")]
	public class ClusterRead220: Generator, IMultiOutlet, IPrepare, IBiome, ICustomClear
	{
		[Val("Cluster", type = typeof(ClusterAsset))]	public ClusterAsset cluster;
		[Val("Update")] public bool autoUpdate = true;
		[Val("Output")] public ulong exitId = 0;

		public Graph SubGraph => cluster?.graph; //to let editor know this graph is assigned to MM
		public TileData SubData (TileData parent) => cluster?.data;
		public Expose.Override Override { get{return null;} set{} } //empty override

		[System.NonSerialized] public ulong[] cachedIds; 
		[System.NonSerialized] public string[] cachedNames; //for dropdown selector


		[Val("Wrap Mode", priority = 4)]	public CoordRect.TileMode wrapMode = CoordRect.TileMode.Clamp;
		[Val("Add.Margin", "Advanced")]	public float additionalMargins = 0;

		[System.NonSerialized] public ulong clusterAssetVersion; //to refresh outlets on change

		public IFnOutlet<object>[] outlets = new IFnOutlet<object>[0];
		public IEnumerable<IOutlet<object>> Outlets() => outlets;

		public void Prepare (TileData data, Terrain terrain)
		{
			//if (clusterAsset == null  ||  subGraph == null) return;
			//subGraphId = subGraph.GetInstanceID();
			
			//TileData subData = data.CreateSubData(id);

			

			/*if (updateCluster  &&  (clusterAsset.graphId != data.graphId  ||  clusterAsset.graphChangeVersion != data.graphChangeVersion))
			{
				TileData subData = data.CreateSubData(id);
				subData.graphId = clusterAsset.graphId;
				subData.graphChangeVersion = clusterAsset.graphChangeVersion;
				subGraph.Prepare(subData, terrain);
			}*/
		}


		public override void Generate (TileData data, StopToken stop) 
		{
			if (stop!=null && stop.stop) return;
			if (cluster == null || cluster.Count==0  || !enabled) 
			{
				foreach (IOutlet<object> outlet in outlets)
					data.RemoveProduct(outlet); 
				return; 
			}

			//updating cluster
			if (autoUpdate && !cluster.CheckUpToDate()) 
				cluster.UpdateCluster(data.area.Coord, data.isDraft, stop);

			//populating outlets ids (maybe keep those cached?)
			Dictionary<ulong,IFnOutlet<object>> idsOutlets = new Dictionary<ulong,IFnOutlet<object>>();
			foreach (IFnOutlet<object> outlet in outlets)
			{
				ulong portalId = outlet.PortalId;
				if (!idsOutlets.ContainsKey(portalId)) //might be several outlets created because of an error
					idsOutlets.Add(portalId, outlet);
			}

			//copy results to outlets
			foreach ((ulong id, object clusterProduct, string name, Type type) in cluster.IdsProductsNamesTypes())
			{
				//IOutlet<object> outlet = idsOutlets[id];
				if (!idsOutlets.TryGetValue(id, out IFnOutlet<object> outlet))
					continue;
					//this outlet might not created yet in parent graph - ignoring since it's not connected anyways

				object tileProduct = null;
				if (clusterProduct != null)
					switch (clusterProduct)
					{
						case MatrixWorld clusterMatrix: tileProduct = GenerateMatrix(clusterMatrix, data, stop); break;
						case TransitionsList clusterObjects: tileProduct = GenerateObjects(clusterObjects, cluster.worldPos, cluster.worldSize, data, stop); break;
						case SplineSys clusterSplines: tileProduct = GenerateSplines(clusterSplines, cluster.worldPos, cluster.worldSize, data, stop); break;
						}

				if (stop!=null && stop.stop) return;
				data.StoreProduct(outlet, tileProduct);
			}
		}

		private MatrixWorld GenerateMatrix (MatrixWorld clusterMatrix, TileData data, StopToken stop) 
		{
			//MatrixWorld clusterMatrix = cluster.GetProduct<MatrixWorld>(exitId);
			MatrixWorld dstMatrix = new MatrixWorld(data.area.full.rect, data.area.full.worldPos, data.area.full.worldSize, data.globals.height);

			//copy from Import node
			if (clusterMatrix.PixelSize.x >= dstMatrix.PixelSize.x)
				MatrixGenerators.Import200.ImportWithEnlarge(clusterMatrix, dstMatrix, wrapMode, stop);
			else
				MatrixGenerators.Import200.ImportWithDownscale(clusterMatrix, dstMatrix, wrapMode, stop);

			return dstMatrix;
		}

		private TransitionsList GenerateObjects (TransitionsList clusterObjects, Vector2D clusterWorldPos, Vector2D clusterWorldSize, TileData data, StopToken stop) 
		{
			TransitionsList dstObjects = new TransitionsList();

			Vector2D min = data.area.full.worldPos - new Vector2D(additionalMargins, additionalMargins);
			Vector2D max = data.area.full.worldPos + data.area.full.worldSize + new Vector2D(additionalMargins*2, additionalMargins*2); 

			for (int t=0; t<clusterObjects.count; t++)
			{
				if (stop!=null && stop.stop) return null;

				Transition transition = clusterObjects.arr[t];

				if (transition.pos.x <= min.x  ||   transition.pos.x >= max.x  ||
					transition.pos.z <= min.z  ||   transition.pos.z >= max.z)
						continue;

				dstObjects.Add(transition);
			}

			return dstObjects;
		}

		private SplineSys GenerateSplines (SplineSys clusterSplines, Vector2D clusterWorldPos, Vector2D clusterWorldSize, TileData data, StopToken stop) 
		{
			SplineSys dstSplines = new SplineSys(clusterSplines);

			Vector2D min = data.area.full.worldPos - new Vector2D(additionalMargins, additionalMargins);
			Vector2D size = data.area.full.worldSize + new Vector2D(additionalMargins*2, additionalMargins*2); 

			dstSplines.CutByRect(min.Vector3(), size.Vector3());
			dstSplines.RemoveOuterSegments(min.Vector3(), size.Vector3());

			return dstSplines;
		}

		public void OnClearing (Graph graph, TileData data, ref bool isReady, bool totalRebuild = false) 
		{
			//resetting UpToDate state if user pressed Rebuild
			if (totalRebuild && autoUpdate && cluster!=null)
				cluster.lastChangeVersion = 0;

			//clarifying whether this generator changed directly or recursively
			bool versionChanged = data.VersionChanged(this); 
			bool thisChanged = !isReady && versionChanged;
			bool inletChanged = !isReady && !versionChanged;

			if (cluster == null) return;
			Graph subGraph = cluster.graph;
			if (subGraph == null) return;
			TileData subData = cluster.data;
			if (subData == null) {isReady = false; return;} 

			//resetting exposed related nodes on this node change
			if (thisChanged)
			{
				//TODO: reset only changed generators
				//foreach (IUnit expUnit in subGraph.exposed.AllUnits(subGraph))
				//	subData.ClearReady((Generator)expUnit);
			}

			//resetting outputs/relevants on inlet or this changed
			if (inletChanged || thisChanged)
			{
				foreach (Generator relGen in subGraph.RelevantGenerators(data.isDraft)) 
					subData.ClearReady(relGen);
			}

			//iterating in sub-graph after
			subGraph.ClearChanged(subData);

			//at the end clearing this if any subgraph relevant changed
			if (isReady)
			{
				foreach (Generator relGen in subGraph.RelevantGenerators(data.isDraft)) 
					if (!subData.IsReady(relGen))
						isReady = false;
			}
		}

		public void DrawGizmo ()
		{
			//hideDefaultToolGizmo = drawOffsetScaleGizmo;
			//if (drawOffsetScaleGizmo)
			//	GeneratorUI.DrawNodeOffsetSize(ref offset, ref scale, nodeToChange:this);
		}

	}


	[System.Serializable]
	[GeneratorMenu (menu="Map/Initial", name ="Cluster", iconName="GeneratorIcons/Import", disengageable = true, disabled = false, 
		helpLink = "https://gitlab.com/denispahunov/mapmagic/-/wikis/MatrixGenerators/Import")]
	public class ClusterMatrixRead220 : Generator, IMultiOutlet, IPrepare, IBiome, ICustomClear, IOutlet<MatrixWorld>
	{
		[Val("Cluster", type = typeof(ClusterAsset))]	public ClusterAsset cluster;
		[Val("Update")] public bool autoUpdate = true;
		[Val("Output")] public ulong exitId = 0;

		public Graph SubGraph => cluster?.graph; //to let editor know this graph is assigned to MM
		public TileData SubData (TileData parent) => cluster?.data;
		public Expose.Override Override { get{return null;} set{} } //empty override

		[System.NonSerialized] public ulong[] cachedIds; 
		[System.NonSerialized] public string[] cachedNames; //for dropdown selector


		[Val("Wrap Mode", priority = 4)]	public CoordRect.TileMode wrapMode = CoordRect.TileMode.Clamp;

		public IFnOutlet<object>[] outlets = new IFnOutlet<object>[0];
		public IEnumerable<IOutlet<object>> Outlets() => outlets;


		public void Prepare (TileData data, Terrain terrain)
		{
			//if (clusterAsset == null  ||  subGraph == null) return;
			//subGraphId = subGraph.GetInstanceID();
			
			//TileData subData = data.CreateSubData(id);

			

			/*if (updateCluster  &&  (clusterAsset.graphId != data.graphId  ||  clusterAsset.graphChangeVersion != data.graphChangeVersion))
			{
				TileData subData = data.CreateSubData(id);
				subData.graphId = clusterAsset.graphId;
				subData.graphChangeVersion = clusterAsset.graphChangeVersion;
				subGraph.Prepare(subData, terrain);
			}*/
		}


		public override void Generate (TileData data, StopToken stop) 
		{
			if (stop!=null && stop.stop) return;
			if (cluster == null || cluster.Count==0  || !enabled) { data.RemoveProduct(this); return; }

			if (autoUpdate && !cluster.CheckUpToDate()) 
				cluster.UpdateCluster(data.area.Coord, data.isDraft, stop);

			MatrixWorld clusterMatrix = cluster.GetProduct<MatrixWorld>(exitId);
			MatrixWorld dstMatrix = new MatrixWorld(data.area.full.rect, data.area.full.worldPos, data.area.full.worldSize, data.globals.height);

			//copy from Import node
			if (clusterMatrix.PixelSize.x >= dstMatrix.PixelSize.x)
				MatrixGenerators.Import200.ImportWithEnlarge(clusterMatrix, dstMatrix, wrapMode, stop);
			else
				MatrixGenerators.Import200.ImportWithDownscale(clusterMatrix, dstMatrix, wrapMode, stop);

			if (stop!=null && stop.stop) return;
			data.StoreProduct(this, dstMatrix);
		}

		public void OnClearing (Graph graph, TileData data, ref bool isReady, bool totalRebuild=false) => OnClearingBase(this, cluster, graph, data, ref isReady);

		public static void OnClearingBase (Generator gen, ClusterAsset cluster, Graph graph, TileData data, ref bool isReady) 
		/// Shared between all cluster types
		{
			//clarifying whether this generator changed directly or recursively
			bool versionChanged = data.VersionChanged(gen);
			bool thisChanged = !isReady && versionChanged;
			bool inletChanged = !isReady && !versionChanged;

			if (cluster == null) return;
			Graph subGraph = cluster.graph;
			if (subGraph == null) return;
			TileData subData = cluster.data;
			if (subData == null) return;

			//resetting exposed related nodes on this node change
			if (thisChanged)
			{
				//TODO: reset only changed generators
				//foreach (IUnit expUnit in subGraph.exposed.AllUnits(subGraph))
				//	subData.ClearReady((Generator)expUnit);
			}

			//resetting outputs/relevants on inlet or this changed
			if (inletChanged || thisChanged)
			{
				foreach (Generator relGen in subGraph.RelevantGenerators(data.isDraft)) 
					subData.ClearReady(relGen);
			}

			//iterating in sub-graph after
			subGraph.ClearChanged(subData);

			//at the end clearing this if any subgraph relevant changed
			if (isReady)
			{
				foreach (Generator relGen in subGraph.RelevantGenerators(data.isDraft)) 
					if (!subData.IsReady(relGen))
						isReady = false;
			}
		}

		public void DrawGizmo ()
		{
			//hideDefaultToolGizmo = drawOffsetScaleGizmo;
			//if (drawOffsetScaleGizmo)
			//	GeneratorUI.DrawNodeOffsetSize(ref offset, ref scale, nodeToChange:this);
		}

	}
}
