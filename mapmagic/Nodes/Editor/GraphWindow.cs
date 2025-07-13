using System;
using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.Profiling;

using Den.Tools;
using Den.Tools.GUI;

using MapMagic.Core;
using MapMagic.Nodes;
using MapMagic.Products;
using MapMagic.Previews;


namespace MapMagic.Nodes.GUI
{
	//[EditoWindowTitle(title = "MapMagic Graph")]  //it's internal Unity stuff
	public class GraphWindow : EditorWindow
	{
		public static GraphWindow current;  //assigned each gui draw (and nulled after)

		public Graph graph;
		public List<Graph> parentGraphs;   //the ones on pressing "up level" button
											//we can have the same function in two biomes. Where should we exit on pressing "up level"?
											//automatically created on opening window, though

		private bool drawAddRemoveButton = true;  //turning off Add/Remove on opening popup with it, and re-enabling once the graph window is focused again

		public static Dictionary<Graph,Vector3> graphsScrollZooms = new Dictionary<Graph, Vector3>();
		//to remember the stored graphs scroll/zoom to switch between graphs
		//public for snapshots

		private static UnityEngine.SceneManagement.Scene prevSceneLoaded; //to search for mapmagic only when scene, selection or root objects count changes
		private static int prevRootObjsCount;
		private static UnityEngine.Object prevObjSelected;


		public static void RecordCompleteUndo () => current.graphUI.undo.Record(completeUndo:true);
		//the usual undo is recorded on valChange via gui

		public const int toolbarSize = 20;

		public UI graphUI = UI.ScrolledZoomedUI(maxZoomStage:0, minZoomStage:-16);  //public for snapshot
		UI toolbarUI = new UI();

		bool wasGenerating = false; //to update final frame when generate is finished
		
		private static Vector2 addDragTo = new Vector2(Screen.width-50,20);
		private static Vector2 AddDragDefault {get{ return new Vector2(Screen.width-50,20); }}
		private const int addDragSize = 34;
		private const int addDragOffset = 20; //the offset from screen corner
		private static readonly object addDragId = new object();

		private Vector2 addButtonDragOffset;

		public HashSet<Generator> selected = new HashSet<Generator>();
		public Dictionary<Generator,Cell> genCellsCache = new Dictionary<Generator,Cell>(); //to quickly restore and reuse cells. Might change each frame
		public Dictionary<Auxiliary,Cell> groupCellsCache = new Dictionary<Auxiliary,Cell>(); //same for groups
		public Dictionary<IInlet<object>, Vector2> inletsPosCache = new Dictionary<IInlet<object>,Vector2>(); //used in non-layout only!
		public Dictionary<IOutlet<object>, Vector2> outletsPosCache = new Dictionary<IOutlet<object>,Vector2>(); //used in non-layout only! Layout just not aware of what is it
		public List<(Rect,FieldChArrInfo)> fieldRectsCache = new List<(Rect,FieldChArrInfo)>(); //populated on right-click only
		public Dictionary<Generator,(Texture2D,Rect)> genTextures = new Dictionary<Generator, (Texture2D,Rect)>(); //chaching the look of generator to texture to re-use it on drag. Together with texture goes it's world rect to render

		public static HashSet<Generator> lastSelected = new HashSet<Generator>();

		public static Generator highlightedGen = null; //gen mouse hovering over, to repaint on change
		public static HashSet<IInlet<object>> selectedPriorLinks = new HashSet<IInlet<object>>();

		private long lastFrameTime;

		public static Action<Graph> OnGraphChanged;

		public List<Generator> sortedGenerators = new List<Generator>();
		public HashSet<Generator> sortedGeneratorsTempHash = new HashSet<Generator>(); 


		public IMapMagic mapMagic;

		public bool MapMagicRelevant =>
			mapMagic != null  &&  
			mapMagic.Graph != null  &&
			(mapMagic.Graph == current.graph  ||  mapMagic.Graph.ContainsSubGraph(current.graph, recursively:true));

		public void UpdateRelatedMapMagic () => mapMagic = FindRelatedMapMagic(graph); //mostly for public calls, GraphWindow itself can use mm=FindRelated frewfrw
		   
		public static IMapMagic FindRelatedMapMagic (Graph graph)
		//TODO: I don't like it
		{
			if (Selection.activeObject is IMapMagic imm)
				if (imm.ContainsGraph(graph)) return imm;
			//doesn't work with MM object, but leaving here just in case

			//looking in selection
			if (Selection.activeObject is GameObject selectedGameObj)
			{
				MapMagicObject mmo = selectedGameObj.GetComponent<MapMagicObject>();
				if (mmo != null && mmo.ContainsGraph(graph)) return mmo;
				//we can't assign ClusterAsset same way! Add code for it 
			}

			//looking in all objects (only on scene reload or selection change)
			UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
			if (scene != prevSceneLoaded || scene.rootCount != prevRootObjsCount || Selection.activeObject != prevObjSelected)
			{
				MapMagicObject[] allMM = GameObject.FindObjectsOfType<MapMagicObject>();
				for (int m=0; m<allMM.Length; m++)
					if (allMM[m].ContainsGraph(graph)) return allMM[m];

				prevSceneLoaded = scene;
				prevRootObjsCount = scene.rootCount;
				prevObjSelected = Selection.activeObject;

				#if MM_DEBUG
				Debug.Log("Finding MapMagic");
				#endif
			}

			return null;
		}

		public static IMapMagic RelatedMapMagic 
		{get{
			if (current == null  ||  current.mapMagic == null  ||  current.mapMagic.Graph == null) return null;
			if (current.mapMagic.Graph != current.graph  &&  !current.mapMagic.Graph.ContainsSubGraph(current.graph, recursively:true)) return null;
			return current.mapMagic;
		}}

