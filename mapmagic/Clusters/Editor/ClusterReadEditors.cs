using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
//using UnityEngine.Profiling;

using Den.Tools;
using Den.Tools.Matrices;
using Den.Tools.GUI;
using MapMagic.Core;
using MapMagic.Products;
using MapMagic.Nodes.GUI;
using MapMagic.Nodes.Biomes;
using MapMagic.Clusters;

namespace MapMagic.Nodes.GUI
{
	public static class ClusterReadEditors
	{
		[Draw.Editor(typeof(ClusterRead230), cat="Header")]
		public static void DrawClusterHeader (ClusterRead230 clusterNode)
		{
			if (clusterNode.subGraph == null)
				return;

			FunctionsEditors.RefreshOnSubgraphChange(clusterNode); //since it's first encounter fn gui appears checking for internal graph change and refreshing if needed
			FunctionsEditors.DrawInletsOutlets(clusterNode);
		}

		[Draw.Editor(typeof(ClusterRead230))]
		public static void DrawClusterNode (ClusterRead230 clusterNode)
		{
			//if (clusterNode.subGraph == null)
			//	return;

			Cluster cluster = clusterNode.cluster;

			using (Cell.Padded(1,1,0,0)) 
			{
				Cell.EmptyLinePx(2);
				using (Cell.LineStd) 
				{
					Graph prevGraph = clusterNode.subGraph;
					GeneratorDraw.SubGraph(clusterNode, ref clusterNode.subGraph, refreshOnGraphChange:false);
					
					//if (prevGraph != clusterNode.subGraph  &&  clusterNode.subGraph != null)
					//{
					//	clusterNode.ovd = new Expose.Override(clusterNode.subGraph.defaults);
					//	GraphWindow.current?.RefreshMapMagic();	
					//}
					//TODO: make sure all values overriden, and remove this
				}

				Cell.EmptyLinePx(4);
				using (Cell.LineStd) Draw.Field(ref cluster.tileSize, "Tile Size");
				using (Cell.LineStd) Draw.Field(ref cluster.offset, "Offset");
				using (Cell.LineStd) Draw.Field(ref cluster.resolution, "Resolution");
				using (Cell.LineStd) Draw.Field(ref cluster.margins, "Margins");

				#if MM_DEBUG
				using (Cell.LineStd) 
				{
					Cell.current.disabled = true;
					//Draw.Toggle(clusterNode.cluster.CheckUpToDate(), "UpToDate");
				}
				#endif

				Cell.EmptyLinePx(4);
				using (Cell.LineStd) 
					if (Draw.Button("Clear Products"))
						cluster.ClearProducts();

				if (Cell.current.valChanged)
					cluster.ResetUpToDate();
			}
		}




		[Draw.Editor(typeof(ClusterRead220), cat="Header")]
		public static void DrawClusterHeader (ClusterRead220 clusterNode)
		{
			if (clusterNode.cluster == null  ||  clusterNode.cluster.graph == null)
				return;

			//refreshing on cluster result change
//			if (clusterNode.cluster.lastChangeVersion != clusterNode.clusterAssetVersion) 
			{
				clusterNode.cluster.RepopulateOutputs();
				SyncProductsOutlets(ref clusterNode.outlets, clusterNode, clusterNode.cluster, GraphWindow.current.graph);
				clusterNode.clusterAssetVersion = clusterNode.cluster.lastChangeVersion;
			}

			//drawing outlets
			using (Cell.Row)
			{
				for (int i=0; i<clusterNode.outlets.Length; i++)
				{
					using (Cell.LineStd)
					{
						using (Cell.Row) Draw.Label(clusterNode.outlets[i].Name);
						Cell.EmptyRowPx(8);
						using (Cell.RowPx(0)) GeneratorDraw.DrawOutlet(clusterNode.outlets[i]);
					}
				}
			}
		}

