using UnityEngine;
using System;
using System.Collections.Generic;

using Den.Tools;
using Den.Tools.Matrices;
using Den.Tools.Splines;
using Den.Tools.Tasks;
using MapMagic.Core;
using MapMagic.Products;
using MapMagic.Nodes;
using MapMagic.Nodes.Biomes;
using MapMagic.Terrains;

namespace MapMagic.Clusters
{
	[SelectionBase]
	[ExecuteInEditMode] //to call onEnable, then it will subscribe to editor update
	[HelpURL("https://gitlab.com/denispahunov/mapmagic/wikis/home")]
	[DisallowMultipleComponent]
	public class ClusterPreview : MonoBehaviour
	{
		public ClusterAsset cluster;

		public Terrain terrain;

		public ulong heightId; //id of the node that outputs height
		public ulong[] textureIds = new ulong[0];
		public TerrainLayer[] textureLayers = new TerrainLayer[0];

		public void OnEnable ()
		{
			/*#if UNITY_EDITOR
			MapMagicObject.isPlaying = UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode;
			UnityEditor.EditorApplication.playModeStateChanged -= MapMagicObject.SetIsPlaying;
			UnityEditor.EditorApplication.playModeStateChanged += MapMagicObject.SetIsPlaying;

			UnityEditor.EditorApplication.update -= MapMagicObject.EditorUpdate; //just in case OnDisabled was not called somehow
			UnityEditor.EditorApplication.update += MapMagicObject.EditorUpdate;	


			#endif

			if (terrainSettings.material == null)
				terrainSettings.material = DefaultTerrainMaterial();

			//generating all tiles that were not generated previously
			StopGenerate();
			StartGenerateNonReady(); //executing in update, otherwise will not find obj pool*/
		}

		
	}
}