		public void RefreshMapMagic ()
		{
			//if (MapMagicRelevant)  //with new clear system relevancy check is not necessary
				mapMagic?.Refresh(false);

			OnGraphChanged?.Invoke(graph);
		}



		public void OnEnable () 
		{
			#if UNITY_2019_1_OR_NEWER
			SceneView.duringSceneGui -= OnSceneGUI;
			SceneView.duringSceneGui += OnSceneGUI;
			#else
			SceneView.onSceneGUIDelegate -= OnSceneGUI;
			SceneView.onSceneGUIDelegate += OnSceneGUI;
			#endif

			UnityEditor.Undo.undoRedoPerformed -= Repaint;
			UnityEditor.Undo.undoRedoPerformed += Repaint;

			ScrollZoomOnOpen(); //focusing after script re-compile

			selected.Clear(); //removing selection from previous graph

			//setting debug name
			#if MM_DEBUG
			if (graph != null)
				graph.debugName = name;
			#endif
		}


		public void OnDisable () 
		{
			#if UNITY_2019_1_OR_NEWER
			SceneView.duringSceneGui -= OnSceneGUI;
			#else
			SceneView.onSceneGUIDelegate -= OnSceneGUI;
			#endif

			 UnityEditor.Tools.hidden = false; //in case gizmo node is turned on
		}


		public void OnInspectorUpdate () 
		{
			current = this;

			//updating gauge
			if (mapMagic == null) return;
			bool isGenerating = mapMagic.IsGenerating()  &&  mapMagic.ContainsGraph(graph);
			if (wasGenerating) { Repaint(); wasGenerating=false; } //1 frame delay after generate is finished
			if (isGenerating) { Repaint(); wasGenerating=true; }
		}


		private void OnGUI()
		{
			current = this;
			
			mapMagic = FindRelatedMapMagic(graph);

			if (graph==null || graph.generators==null) return;

			if (mapMagic==null) Den.Tools.Tasks.CoroutineManager.Update(); //updating coroutine if no mm assigned (to display brush previews)

			//finding node to highlight
			//disabled because makes no sense. Even not because performance imapct
			/*wantsMouseMove = true; //switches to always update
			if (Event.current.mode == EventType.MouseMove)
				using (ProfilerExt.Profile("Highlighting Generators"))
				{
					Generator prevHighlightedGen = highlightedGen;
					highlightedGen = null;
				
					foreach (Generator gen in graph.generators)
					{
						Rect genRect = graphUI.scrollZoom.ToScreen(gen.guiPosition, gen.guiSize);
						if (genRect.Contains(Event.current.mousePosition - new Vector2(0,toolbarSize)))
							highlightedGen = gen;
					}

					if (highlightedGen != prevHighlightedGen)
						Repaint();

					Event.current.Use(); //we won't need mousemove anymore.
					return;
				}*/

			//de-serializing scroll and zoom pos
			if (!graph.guiScrollZoomLoaded)
			{
				graphUI.scrollZoom.zoomStage = graph.guiZoomStage; 
				graphUI.scrollZoom.zoom.x = graphUI.scrollZoom.ZoomStageToZoom(graph.guiZoomStage);
				graphUI.scrollZoom.zoom.y = graphUI.scrollZoom.zoom.x;
				graphUI.scrollZoom.scroll =  graph.guiScroll;
				graph.guiScrollZoomLoaded = true;
			}

			//fps timer
			#if MM_DEBUG
			long frameStart = System.Diagnostics.Stopwatch.GetTimestamp();
			#endif

			//undo
			if (graphUI.undo == null) 
			{
				graphUI.undo = new Den.Tools.GUI.Undo() { undoObject = graph , undoName = "MapMagic Graph Change" };
				graphUI.undo.undoAction = GraphWindow.current.RefreshMapMagic;
			}
			graphUI.undo.undoObject = graph;

			//graph
			using (new UnityEngine.GUI.ClipScope(new Rect(0, toolbarSize, Screen.width, Screen.height-toolbarSize)))
				graphUI.Draw(DrawGraph, inInspector:false, customRect:new Rect(0, toolbarSize, Screen.width, Screen.height-toolbarSize));

			//toolbar
			using (new UnityEngine.GUI.ClipScope(new Rect(0,0, Screen.width, toolbarSize)))
				toolbarUI.Draw(DrawToolbar, inInspector:false);

			//storing graph scrollzoom focus it on load
			graph.guiScroll = graphUI.scrollZoom.scroll;
			graph.guiZoomStage = graphUI.scrollZoom.zoomStage;

			//preventing switching to main while dragging field (for MMobject only)
			if (mapMagic != null  &&  mapMagic is MapMagicObject mapMagicObject)
			{
				bool newForceDrafts = DragDrop.obj!=null && (DragDrop.group=="DragField" || DragDrop.group=="DragCurve" || DragDrop.group=="DragLevels"); 
				if (!newForceDrafts  &&  mapMagicObject.guiDraggingField)
				{
					mapMagicObject.guiDraggingField = newForceDrafts;
					mapMagicObject.SwitchLods();
				}
				mapMagicObject.guiDraggingField = newForceDrafts;
			}

			//setting debug name
			#if MM_DEBUG
			graph.debugName = name;
			#endif

			//showing fps
			#if MM_DEBUG
			if (graph.debugGraphFps)
			{
				long frameEnd = System.Diagnostics.Stopwatch.GetTimestamp();
				float timeDelta = 1f * (frameEnd-frameStart) / System.Diagnostics.Stopwatch.Frequency;
				float fps = 1f / timeDelta;
				EditorGUI.LabelField(new Rect(10, toolbarSize+10, 150, 18), (timeDelta*1000).ToString("0.0") + "ms, max FPS:" + fps.ToString("0.0"));
			}
			#endif

			//mouse position
			#if MM_DEBUG
			if (graph.debugDrawMousePosition  &&  Event.current.type == EventType.Repaint)
			{
				Vector2 mousePos = Event.current.mousePosition;
				float size = 20f;
				Rect rect = new Rect(mousePos.x - size / 2, mousePos.y - size / 2, size, size);
				//EditorGUI.DrawRect(rect, Color.red);

				mousePos = graphUI.HardwareMousePos;
				rect = new Rect(mousePos.x - size / 2, mousePos.y - size / 2, size, size);
				//EditorGUI.DrawRect(rect, Color.gray);
			}
			#endif

			//moving scene view
			#if MM_DEBUG
			if (graph.drawInSceneView)
				MoveSceneView();
			#endif
		}