		[Draw.Editor(typeof(ClusterRead220))]
		public static void DrawClusterNode (ClusterRead220 clusterNode)
		{
			if (clusterNode.cluster == null  ||  clusterNode.cluster.graph == null)
				return;

			//refreshing on cluster result change
			if (clusterNode.cluster.lastChangeVersion != clusterNode.clusterAssetVersion) 
			{
				clusterNode.cluster.RepopulateOutputs();
				SyncProductsOutlets(ref clusterNode.outlets, clusterNode, clusterNode.cluster, GraphWindow.current.graph);
				clusterNode.clusterAssetVersion = clusterNode.cluster.lastChangeVersion;
			}

			using (Cell.Padded(1,1,0,0)) 
			{
				/*using (Cell.LineStd) 
				{
					int selectedNum = clusterNode.cachedIds.Find(clusterNode.exitId);
					Draw.PopupSelector(ref selectedNum, clusterNode.cachedNames, "Output");
					if (selectedNum>=0 && selectedNum<clusterNode.cachedIds.Length)
						clusterNode.exitId = clusterNode.cachedIds[selectedNum];
				}*/

				#if MM_DEBUG
				using (Cell.LineStd) 
				{
					Cell.current.disabled = true;
					Draw.Toggle(clusterNode.cluster.CheckUpToDate(), "UpToDate");
				}
				#endif
			}
		}

		public static bool SyncProductsOutlets (ref IFnOutlet<object>[] outlets, Generator gen, ClusterAsset cluster, Graph graph) 
		/// Using instead function's SyncLayersPortals - it operates not with inner graph, but with cluster results
		{
			List<IFnOutlet<object>> newOutlets = null;
			lock (outlets) //SyncInlets may change inputs
			{
				//skipping if there is no change
				bool noChange = true;

				if (cluster.Count != outlets.Length)
					noChange = false;

				foreach (IFnOutlet<object> outlet in outlets)
				{
					if (!cluster.ContainsId(outlet.PortalId))
						{ noChange = false; break; }

					outlet.Name = cluster.GetName(outlet.PortalId); //renaming even if no change
				}

				if (noChange)
					return false;


				//disconnecting outlets that have no product in cluster (and creating list of outlets who have products in cluster)
				newOutlets = new List<IFnOutlet<object>>();

				foreach (IFnOutlet<object> outlet in outlets)
				{
					cluster.GetProductNameType(outlet.PortalId, out object product, out string name, out Type type);

					if (!cluster.ContainsId(outlet.PortalId))
					{
						graph.UnlinkOutlet(outlet);
					}

					else //contains id
					{
						newOutlets.Add(outlet);
						outlet.Name = name;
					}
				}


				//creating outlets if not exist
				Dictionary<ulong,IFnOutlet<object>> existingOutlets = new Dictionary<ulong,IFnOutlet<object>>();
				foreach (IFnOutlet<object> outlet in outlets)
				{
					if (!existingOutlets.ContainsKey(outlet.PortalId)) //might be several outlets created because of an error
						existingOutlets.Add(outlet.PortalId, outlet);
				}

				foreach ((ulong id, object product, string name, Type type) in cluster.IdsProductsNamesTypes())
				{
					if (!existingOutlets.ContainsKey(id)) //create new if not exists
					{
						IFnOutlet<object> outlet = CreateOutlet(gen, type);
						outlet.Name = name;
						outlet.PortalId = id;
						newOutlets.Add(outlet);
					}

					
				}
			}

			outlets = newOutlets.ToArray();
			return true;
		}

		private static IFnOutlet<object> CreateOutlet (Generator gen, Type genericType)
		{
			Type layerBaseType = typeof(FnOutlet<>);
			Type layerType = layerBaseType.GetGenericTypeDefinition().MakeGenericType(genericType);
			
			object layerObj = Activator.CreateInstance(layerType);
			IFnOutlet<object> layer = (IFnOutlet<object>)layerObj;

			layer.Id = Id.Generate();
			layer.SetGen(gen);

			return layer;
		}


