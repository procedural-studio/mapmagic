using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;

using Den.Tools;
using Den.Tools.GUI;

using MapMagic.Core.GUI;


namespace MapMagic.Nodes.GUI
{
	[CustomEditor(typeof(Graph))]
	//[InitializeOnLoad]  
	public class GraphInspector : Editor
	{
		Graph graph; //aka target
		UI ui = new UI();

		/*public void OnEnable () 
		{
			SceneView.onSceneGUIDelegate -= DragGraphToScene;
			SceneView.onSceneGUIDelegate += DragGraphToScene;
		}*/

		private static bool guiSetColorsPro;
		private static bool guiSetColorsLinks;

		public static HashSet<string> allGraphsGuids;

		[RuntimeInitializeOnLoadMethod, UnityEditor.InitializeOnLoadMethod] 
		static void Subscribe()
		{
			#if UNITY_2019_1_OR_NEWER
			SceneView.duringSceneGui -= DragGraphToScene;
			SceneView.duringSceneGui += DragGraphToScene;
			#else
			SceneView.onSceneGUIDelegate -= DragGraphToScene;
			SceneView.onSceneGUIDelegate += DragGraphToScene;
			#endif
			
			allGraphsGuids = new HashSet<string>(AssetDatabase.FindAssets("t:Graph"));   //compiling all graph guids to display graph icons
			EditorHacks.SubscribeToListIconDrawCallback(DrawListIcon);	
			EditorHacks.SubscribeToTreeIconDrawCallback(DrawTreeIcon);	
			
			//SceneView.duringSceneGui -= DrawInSceneView;
			//#if MM_DEBUG
			//SceneView.duringSceneGui += DrawInSceneView;
			//#endif
		}

		static void DragGraphToScene (SceneView sceneView)
		{
			UnityEngine.Object[] draggedObjs = DragAndDrop.objectReferences;
			if (draggedObjs == null || draggedObjs.Length != 1 || !(draggedObjs[0] is Graph)) return;

			Graph graph = (Graph)draggedObjs[0];

			if (graph != null)
			//if (Event.current.mode == EventType.DragUpdated || Event.current.mode == EventType.DragPerform)
			//in Unity 2021.2 won't produce events if cursor is Rejected
			{
				DragAndDrop.visualMode = DragAndDropVisualMode.Copy; // show a drag-add icon on the mouse cursor
					// won't perform a drag if cursor is Rejected
			}

			if (Event.current.type == EventType.DragPerform)
			{
				DragAndDrop.AcceptDrag();
				MapMagicInspector.CreateMapMagic(graph);
			}
		}

		/*private static Graph sceneViewGraph = null;
		private static GraphInspector sceneViewGraphInspector = null;
		private static void DrawInSceneView (SceneView sceneView)
		{	
			if (sceneViewGraphInspector != null  &&  sceneViewGraph != null  &&  sceneViewGraph.drawInSceneView)
				sceneViewGraphInspector.OnInspectorGUI();
		}*/
		

		public override void  OnInspectorGUI ()
		{
			graph = (Graph)target;

			//assign to draw in scene view
			//#if MM_DEBUG
			//if (graph.drawInSceneView)
			//	{ sceneViewGraph = graph; sceneViewGraphInspector = this; }
			//#endif

			//undo
			if (ui.undo == null) ui.undo = new Den.Tools.GUI.Undo();
			ui.undo.undoObject = graph;
			ui.undo.undoName = "MapMagic Graph Settings";

			//drawing
			ui.Draw(DrawGUI, inInspector:true);	
		}