		private void DrawGraph ()  {
			using(ProfilerExt.Profile("DrawGraph"))
		{
			if (!graph.validated)
				graph.Validate();

			bool isScheme = UI.current.scrollZoom.zoom.x < 0.4f;
			bool superScheme = UI.current.scrollZoom.zoom.x < 0.2f;

			HashSet<Generator> relayout = new HashSet<Generator>(); //Re-layout all generators on screen AND these


			//updating caches and finding a need to redraw all (on first frame or if collections changed)
			bool gensChanged = genCellsCache.Sync(graph.generators, gen=>new Cell()); //new Cell will Init and Activate on Cell.Cached
			bool groupsChanged = groupCellsCache.Sync(graph.groups, gen=>new Cell());

			if (graph.generators.Length != 0)
				gensChanged = gensChanged  ||  !genCellsCache.ContainsKey(graph.generators[0]); //making sure that it has these generators. Not needed actually

			bool redrawAll = gensChanged || groupsChanged;   //not = redrawAll || Sync() - it will not fire outlets Sync if inlets changes

			if (!UI.current.layout) //we don't know inlets/outlets cells unless they were drawn
			{
				bool inletsChanged = inletsPosCache.Sync(graph.Inlets(), inlet=>new Vector2());  
				bool outletsChanged = outletsPosCache.Sync(graph.Outlets(), outlet=>new Vector2());
				redrawAll = redrawAll || inletsChanged || outletsChanged;
			}

			sortedGenerators.Sync(graph.generators, arrHashSet:sortedGeneratorsTempHash);

			if (UI.current.scrollZoom.zoomChanged) //clearing all cached textures on zoom change to avoid displaying nodes with improper zoom - we'll have to resize those anyways
				genTextures.Clear();

			#if MM_DEBUG
			if (redrawAll)
				Debug.Log("Redrawing all since cache changed");
			#endif

			//debug
			/*float rnd = UnityEngine.Random.Range(0f, 1f);
			if (Event.current.keyCode == KeyCode.LeftArrow)
				foreach (Generator gen in graph.generators)
					{ gen.guiPosition.x += rnd; gen.guiPosition = UI.current.scrollZoom.RoundToZoom(gen.guiPosition); }
			if (Event.current.keyCode == KeyCode.RightArrow)
				foreach (Generator gen in graph.generators)
					{ gen.guiPosition.x -= rnd;  gen.guiPosition = UI.current.scrollZoom.RoundToZoom(gen.guiPosition); }*/


			//background
			using (ProfilerExt.Profile("Background"))
			{
				float gridColor = !UI.current.styles.isPro ? 0.45f : 0.075f;
				float gridBackgroundColor = !UI.current.styles.isPro ? 0.5f : 0.1f;

				#if MM_DEBUG
					if (!graph.debugGraphBackground)
					{
						gridColor = graph.debugGraphBackColor;
						gridBackgroundColor = graph.debugGraphBackColor;
					}
				#endif

				Draw.StaticGrid(
					displayRect: new Rect(0, 0, Screen.width, Screen.height-toolbarSize),
					cellSize:new Vector2(32,32),
					cellOffset:new Vector2(),
					color:new Color(gridColor,gridColor,gridColor), 
					background:new Color(gridBackgroundColor,gridBackgroundColor,gridBackgroundColor),
					fadeWithZoom:true);

				#if MM_DEBUG
					if (graph.drawInSceneView)
					{
						using (Cell.Full)
							DrawSceneView();
					}
				#endif
			}


			//properties
			using (ProfilerExt.Profile("Properties Button"))
			{
				GeneratorInspector[] inspectors = Resources.FindObjectsOfTypeAll<GeneratorInspector>();
				if (inspectors.Length == 0)
					using (Cell.Static(position.width-15,20,20,40))
					{
						GUIStyle style = UI.current.textures.GetElementStyle(
							UI.current.textures.GetColorizedTexture("MapMagic/RoundedNode/SmallCorners", new Color(0.25f, 0.25f, 0.25f, 1)),
							UI.current.textures.GetColorizedTexture("MapMagic/RoundedNode/SmallCorners", new Color(0.5f, 0.5f, 0.5f, 1)),
							hires:true);
						if (Draw.Button("", style:style))
						{
							Event.current.Use();
							GeneratorInspector.ShowEditor( new Rect(position.x+position.width, position.y, 200, position.height) );
						}

						if (Cell.current.Contains(UI.current.mousePos))
							Draw.Element(UI.current.textures.GetTexture("MapMagic/RoundedNode/SmallCorners_Frame"), 
							borders:new Vector4(8,8,8,8),
							color:new Color(1,1,1,0.25f));

						Texture2D chevronRight = UI.current.textures.GetTexture("DPUI/Chevrons/TickRight");
						Draw.Icon(chevronRight);
					}
			}


			//selecting nodes
			if (!UI.current.layout)
				using (ProfilerExt.Profile("Deselecting"))
				{
					bool selectionChanged = GeneratorDraw.SelectGenerators(sortedGenerators, selected, genCellsCache);
					
					if (selectionChanged)
					{
						lastSelected = new HashSet<Generator>(selected);
						UI.RepaintAllWindows();

						//sorting nodes to draw selected last
						sortedGenerators.RemoveAll(e => selected.Contains(e));
						sortedGenerators.AddRange(selected);

						selectedPriorLinks.Clear();

						if (selected.Count != 0)
						{
							foreach (Generator sel in selected)
								foreach (IInlet<object> inlet in sel.AllInlets())
									selectedPriorLinks.Add(inlet);

							//selectedPriors = new HashSet<Generator>(graph.AllPredecessors(highlightedGen));
							//selectedPriorLinks = new HashSet<IInlet<object>>(graph.AllPredecessorLinks(highlightedGen));
						}
					}
				}


			//drawing groups (same as generators)
			using (ProfilerExt.Profile("Groups"))
				using (Cell.Full) //all group cached cells are children of this one
					foreach (Auxiliary aux in graph.groups)
					{
						//skipping out of screen nodes
						if (!redrawAll  &&  !UI.current.IsInWindow(aux.guiPos, aux.guiSize))
							continue;

						//using cached cell
						Cell cell = groupCellsCache[aux]; //should be already added on update cache

						using (Cell.Cached(cell, aux.guiPos.x, aux.guiPos.y, aux.guiSize.x, aux.guiSize.y))
						{
							if (aux is Group group)
							{
								if (!UI.current.layout && !group.pinned)
									GroupDraw.DragGroup(group, graph.generators, graph.groups);

								GroupDraw.DrawGroup(group);
							}
							if (aux is Comment comment)
							{
								GroupDraw.ResizeComment(comment);
								GroupDraw.DrawComment(comment);
							}
						}
					}


			//dragging nodes
			if (!UI.current.layout) //only in repaint - we've got to have rects ready
				using (ProfilerExt.Profile("Dragging Nodes"))
					foreach (Generator gen in sortedGenerators)
						GeneratorDraw.DragGenerator(gen, selected);


			//re-layouting on drag (before links since it affects links positions) //HACK
			if (!UI.current.layout  &&  DragDrop.IsDragging<Generator>()  &&  DragDrop.totalDelta.magnitude >= 2) //only in repaint - after drag, and if drag actually done (more than pixel)
				using (ProfilerExt.Profile("Re-layout"))
					foreach (Generator gen in selected)
						GeneratorDraw.UpdateGeneratorPosition(gen, genCellsCache, inletsPosCache, outletsPosCache);


			//removing links that whose inlet or outlet were not drawn on redrawAll
			if (redrawAll && !UI.current.layout)
				using (ProfilerExt.Profile("Removing Links"))
				{
					List<IInlet<object>> linksToRemove = null;
					foreach (var kvp in graph.links)
					{
						IInlet<object> inlet = kvp.Key;
						IOutlet<object> outlet = kvp.Value;

						if (!inletsPosCache.ContainsKey(inlet) ||
							!outletsPosCache.ContainsKey(outlet) )
							{
								Debug.LogError("Could not find a cell for " + outlet.Gen + " -> " + inlet.Gen + ". Removing link");
								if (linksToRemove == null) linksToRemove = new List<IInlet<object>>();
								linksToRemove.Add((inlet));
								continue;
						}
					}
				
					//if (linksToRemove != null)
					//foreach (IInlet<object> inlet in linksToRemove)
					//	graph.UnlinkInlet(inlet); 
				}

			//drawing links
			if (!UI.current.layout)
				using (ProfilerExt.Profile("Drawing Links"))
					foreach (var kvp in graph.links)
					{
						IInlet<object> inlet = kvp.Key;
						IOutlet<object> outlet = kvp.Value;

						inletsPosCache.TryGetValue(inlet, out Vector2 inletPos);
						outletsPosCache.TryGetValue(outlet, out Vector2 outletPos);

						Color color = GeneratorDraw.GetLinkColor(inlet);
						//if (!selectedPriorLinks.Contains(kvp.Key))
						//	color.a = 0.333f;

						if (!(inlet is IPortalExit<object>  &&  !graph.debugDrawPortalLinks))
							LinkDraw.DrawLink(outletPos, inletPos, color, scheme:superScheme);
					}


			//removing null generators (for test purpose)
			using (ProfilerExt.Profile("Removing null gens"))
				for (int n=graph.generators.Length-1; n>=0; n--)
				{
					if (graph.generators[n] == null)
						ArrayTools.RemoveAt(ref graph.generators, n);
				}


			//drawing generators
			using (ProfilerExt.Profile("Drawing Generators"))
				using (Cell.Full) //all gen cached cells are children of this one
				{
					//if (cell.subCells != null) cell.subCells.Clear();

					Cell.current.id = "Nodes";
					foreach (Generator gen in sortedGenerators)
					{
						
						if (!redrawAll  &&  !UI.current.IsInWindow(gen.guiPosition, gen.guiSize))
							continue;

						GeneratorDraw.DrawGenerator(gen, graph, isSelected:selected.Contains(gen));
					}
				}

			//re-layouting generators on zoom changes to write inlets/outlets positions to repaint //HACK
			if (UI.current.layout  &&  UI.current.scrollZoom.zoomChanged)
				using (ProfilerExt.Profile("Re-layout on zoom"))
					foreach (Generator gen in sortedGenerators)
					{
						if (!redrawAll  &&  !UI.current.IsInWindow(gen.guiPosition, gen.guiSize)) //skipping out of screen nodes
							continue;

						Cell cell = genCellsCache[gen];
						cell.CalculateSubRects();

						GeneratorDraw.DrawGenerator(gen, graph, isSelected:selected.Contains(gen));
					}


			//add/remove button
			//disabled because of user feedback
			//using (ProfilerExt.Profile("Add/Remove Button"))
			//	using (Cell.Full) DragDrawAddRemove(); 

			//right click menu (should have access to cellObjs)
			if (!UI.current.layout  &&  Event.current.type == EventType.MouseDown  &&  Event.current.button == 1)
				RightClick.DrawRightClickItems(graphUI, graphUI.mousePos, graph);

			//create menu on space
			if (!UI.current.layout  &&  Event.current.type == EventType.KeyDown  &&  Event.current.keyCode == KeyCode.Space  && !Event.current.shift)
				CreateRightClick.DrawCreateItems(graphUI.mousePos, graph);

			//delete selected generators
			if (selected!=null  &&  selected.Count!=0  &&  Event.current.type==EventType.KeyDown  &&  Event.current.keyCode==KeyCode.Delete)
				GraphEditorActions.RemoveGenerators(graph, selected);

			//copy-paste selected generators
			if (!UI.current.layout  &&  Event.current.type == EventType.KeyDown  && Event.current.control)
			{
				if (Event.current.keyCode == KeyCode.C)
					GeneratorRightClick.copiedGenerators = graph.Export(selected);
				if (Event.current.keyCode == KeyCode.V)
				{
					(Generator[] iGens,Auxiliary[] iGrps) imported = graph.Import(GeneratorRightClick.copiedGenerators); 
					Graph.Reposition(imported.iGens, imported.iGrps, graphUI.mousePos);
				}
			}

			//redrawing if needed
			if (redrawAll  &&  !UI.current.layout)
				UI.current.DrawAfter( DrawGraph ); 
			
		}}