		// Re-generating drafts when cluster has finished generating (from main tile)
		// or fc it complicates things! Better generate it for drafts with inner stop

/*		#if UNITY_EDITOR
		[UnityEditor.InitializeOnLoadMethod]
		#endif
		[RuntimeInitializeOnLoadMethod] 
		static void Subscribe ()
		{
//			ClusterAsset.OnClusterGenerated += EnqueueUpdateDraftsOnClusterGenerated;
		}

		private static void EnqueueUpdateDraftsOnClusterGenerated (ClusterAsset cluster)
		{
			Debug.Log("Cluster Generated");
			//Den.Tools.Tasks.CoroutineManager.Enqueue(()=>UpdateDraftsOnClusterGenerated(cluster));
		}

		private static void UpdateDraftsOnClusterGenerated (ClusterAsset cluster)
		{
			IMapMagic mapMagic = GraphWindow.current.mapMagic;
			if (mapMagic == null  ||  mapMagic.Graph == null)
				return;

			foreach (ClusterMatrixRead220 clRead in mapMagic.Graph.GeneratorsOfType<ClusterMatrixRead220>())
				if (clRead.cluster == cluster)
					mapMagic.Clear(clRead);

			mapMagic.Refresh(main:true, draft:true);
		}*/



		[Draw.Editor(typeof(ClusterMatrixRead220), cat="Header")]
		public static void DrawMatrixClusterHeader (ClusterMatrixRead220 clusterNode)
		{
			if (clusterNode.cluster == null  ||  clusterNode.cluster.graph == null)
				return;

			//refreshing
			ClusterAsset clusterAsset = clusterNode.cluster;
//			if (!clusterAsset.UpToDate)
//				clusterAsset.UpdateNamesTypesFromGraph();

			//drawing outlets
			using (Cell.Row)
			{
				for (int i=0; i<clusterNode.outlets.Length; i++)
				{
					using (Cell.LineStd)
					{
						using (Cell.Row) Draw.Label(clusterNode.outlets[i].Name);
						Cell.EmptyRowPx(8);
						using (Cell.RowPx(0)) GeneratorDraw.DrawOutlet(clusterNode.outlets[i]);
					}
				}
			}

		}

		[Draw.Editor(typeof(ClusterMatrixRead220))]
		public static void DrawMatrixCluster (ClusterMatrixRead220 gen)
		{
			if (gen.cluster == null  ||  gen.cluster.graph == null)
				return;

			//refreshing
			ClusterAsset cluster = gen.cluster;
			if (!cluster.CheckUpToDate()) 
				cluster.RepopulateOutputs();

			cluster.StoredCachedIdsNames(ref gen.cachedIds, ref gen.cachedNames, type:typeof(MatrixWorld));

			using (Cell.Padded(1,1,0,0)) 
			{
				using (Cell.LineStd) 
				{
					int selectedNum = gen.cachedIds.Find(gen.exitId);
					Draw.PopupSelector(ref selectedNum, gen.cachedNames, "Output");
					if (selectedNum>=0 && selectedNum<gen.cachedIds.Length)
						gen.exitId = gen.cachedIds[selectedNum];
				}

				#if MM_DEBUG
				using (Cell.LineStd) 
				{
					Cell.current.disabled = true;
					Draw.Toggle(cluster.CheckUpToDate(), "UpToDate");
				}
				#endif
			}
		}


		// Re-generating drafts when cluster has finished generating (from main tile)
		// or fc it complicates things! Better generate it for drafts with inner stop

/*		#if UNITY_EDITOR
		[UnityEditor.InitializeOnLoadMethod]
		#endif
		[RuntimeInitializeOnLoadMethod] 
		static void Subscribe ()
		{
//			ClusterAsset.OnClusterGenerated += EnqueueUpdateDraftsOnClusterGenerated;
		}

		private static void EnqueueUpdateDraftsOnClusterGenerated (ClusterAsset cluster)
		{
			Debug.Log("Cluster Generated");
			//Den.Tools.Tasks.CoroutineManager.Enqueue(()=>UpdateDraftsOnClusterGenerated(cluster));
		}

		private static void UpdateDraftsOnClusterGenerated (ClusterAsset cluster)
		{
			IMapMagic mapMagic = GraphWindow.current.mapMagic;
			if (mapMagic == null  ||  mapMagic.Graph == null)
				return;

			foreach (ClusterMatrixRead220 clRead in mapMagic.Graph.GeneratorsOfType<ClusterMatrixRead220>())
				if (clRead.cluster == cluster)
					mapMagic.Clear(clRead);

			mapMagic.Refresh(main:true, draft:true);
		}*/
	}
}