		public void DrawGUI ()
		{
				using (Cell.LinePx(32))
					Draw.Label("WARNING: Keeping this asset selected in \nInspector can slow down editor GUI performance.", style:UI.current.styles.helpBox);
				Cell.EmptyLinePx(5);

				using (Cell.LinePx(24)) if ( Draw.Button("Open Editor"))
					GraphWindow.Show(graph);
				using (Cell.LinePx(20)) if ( Draw.Button("Open in New Tab"))
					GraphWindow.ShowInNewTab(graph);

				//seed
				Cell.EmptyLinePx(5);
				using (Cell.LineStd)
				{
					int newSeed = Draw.Field(graph.random.Seed, "Seed"); //
					if (newSeed != graph.random.Seed)
					{
						graph.random.Seed = newSeed;
						//Graph.OnChange.Raise(graph);
					} 
				}

				using (Cell.LineStd) Draw.DualLabel("Nodes", graph.generators.Length.ToString());
				using (Cell.LineStd) Draw.DualLabel("MapMagic ver", graph.serializedVersion.ToString());
				Cell.EmptyLinePx(5);


				//global values
				/*using (Cell.LineStd)
					using (new Draw.FoldoutGroup (ref showShared, "Global Values"))
						if (showShared)
					{
						List<string> changedNames = new List<string>();
						List<object> changedVals = new List<object>();

						(Type mode, string name)[] typeNames = graph.sharedVals.GetTypeNames();
						for (int i=0; i<typeNames.Length; i++)
							using (Cell.LineStd) GeneratorDraw.DrawGlobalVar(typeNames[i].mode, typeNames[i].name);

						if (Cell.current.valChanged)
						{
							GraphWindow.current.mapMagic.ClearAllNodes();
							GraphWindow.current.mapMagic.StartGenerate();
						}	
					}*/

				//dependent graphs
				using (Cell.LineStd)
					using (new Draw.FoldoutGroup (ref graph.guiShowDependent, "Dependent Graphs"))
						if (graph.guiShowDependent)
						{
							using (Cell.LinePx(0))
								DrawDependentGraphs(graph);
						}

				//debug
				#if MM_DEBUG
				using (Cell.LineStd)
					using (new Draw.FoldoutGroup (ref graph.guiShowDebug, "Debug"))
						if (graph.guiShowDebug)
						{
							using (Cell.LineStd) Draw.Toggle(ref graph.debugGenerate, "Generate");
							using (Cell.LineStd) Draw.Toggle(ref graph.debugGenInfo, "Info");
							using (Cell.LineStd) Draw.Toggle(ref graph.debugGraphBackground, "Background");
							using (Cell.LineStd) Draw.Field(ref graph.debugGraphBackColor, "Back Color");
							using (Cell.LineStd) Draw.Toggle(ref graph.debugGraphFps, "Graph FPS");
							using (Cell.LineStd) Draw.Toggle(ref graph.drawInSceneView, "Draw In Scene View");
							using (Cell.LineStd) Draw.Toggle(ref graph.saveChanges, "Save Changes");
							using (Cell.LineStd) Draw.Toggle(ref graph.debugDrawMousePosition, "Mouse Position");
							using (Cell.LineStd) Draw.Toggle(ref graph.debugDrawPortalLinks, "Portal Links");

							using (Cell.LineStd) 
								if (Draw.Button("Test Missing Gen Plug"))
									MissingGenPlug.TestAdd(graph);

							using (Cell.LineStd) 
								if (Draw.Button("Restore Missing Gen Plug"))
									for (int g=0; g<graph.generators.Length; g++)
										if (graph.generators[g] is MissingGenPlug miss)
											MissingGenPlug.Restore(miss, graph);
						}

				using (Cell.LineStd)
					using (new Draw.FoldoutGroup (ref graph.guiShowColors, "Colors"))
						if (graph.guiShowColors)
						{
							using (Cell.LineStd) Draw.Toggle(ref guiSetColorsPro, "Pro");
							using (Cell.LineStd) Draw.Toggle(ref guiSetColorsLinks, "Links");

							Dictionary<Type,Color> dict = GeneratorDraw.generatorColors;
							if (guiSetColorsPro) dict = GeneratorDraw.generatorColorsPro;
							if (guiSetColorsLinks) dict = GeneratorDraw.linkColors;
							if (guiSetColorsLinks && guiSetColorsPro) dict = GeneratorDraw.linkColorsPro;

							Type[] types = dict.Keys.ToArray();
							foreach (Type type in types)
								using (Cell.LineStd) 
									dict[type] = Draw.Field(dict[type], type.Name);

							Cell.EmptyLinePx(10);
							using (Cell.LineStd) Draw.Field(ref GeneratorDraw.tempShadowSize, "Shadow Size");
							using (Cell.LineStd) Draw.Field(ref GeneratorDraw.tempShadowOffset, "Shadow Offset");
							using (Cell.LineStd) Draw.Field(ref GeneratorDraw.tempShadowFill, "Shadow Fill");
							using (Cell.LineStd) Draw.Field(ref GeneratorDraw.tempShadowGamma, "Shadow Gamma");
							using (Cell.LineStd) Draw.Field(ref GeneratorDraw.tempShadowOpacity, "Shadow Opacity");


							if (Cell.current.valChanged)
								GraphWindow.current.Repaint();
						}
				#endif
		}

		private void DrawDependentGraphs (Graph graph)
		/// Draws subgraphs recursively
		{
			foreach (Graph subGraph in graph.SubGraphs())
			{
				using (Cell.LineStd)
				{
					using (Cell.Row) Draw.Label(subGraph.name);
					using (Cell.RowPx(100)) Draw.ObjectField(subGraph);
				}

				using (Cell.LinePx(0))
				{
					Cell.EmptyRowPx(10);
					DrawDependentGraphs(subGraph);
				}
			}
		}



		public static void DrawListIcon (Rect iconRect, string guid, bool isListMode)
		{
			if (!allGraphsGuids.Contains(guid)) return;
			Texture2D icon = TexturesCache.LoadTextureAtPath("MapMagic/Icons/AssetBig");
			UnityEngine.GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
		}


		public static void DrawTreeIcon (Rect iconRect, string guid)
		{
			if (!allGraphsGuids.Contains(guid)) return;

			if (!BuildPipeline.isBuildingPlayer) //otherwise will log an error during build that cannot find AssetSmall icon in built resources
			{
				Texture2D icon = TexturesCache.LoadTextureAtPath("MapMagic/Icons/AssetSmall");
				UnityEngine.GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
			}
		}
	}
}