		private void DrawToolbar () 
		{ 
			//using (Timer.Start("DrawToolbar"))

			using (Cell.LinePx(toolbarSize))
			{
				Draw.Button();

				//Graph graph = CurrentGraph;
				//Graph rootGraph = mapMagic.graph;

				//if (mapMagic != null  &&  mapMagic.graph!=graph  &&  mapMagic.graph!=rootGraph) mapMagic = null;

				UI.current.styles.Resize(0.9f);  //shrinking all font sizes

				Draw.Element(UI.current.styles.toolbar);

	
				//undefined graph
				if (graph==null)
				{
					using (Cell.RowPx(200)) Draw.Label("No graph selected to display. Select:");
					using (Cell.RowPx(100)) Draw.ObjectField(ref graph);
					return;
				}

				//if graph loaded corrupted
				if (graph.generators==null) 
				{
					using (Cell.RowPx(300)) Draw.Label("Graph is null. Check the console for the error on load.");

					using (Cell.RowPx(100))
						if (Draw.Button("Reload", style:UI.current.styles.toolbarButton)) graph.OnAfterDeserialize();

					using (Cell.RowPx(100))
					{
						if (Draw.Button("Reset", style:UI.current.styles.toolbarButton)) graph.generators = new Generator[0];
					}
					
					Cell.EmptyRowRel(1);

					return;
				}

				//root graph
				Graph rootGraph = null;
				if (parentGraphs != null  &&  parentGraphs.Count != 0) 
					rootGraph = parentGraphs[0];
					//this has nothing to do with currently assigned mm graph - we can view subGraphs with no mm in scene at all

				if (rootGraph != null)
				{
					Vector2 rootBtnSize = UnityEngine.GUI.skin.label.CalcSize( new GUIContent(rootGraph.name) );
					using (Cell.RowPx(rootBtnSize.x))
					{
						//Draw.Button(graph.name, style:UI.current.styles.toolbarButton, cell:rootBtnCell);
						Draw.Label(rootGraph.name);
							if (Draw.Button("", visible:false))
								EditorGUIUtility.PingObject(rootGraph);
					}
				
					using (Cell.RowPx(20)) Draw.Label(">>"); 
				}

				//this graph
				Vector2 graphBtnSize = UnityEngine.GUI.skin.label.CalcSize( new GUIContent(graph.name) );
				using (Cell.RowPx(graphBtnSize.x))
				{
					Draw.Label(graph.name);
					if (Draw.Button("", visible:false))
						EditorGUIUtility.PingObject(graph);
				}

				//up-level and tree
				using (Cell.RowPx(20))
				{
					if (Draw.Button(null, icon:UI.current.textures.GetTexture("DPUI/Icons/FolderTree"), iconScale:0.5f, visible:false))
						GraphTreePopup.DrawGraphTree(rootGraph!=null ? rootGraph : graph);
				}

				using (Cell.RowPx(20))
				{
					if (parentGraphs != null  &&  parentGraphs.Count != 0  && 
						Draw.Button(null, icon:UI.current.textures.GetTexture("DPUI/Icons/FolderUp"), iconScale:0.5f, visible:false))
					{
						graph = parentGraphs[parentGraphs.Count-1];
						parentGraphs.RemoveAt(parentGraphs.Count-1);
						ScrollZoomOnOpen();
						Repaint();
					}
				}

				Cell.EmptyRowRel(1); //switching to right corner

				//seed
				Cell.EmptyRowPx(5);
				using (Cell.RowPx(1)) Draw.ToolbarSeparator();

				using (Cell.RowPx(90))
				//	using (Cell.LinePx(toolbarSize-1))  //-1 just to place it nicely
				{
					#if UNITY_2019_1_OR_NEWER
					int newSeed;
					using (Cell.RowRel(0.4f)) Draw.Label("Seed:");
					using (Cell.RowRel(0.6f))
						using (Cell.Padded(1))
							newSeed = (int)Draw.Field(graph.random.Seed, style:UI.current.styles.toolbarField);
					#else
					Cell.current.fieldWidth = 0.6f;
					int newSeed = Draw.Field(graph.random.Seed, "Seed:");
					#endif
					if (newSeed != graph.random.Seed)
					{
						GraphWindow.RecordCompleteUndo();
						graph.random.Seed = newSeed;
						GraphWindow.current?.RefreshMapMagic();
					}
				}

				Cell.EmptyRowPx(2);


				//gauge
				using (Cell.RowPx(1)) Draw.ToolbarSeparator();

				using (Cell.RowPx(200))
					using (Cell.LinePx(toolbarSize-1)) //-1 to leave underscore under gauge
				{
					if (mapMagic != null)
					{
						bool isGenerating = mapMagic.IsGenerating();

						//background gauge
						if (isGenerating)
						{
							float progress = mapMagic.GetProgress();

							if (progress < 1 && progress != 0)
							{
								Texture2D backgroundTex = UI.current.textures.GetTexture("DPUI/ProgressBar/BackgroundBorderless");
								mapMagic.GetProgress();
								Draw.Texture(backgroundTex);

								Texture2D fillTex = UI.current.textures.GetBlankTexture(UI.current.styles.isPro ? Color.grey : Color.white);
								Color color = UI.current.styles.isPro ? new Color(0.24f, 0.37f, 0.58f) : new Color(0.44f, 0.574f, 0.773f);
								Draw.ProgressBarGauge(progress, fillTex, color);
							}

							//Repaint(); //doing it in OnInspectorUpdate
						}

						//refresh buttons
						using (Cell.RowPx(20))
							if (Draw.Button(null, icon:UI.current.textures.GetTexture("DPUI/Icons/RefreshAll"), iconScale:0.5f, visible:false))
							{
								//graphUI.undo.Record(completeUndo:true); //won't record changed terrain data
								if (mapMagic is MapMagicObject mapMagicObject)
								{
									foreach (Terrain terrain in mapMagicObject.tiles.AllActiveTerrains())
										UnityEditor.Undo.RegisterFullObjectHierarchyUndo(terrain.terrainData, "RefreshAll");
									EditorUtility.SetDirty(mapMagicObject);
								}

								current.mapMagic.Refresh(clearAll:true);
							}

						using (Cell.RowPx(20))
							if (Draw.Button(null, icon:UI.current.textures.GetTexture("DPUI/Icons/Refresh"), iconScale:0.5f, visible:false))
							{
								current.mapMagic.Refresh(clearAll:false);
							}

						//ready mark
						if (!isGenerating)
						{
							Cell.EmptyRow();
							#if !MM_DEBUG
							using (Cell.RowPx(40)) Draw.Label("Ready");
							#else
							using (Cell.RowPx(140)) Draw.Label(graph.IdsVersions().ToString());
							#endif
						}
					}

					else
						Draw.Label("Not Assigned to MapMagic Object");
				}

				using (Cell.RowPx(1)) Draw.ToolbarSeparator();

				//focus
				using (Cell.RowPx(20))
					if (Draw.Button(null, icon:UI.current.textures.GetTexture("DPUI/Icons/FocusSmall"), iconScale:0.5f, visible:false))
					{
						graphUI.scrollZoom.FocusWindowOn(GetNodesCenter(graph), position.size);
					}

				using (Cell.RowPx(20))
				{
					if (graphUI.scrollZoom.zoom.x < 0.999f)
					{
						if (Draw.Button(null, icon:UI.current.textures.GetTexture("DPUI/Icons/ZoomSmallPlus"), iconScale:0.5f, visible:false))
							graphUI.scrollZoom.ZoomTo(Vector2.one, position.size/2);
					}
					else
					{
						if (Draw.Button(null, icon:UI.current.textures.GetTexture("DPUI/Icons/ZoomSmallMinus"), iconScale:0.5f, visible:false))
							graphUI.scrollZoom.ZoomTo(new Vector2(0.375f,0.375f), position.size/2); 
					}
				}
			}
		}

		
		private void DrawSceneView ()
		{
			Rect windowRect = UI.current.editorWindow.position;
			SceneView sceneView = SceneView.lastActiveSceneView;

			//drawing
			if (sceneTex == null  ||  sceneTex.width != (int)windowRect.width  ||  sceneTex.height != (int)windowRect.height )
				sceneTex = new RenderTexture((int)windowRect.width, (int)windowRect.height, 24, RenderTextureFormat.ARGB32, 0);
			RenderTexture backTex = sceneView.camera.targetTexture;
			sceneView.camera.targetTexture = sceneTex;
			sceneView.camera.Render();
			sceneView.camera.targetTexture = backTex;

			using (Cell.Custom(
				0, 
				0, 
				UI.current.editorWindow.position.width, 
				UI.current.editorWindow.position.height))
			{
				Cell.current.MakeStatic();
				//Draw.Icon(sceneTex); 
				Draw.Texture(sceneTex);
			}

			//moving/rotating


			
		}
		private static RenderTexture sceneTex = null;

