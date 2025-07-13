
using UnityEngine;
using UnityEditor;
using System;
using System.Collections;
using System.Collections.Generic;

using Den.Tools;
using Den.Tools.GUI;

using MapMagic.Nodes;
using MapMagic.Nodes.Biomes;
using MapMagic.Terrains;
using MapMagic.Locks;

using MapMagic.Nodes.GUI; //to open up editor window
using MapMagic.Terrains.GUI; //pin draw
using Den.Tools.Matrices;

namespace MapMagic.Clusters
{
	[CustomEditor(typeof(ClusterAsset))]
	public class ClusterAssetInspector : Editor
	{
		public ClusterAsset cluster; //aka target

		UI ui = new UI();

		int selectedNameNum = 0;

		#if UNITY_EDITOR
		[UnityEditor.InitializeOnLoadMethod]
		#endif
		static void Subscribe ()
		{
			MapMagic.Core.GUI.MapMagicInspector.OnClusterExported += CreateViaDialog;
		}


		public void OnEnable ()
		{
			EditorHacks.SetIconForObject(target, TexturesCache.LoadTextureAtPath("MapMagic/Icons/Window"));
		}


		public override void  OnInspectorGUI ()
		{
			DrawDefaultInspector();

			cluster = (ClusterAsset)target;

			ui.Draw(DrawGUI, inInspector:true);
		}


		public void DrawGUI ()
		{
			Cell.EmptyLinePx(4);

			string[] clusterNames = cluster.StoredNames();
			using (Cell.LineStd) Draw.PopupSelector(ref selectedNameNum, clusterNames);

			using (Cell.LineStd) 
				if (Draw.Button("Preview Matrix"))
				{
					MatrixWorld matrix = cluster.GetProductByNum<MatrixWorld>(selectedNameNum);

					if (matrix != null)
						matrix.ToWindow(name);
				}

			#if MM_DEBUG
			if (cluster.graph != null)
			{
				using (Cell.LineStd) Draw.DualLabel("Graph version", Id.ToString(cluster.graph.IdsVersions()));
				using (Cell.LineStd) Draw.DualLabel("Cluster version", Id.ToString(cluster.lastChangeVersion));
				using (Cell.LineStd) Draw.Label(cluster.CheckUpToDate() ? "Up To Date" : "Not ready");
				using (Cell.LineStd) 
					if (Draw.Button("Produce"))
						cluster.Produce();
			}
			#endif
		}


		public static void CreateViaDialog (MapMagic.Core.MapMagicObject mapMagic, Vector3 worldPos)
		/// Creates an asset on pressing "Export Cluster" button in MM inspector
		{
			string savePath = UnityEditor.EditorUtility.SaveFilePanel(
				$"Export Cluster",
				"Assets",
				"MapMagic Cluster", 
				"asset");

			if (savePath == null  ||  savePath.Length==0)
				return; //clicked cancel

			savePath = savePath.Replace(Application.dataPath, "Assets");

			ClusterAsset cluster = CreateInstance<ClusterAsset>();
			cluster.graph = mapMagic.graph;
			cluster.globals = mapMagic.globals.Clone();
			cluster.worldPos = worldPos.Vector2D();
			cluster.worldSize = mapMagic.tileSize;
			cluster.resolution = (int)mapMagic.tileResolution;

			AssetDatabase.DeleteAsset(savePath);  //this will mark cluster as 'null' in all MMs using it
			AssetDatabase.CreateAsset(cluster, savePath);
			AssetDatabase.SaveAssets();
		}
	}
}