		private void MoveSceneView ()
		{
			SceneView sceneView = SceneView.lastActiveSceneView;

			if (Event.current.alt  &&  Event.current.button == 2)
			{
				Ray rayZero = sceneView.camera.ViewportPointToRay(Vector2.zero);
				Vector3 pointZero = rayZero.origin + rayZero.direction*sceneView.cameraDistance;
				
				Ray rayX = sceneView.camera.ViewportPointToRay(new Vector2(1,0));
				Vector3 pointX = rayX.origin + rayX.direction*sceneView.cameraDistance;

				Ray rayY = sceneView.camera.ViewportPointToRay(new Vector2(0,1));
				Vector3 pointY = rayY.origin + rayY.direction*sceneView.cameraDistance;

				Vector3 axisX = pointZero-pointX;
				Vector3 axisY = pointZero-pointY;

				Vector2 relativeDelta = Event.current.delta / new Vector2(Screen.width, Screen.height);
				relativeDelta.y = -relativeDelta.y;
				sceneView.pivot += axisX*relativeDelta.x + axisY*relativeDelta.y;

				//Debug.DrawLineObject(pointZero, pointX, Color.red);
				//unfortunately ViewportPointToRay uses scene view - and will return improper values if aspect is different

				Repaint();
			}

			if (Event.current.alt  &&  Event.current.button == 0  &&  !Event.current.isScrollWheel)
			{
				Vector3 rotation = sceneView.rotation.eulerAngles;
				rotation.y += Event.current.delta.x / 10;
				rotation.x += Event.current.delta.y / 10;
				sceneView.rotation = Quaternion.Euler(rotation);

				Repaint();
			}

			if (Event.current.alt  &&  Event.current.isScrollWheel) //undocumented!
			{
				//Vector3 camVec = sceneView.camera.transform.position - sceneView.pivot;
				//float camVecLength = camVec.magnitude; 
				//camVecLength *= 1 + Event.current.delta.y*0.02f;

				float size = sceneView.size;
				size *= 1 + Event.current.delta.y*0.02f;

				sceneView.LookAtDirect(sceneView.pivot, sceneView.rotation, size);

				Repaint();
			}	
		}


		private static Vector2 GetNodesCenter (Graph graph)
		{
			//Graph graph = CurrentGraph;
			if (graph.generators.Length==0) return new Vector2(0,0);

			Vector2 min = graph.generators[0].guiPosition;
			Vector2 max = min + graph.generators[0].guiSize;

			for (int g=1; g<graph.generators.Length; g++)
			{
				Vector2 pos = graph.generators[g].guiPosition;
				min = Vector2.Min(pos, min);
				max = Vector2.Max(pos + graph.generators[g].guiSize, max);
			}

			return (min + max)/2;
		}


		private static (Vector2,Vector2) GetAnchorPos (Vector2 genPos, Vector2 genSize)
		{
			Vector2 genCenter = genPos + genSize/2;
			Vector2 anchor =  new Vector2(
				genCenter.x > Screen.width/2 ? 1 : 0,
				genCenter.y > Screen.height/2 ? 1 : 0 );

			Vector2 genCorner = genPos + genSize*anchor;
			Vector2 screenCorner = new Vector2(Screen.width, Screen.height)*anchor;
			Vector2 sign = -(anchor*2 - Vector2.one);
			
			Vector2 pos = (screenCorner + genCorner*sign)*sign;

			Vector2 absPos = pos*sign;
			if (absPos.x < 0) pos.x = 0;
			if (absPos.y < 0) pos.y = 0;

			return (anchor, pos);
		}


		private static Vector2 PlaceByAnchor  (Vector2 anchor, Vector2 pos, Vector2 size)
		{
			Vector2 screenCorner = new Vector2(Screen.width, Screen.height)*anchor;
			Vector2 genCorner = size*anchor;

			return screenCorner - genCorner + pos;
		}


		public void OnSceneGUI (SceneView sceneview)
		{
			if (graph==null || graph.generators==null) return; //if graph loaded corrupted

			bool hideDefaultToolGizmo = false; //if any of the nodes has it's gizmo enabled (to hide the default tool)

			for (int n=0; n<graph.generators.Length; n++)
				if (graph.generators[n] is ISceneGizmo)
				{
					ISceneGizmo gizmoNode = (ISceneGizmo)graph.generators[n];
					gizmoNode.DrawGizmo();
					if (gizmoNode.hideDefaultToolGizmo) hideDefaultToolGizmo = true;
				}
			
			if (hideDefaultToolGizmo) UnityEditor.Tools.hidden = true;
			else UnityEditor.Tools.hidden = false;
		}


		#region Showing Window

			public static GraphWindow ShowInNewTab (Graph graph)
			{
				GraphWindow window = CreateInstance<GraphWindow>();

				window.OpenRoot(graph);

				ShowWindow(window, inTab:true);
				return window;
			}

			public static GraphWindow Show (Graph graph)
			{
				GraphWindow window = null;
				GraphWindow[] allWindows = Resources.FindObjectsOfTypeAll<GraphWindow>();

				//if opened as biome via focused graph window - opening as biome
				if (focusedWindow is GraphWindow focWin  &&  focWin.graph.ContainsSubGraph(graph))
				{
					focWin.OpenBiome(graph);
					return focWin;
				}

				//if opened only one window - using it (and trying to load mm biomes)
				if (window == null)
				{
					if (allWindows.Length == 1)  
					{
						window = allWindows[0];
						if (!window.TryOpenMapMagicBiome(graph))
							window.OpenRoot(graph);
					}
				}

				//if window with this graph currently opened - just focusing it
				if (window == null)
				{
					for (int w=0; w<allWindows.Length; w++)
						if (allWindows[w].graph == graph)
							window = allWindows[w];
				}

				//if the window with parent graph currently opened
				if (window == null)
				{
					for (int w=0; w<allWindows.Length; w++)
						if (allWindows[w].graph.ContainsSubGraph(graph))
						{
							window = allWindows[w];
							window.OpenBiome(graph);
						}
				}

				//if no window found after all - creating new tab (and trying to load mm biomes)
				if (window == null)
				{
					window = CreateInstance<GraphWindow>();
					if (!window.TryOpenMapMagicBiome(graph))
						window.OpenRoot(graph);
				}
					
				ShowWindow(window, inTab:false);
				return window;
			}


			public void OpenBiome (Graph graph)
			/// In this case we know for sure what window should be opened. No internal checks
			{
				if (parentGraphs == null) parentGraphs = new List<Graph>();
				parentGraphs.Add(this.graph);
				this.graph = graph;
				DragDrop.obj = null; //resetting dragDrop or it will move any generator to position of this one on open
				ScrollZoomOnOpen();
			}


			public void OpenBiome (Graph graph, Graph root)
			/// Opens graph as sub-sub-sub biome to root
			{
				parentGraphs = GetStepsToSubGraph(root, graph);
				this.graph = graph;
				DragDrop.obj = null;
				ScrollZoomOnOpen();
			}


			private bool TryOpenMapMagicBiome (Graph graph)
			/// Finds MapMagic object in scene and opens graph as mm biome with mm graph as a root
			/// Return false if it's wrong mm (or no mm at all)
			{
				mapMagic = FindRelatedMapMagic(graph);
				if (mapMagic == null) return false;

				parentGraphs = GetStepsToSubGraph(mapMagic.Graph, graph);
				this.graph = graph;

				DragDrop.obj = null;
				ScrollZoomOnOpen();

				return true;
			}


			private void OpenRoot (Graph graph)
			{
				this.graph = graph;
				parentGraphs = null;

				DragDrop.obj = null; //resetting dragDrop or it will move any generator to position of this one on open
				ScrollZoomOnOpen();
			}


			private static void ShowWindow (GraphWindow window, bool inTab=false)
			/// Opens the graph window. But it should be created and graph assigned first.
			{
				Texture2D icon = TexturesCache.LoadTextureAtPath("MapMagic/Icons/Window"); 
				window.titleContent = new GUIContent("MapMagic Graph", icon);

				if (inTab) window.ShowTab();
				else window.Show();
				window.Focus();
				window.Repaint();

				DragDrop.obj = null;
				window.ScrollZoomOnOpen(); //focusing after window has shown (since it needs window size)
			}


			private static GraphWindow FindReusableWindow (Graph graph)
			/// Finds the most appropriate window among all of all currently opened
			{
				GraphWindow[] allWindows = Resources.FindObjectsOfTypeAll<GraphWindow>();

				//if opened only one window - using it
				if (allWindows.Length == 1)  
					return allWindows[0];

				//if opening from currently active window
				if (focusedWindow is GraphWindow focWin)
					if (focWin.graph.ContainsSubGraph(graph))
						return focWin;
						
				//if window with this graph currently opened
				for (int w=0; w<allWindows.Length; w++)
					if (allWindows[w].graph == graph)
						return allWindows[w];

				//if the window with parent graph currently opened
				for (int w=0; w<allWindows.Length; w++)
					if (allWindows[w].graph.ContainsSubGraph(graph))
						return allWindows[w];

				return null;
			}


			private void ScrollZoomOnOpen ()
			///Finds a graph scroll and zoom from graphsScrollZooms and focuses on them. To switch between graphs
			///should be called each time new graph assigned
			{
				if (graph == null) return; 

				if (graphsScrollZooms.TryGetValue(graph, out Vector3 scrollZoom))
				{
					graphUI.scrollZoom.FocusWindowOn(new Vector2(scrollZoom.x, scrollZoom.y), position.size);
					graphUI.scrollZoom.zoom = new Vector2(scrollZoom.z,scrollZoom.z);
				}

				else
					graphUI.scrollZoom.FocusWindowOn(GetNodesCenter(graph), position.size);
			}


			public static List<Graph> GetStepsToSubGraph (Graph rootGraph, Graph subGraph)
			/// returns List(this > biome > innerBiome)
			/// doesn't include the subgraph itself
			/// doesn't perform check if subGraph is contained within graph at all
			{
				List<Graph> steps = new List<Graph>();
				ContainsSubGraphSteps(rootGraph, subGraph, steps);
				steps.Reverse();
				return steps;
			}


			private static bool ContainsSubGraphSteps (Graph thisGraph, Graph subGraph, List<Graph> steps)
			/// Same as ContainsSubGraph, but using track list for GetStepsToSubGraph
			{
				if (thisGraph == subGraph)
					return true;

				foreach (Graph biomeSubGraph in thisGraph.SubGraphs())
					if (ContainsSubGraphSteps(biomeSubGraph, subGraph, steps))
					{
						steps.Add(thisGraph);
						return true;
					}
				
				return false;
			}


			[MenuItem ("Window/MapMagic/Editor")]
			public static void ShowEditor ()
			{
				MapMagicObject mm = FindObjectOfType<MapMagicObject>();
				Graph gens = mm!=null? mm.graph : null;
				GraphWindow.Show(mm?.graph);
			}

			[UnityEditor.Callbacks.OnOpenAsset(0)]
			public static bool ShowEditor (int instanceID, int line)
			{
				UnityEngine.Object obj = EditorUtility.InstanceIDToObject(instanceID);
				if (obj is Nodes.Graph graph) 
				{ 
					if (graph.generators == null)
						graph.OnAfterDeserialize();
					if (graph.generators == null)
						throw new Exception("Error loading graph");

					if (UI.current != null) UI.current.DrawAfter( new Action( ()=>GraphWindow.Show(graph) ) ); //if opened via graph while drawing it - opening after draw
					else Show(graph); 
					return true; 
				}
				if (obj is MapMagicObject) { GraphWindow.Show(((MapMagicObject)obj).graph); return true; }
				return false;
			}

		#endregion
	}

}//namespace