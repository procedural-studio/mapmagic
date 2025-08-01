﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Profiling;

using Den.Tools;
using Den.Tools.Tasks;
using MapMagic.Core;
using MapMagic.Products;
using MapMagic.Nodes;
using static System.Collections.Specialized.BitVector32;

namespace MapMagic.Terrains
{

	//[System.Serializable]
	public class TerrainTile : MonoBehaviour, ITile, ISerializationCallbackReceiver
	// TODO: when using Voxeland, use an area or a special VoxelandTile with the same interface
	{
		public MapMagicObject mapMagic;  //each tile belongs to only one mm object, it could not be changed or copied, both monobehs so no problem with serialization

		public Coord coord = new Coord(int.MaxValue, int.MaxValue);
		public float distance = -1;  //distance in chunks from the center of the deploy rects
		public int Priority => (int)(-distance*100);

		public bool preview = true;

		public TerrainData defaultTerrainData;

		public Rect WorldRect => new Rect(coord.x*mapMagic.tileSize.x, coord.z*mapMagic.tileSize.z, mapMagic.tileSize.x, mapMagic.tileSize.z);
		public Vector2D Min => new Vector2D(coord.x*mapMagic.tileSize.x, coord.z*mapMagic.tileSize.z);
		public Vector2D Max => new Vector2D((coord.x+1)*mapMagic.tileSize.x, (coord.z+1)*mapMagic.tileSize.z);
		public bool ContainsWorldPosition(float x, float z)
		{
			Vector2D worldPos = new Vector2D(coord.x*mapMagic.tileSize.x, coord.z*mapMagic.tileSize.z);
			return  x > worldPos.x  &&   x < worldPos.x+mapMagic.tileSize.x  &&
					z > worldPos.z  &&   z < worldPos.z+mapMagic.tileSize.z;
		}

		public static Action<TerrainTile, TileData> OnBeforeTileStart;
		public static Action<TerrainTile, TileData> OnBeforeTilePrepare;
		public static Action<TerrainTile, TileData, StopToken> OnBeforeTileGenerate;
		public static Action<TerrainTile, TileData, StopToken> OnTileFinalized; //tile event
		public static Action<TerrainTile, TileData, StopToken> OnTileApplied;  //TODO: rename to OnTileComplete. OnTileApplied should be called before switching lod
		public static Action<MapMagicObject> OnAllComplete;
		public static Action<TerrainTile, bool, bool> OnLodSwitched;
		public static Action<TileData> OnPreviewAssigned; //preview tile changed
		public static Action<TerrainTile> OnTileMoved;

		public static Action<TerrainTile> OnBeforeResetTerrain; //mainly for Lock, to save and return stored data
		public static Action<TerrainTile> OnAfterResetTerrain;


		[System.Serializable]
		public class DetailLevel
		{
			[NonSerialized] public TileData data; //also assigned on before serialize
			public Terrain terrain;
			public EdgesSet edges = new EdgesSet(); //edges are serializable, while data is not

			public bool generateStarted = true;	//to avoid starting generate for the second time
			public bool generateReady = false;	//used to control progress bar and lod switch, does not affect task planning
			public bool applyReady = false;		//practice shows two bools better than stage enum

			[NonSerialized] public StopToken stop;  //a tag to stop last assigned task
			[NonSerialized] public Thread thread;
			[NonSerialized] public Action lastExecutedAction; //for draft only to create a new thread to restart
			[NonSerialized] public CoroutineManager.Task coroutine;
			[NonSerialized] public Stack<CoroutineManager.Task> applyMainCoroutines;
			[NonSerialized] public CoroutineManager.Task applyDraftCoroutine;
			[NonSerialized] public CoroutineManager.Task switchLodCoroutine; //should be cancelled somehow, but shouldn't be added to coroutines list (otherwise IsGenerating return true)

			public DetailLevel (TerrainTile tile, bool isDraft) { data=new TileData(); terrain = tile.CreateTerrain(isDraft); }
			public void Remove () { data?.Clear(inSubs:true); if (terrain!=null) GameObject.DestroyImmediate(terrain.gameObject); }
		}

		[NonSerialized] public DetailLevel main;
		[NonSerialized] public DetailLevel draft;
		//serializing on onbeforeserialize

		public ObjectsPool objectsPool;

		public bool guiMain;
		public bool guiDraft;


		public Terrain GetTerrain (bool isDraft)  =>  isDraft ? draft?.terrain : main?.terrain;
		public bool ContainsTerrain (Terrain terrain)  =>  terrain==draft?.terrain  || terrain==main?.terrain;

		public Terrain ActiveTerrain 
		/// Setting null will disable both terrains
		{
			get{
				if (main!=null && main.terrain != null  &&  main.terrain.isActiveAndEnabled) 
					return main.terrain;
				if (draft!=null && draft.terrain != null  &&  draft.terrain.isActiveAndEnabled) 
					return draft.terrain;
				return null;
			}

			set{
				if (main!=null && value==main.terrain)
				{ 
					if (main.terrain != null && !main.terrain.isActiveAndEnabled) 
					{
						main.terrain.gameObject.SetActive(true); 
						//main.terrain.Flush(); //this is required to set neighbors
					}
					if (draft !=null && draft.terrain != null && draft.terrain.isActiveAndEnabled) draft.terrain.gameObject.SetActive(false); 
				}
				else if (draft!=null && value==draft.terrain)
				{
					if (main!=null && main.terrain != null && main.terrain.isActiveAndEnabled) main.terrain.gameObject.SetActive(false); 
					if (draft.terrain != null && !draft.terrain.isActiveAndEnabled) 
					{
						draft.terrain.gameObject.SetActive(true); 
						//draft.terrain.Flush(); 
					}
				}
				else
				{
					if (main?.terrain != null && main.terrain.isActiveAndEnabled) 
						main.terrain.gameObject.SetActive(false); 

					if (draft?.terrain != null && draft.terrain.isActiveAndEnabled) 
						draft.terrain.gameObject.SetActive(false); 
				}
			}
		}


		public void SwitchLod ()
		/// Changes detail level based on main and draft avaialability and readyness
		/// Doesn't start generate (it's done by Dist), only welds drafts (not mains)
		{
			if (this == null) return; //happens after scene switch

			Profiler.BeginSample("Switch Lod");

			bool useMain = main!=null;
			bool useDraft = draft!=null;
			//if both using main
			//if none disabling terrain

			//in editor
			#if UNITY_EDITOR
			if (!MapMagicObject.isPlaying)
			{
				//if both detail levels are used - choosing the one should be displayed
				if (useMain && useDraft) 
				{
					//if generating Draft in DraftData - switching to draft
					if (draft.data!=null  &&  mapMagic?.graph!=null  &&  !draft.data.AllOutputsReady(mapMagic.graph, OutputLevel.Draft, inSubs:true))
						useMain = false;

					//if generating Both in MainData - switching to draft too
					if (main.data!=null  &&  mapMagic?.graph!=null  &&  !main.data.AllOutputsReady(mapMagic.graph, OutputLevel.Draft | OutputLevel.Main, inSubs:true))
						useMain = false;

					//if dragging graph dragfield - do not switch from draft back to main
					if (mapMagic.guiDraggingField  &&  ActiveTerrain == draft.terrain)
						useMain = false; 
				}
			}
			else
			#endif

			//if playmode
			{
				//default case with drafts
				if (mapMagic.draftsInPlaymode)
				{
					if ((int)distance > mapMagic.mainRange)  useMain = false;
					if ((int)distance > mapMagic.tiles.generateRange  &&  mapMagic.hideFarTerrains)  useDraft = false;
				}

				//case no drafts at all
				else
				{
					if ((int)distance > mapMagic.tiles.generateRange  &&  mapMagic.hideFarTerrains)  useMain = false;
					useDraft = false;
				}

				//hiding just moved terrains
				if (main!=null  &&  !main.applyReady) useMain = false; 
				if (draft!=null  &&  !draft.applyReady) useDraft = false; 

				//if main is not ready and using drafts
				if (useMain  &&  useDraft  &&  !main.applyReady) useMain = false;
			}

			//debugging
			//string was = ActiveTerrain==main.terrain ? "main" : (ActiveTerrain==draft.terrain ? "draft" : "null");
			//string replaced = useMain ? "main" : (useDraft ? "draft" : "null");
			//Debug.Log("Switching lod. Was " + was + ", replaced with " + replaced);
			//if (was == "draft" && replaced == "main")
			//	Debug.Log("Test");

			//finding if lod switch is for real and switching active terrain
			Terrain newActiveTerrain;
			if (useMain) newActiveTerrain = main.terrain;
			else if (useDraft) newActiveTerrain = draft.terrain;
			else newActiveTerrain = null;

			bool lodSwitched = false;
			if (ActiveTerrain != newActiveTerrain) 
			{
				lodSwitched = true;
				ActiveTerrain = newActiveTerrain;
			}

			//disabling objects
			bool objsEnabled = useMain; // || (useDraft && mapMagic.draftsIfObjectsChanged);
			bool currentObjsEnabled = objectsPool.isActiveAndEnabled;
			if (!objsEnabled && currentObjsEnabled) objectsPool.gameObject.SetActive(false);
			if (objsEnabled && !currentObjsEnabled) objectsPool.gameObject.SetActive(true);
			

			//welding
			//TODO: check active terrain to know if the switch is for real 
			if (lodSwitched &&
				mapMagic.tiles.Contains(coord) ) //otherwise error on SwitchLod called from Generate (when tile has been moved)
			{
				if (useMain)
				{
					Weld.WeldSurroundingDraftsToThisMain(mapMagic.tiles, coord);
					Weld.WeldCorners(mapMagic.tiles, coord);

					//Weld.SetNeighbors(mapMagic.tiles, coord); 
					//Unity calls Terrain.SetConnectivityDirty on each terrain enable or disable that resets neighbors
					//using autoConnect instead. AutoConnect is a crap but neighbors are broken
				}
				else if (useDraft  &&  draft.applyReady) 
					Weld.WeldThisDraftWithSurroundings(mapMagic.tiles, coord);
			}

			if (lodSwitched) OnLodSwitched?.Invoke(this, useMain, useDraft);

			//CoroutineManager.Enqueue( ()=>Weld.SetNeighbors(mapMagic.tiles, coord) );
			//CoroutineManager.Enqueue( mapMagic.Tmp );
			//mapMagic.Tmp();

			Profiler.EndSample();
		}


		public void ResetTerrain ()
		/// Removes terrain and children, re-constructing tile. Used to clear some output
		{
			OnBeforeResetTerrain?.Invoke(this);

			bool hasMain = main!=null;
			bool hasDraft = draft!=null;

			//removing all children
			for (int i=transform.childCount-1; i>-0; i--)
				GameObject.DestroyImmediate(transform.GetChild(i).gameObject);

			//creating new
			if (hasMain) main = new DetailLevel(this, isDraft:false);
			if (hasDraft) draft = new DetailLevel(this, isDraft:true);
			CreateObjectsPool();

			OnAfterResetTerrain?.Invoke(this);
		}


		#region ITile

			public static TerrainTile Construct (MapMagicObject mapMagic)
			{
				Profiler.BeginSample("Construct Internal");

				GameObject go = new GameObject();
				go.transform.parent = mapMagic.transform;
				TerrainTile tile = go.AddComponent<TerrainTile>();
				tile.mapMagic = mapMagic;
				
				//tile.Resize(mapMagic.tileSize, (int)mapMagic.tileResolution, mapMagic.tileMargins, (int)mapMagic.lodResolution, mapMagic.lodMargins);
				
				//creating detail levels in playmode (for editor Pin us used)
				if (MapMagicObject.isPlaying) //if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
				{
					tile.main = new DetailLevel(tile, isDraft:false); //tile created in any case and generated at the background

					if (mapMagic.draftsInPlaymode)
						tile.draft = new DetailLevel(tile, isDraft:true);
				}

				//creating objects pool
				tile.CreateObjectsPool();

				Profiler.EndSample();

				return tile;
			}


			public void Pin (bool asDraftOnly)
			{
				if (mapMagic.draftsInEditor && draft==null)
					draft = new DetailLevel(this, isDraft:true);

				if (!asDraftOnly && main==null)
					main = new DetailLevel(this, isDraft:false);

				if (asDraftOnly && main!=null)
					{ main.Remove(); main=null; }
			}


			public void Move (Coord newCoord, float newRemoteness)
			{
				coord = newCoord;

				//if (IsGenerating) //stopping anyway just in case
					Stop();

				//clearing
				main?.data?.Clear(inSubs:true);
				draft?.data?.Clear(inSubs:true);

				if (main!=null) { main.applyReady = false;	main.generateReady = false;		main.generateStarted = false; }
				if (draft != null) { draft.applyReady = false;	draft.generateReady = false;	draft.generateStarted = false; }

				ActiveTerrain = null; //disabling terrains

				//resizing (if needed)
				Vector3 size = mapMagic.tileSize.Vector3();
				Vector3 position = new Vector3(coord.x*size.x, 0, coord.z*size.z);

				if (main!=null  &&  main.terrain != null  &&  main.terrain.terrainData.size != new Vector3 (size.x, main.terrain.terrainData.size.y, size.z)) 
					main.terrain.terrainData.size = new Vector3(size.x, main.terrain.terrainData.size.y, size.z);

				if (draft!=null && draft.terrain != null  &&  draft.terrain.terrainData.size != new Vector3 (size.x, draft.terrain.terrainData.size.y, size.z)) 
					draft.terrain.terrainData.size = new Vector3(size.x, draft.terrain.terrainData.size.y, size.z);

				//moving
				transform.localPosition = position;
				gameObject.name = "Tile " + coord.x + "," + coord.z;

				//switch Dist (on each move)
				Dist(newRemoteness);

				OnTileMoved?.Invoke(this);
			}


			public void Dist (float newRemoteness)
			{
				distance = newRemoteness;

				if (MapMagicObject.isPlaying) 	
				{
					if (main != null  &&
						!main.generateStarted  &&
						(int)distance <= mapMagic.mainRange) 
								StartGenerate(mapMagic.graph, generateMain:true, generateLod:false);

					if (draft != null  &&
						!draft.generateStarted  &&
						(int)distance <= mapMagic.tiles.generateRange)
								StartGenerate(mapMagic.graph, generateMain:false, generateLod:true);

					//switching lod in playmode
					if (coord != new Coord(int.MaxValue, int.MaxValue))  //skipping tiles that were just created to avoid showing blank terrain and error on weld
						SwitchLod();
				}

				else //editor mode
				{
					if (draft != null  &&  !draft.generateStarted) StartGenerate(mapMagic.graph, generateMain:false, generateLod:true);
					if (main != null  &&  !main.generateStarted) StartGenerate(mapMagic.graph, generateMain:true, generateLod:false);
				}

				//TODO: switch tasks priorities
			}


			public void Remove ()
			{
				Stop();

				#if UNITY_EDITOR
				if (!UnityEditor.EditorApplication.isPlaying)
					GameObject.DestroyImmediate(gameObject);
				else
				#endif
					GameObject.Destroy(gameObject);
			}


			public bool IsNull {get{ return this==(UnityEngine.Object)null || this.Equals(null) || gameObject==null || gameObject.Equals(null); } }
			
			//public bool Equals(TerrainTile tile) { return (object)this == (object)tile; }

			
			public void Resize ()
			{
				Move(coord, distance);
				//yep, it will change the tile size, including the height
			}


			public Terrain CreateTerrain (bool isDraft)
			{
				GameObject go = new GameObject();
				go.transform.parent = transform;
				go.transform.localPosition = new Vector3(0,0,0);
				go.name = isDraft ? "Draft Terrain" : "Main Terrain";

				Terrain terrain = go.AddComponent<Terrain>();
				TerrainCollider terrainCollider = go.AddComponent<TerrainCollider>();

				TerrainData terrainData;
				TerrainData template = Resources.Load<TerrainData>("MapMagicDefaultTerrainData");
				if (template != null)	
					terrainData = GameObject.Instantiate(template); 
				else
					terrainData = new TerrainData(); 

				terrain.terrainData = terrainData;
				terrainCollider.terrainData = terrainData;
				terrainData.size = mapMagic.tileSize.Vector3();

				mapMagic.terrainSettings.ApplyAll(terrain);
				terrain.groupingID = isDraft ? -2 : -1;

				return terrain;
			}

			public void CreateObjectsPool ()
			{
				GameObject poolGo = new GameObject();
				poolGo.transform.parent = transform;
				poolGo.transform.localPosition = new Vector3();
				poolGo.name = "Objects";
				objectsPool = poolGo.AddComponent<ObjectsPool>();
			}

		#endregion


		#region Async/Task

			/*private Task draftTask;
			private Task mainTask;

			private bool reGenDraft;


			public async Task GenerateAsync (Graph graph, bool genMain, bool genDraft)
			{
				if (draft != null  &&  genDraft) draftTask = GenerateDraftAsync(graph);
				if (main != null  &&  genMain) mainTask = GenerateMainAsync(graph);

				if (draft != null  &&  genDraft) await draftTask;
				if (main != null  &&  genMain) await mainTask;

				SwitchLod();
			}


			public async Task GenerateDraftAsync (Graph graph)
			{
				if (draftTask != null && !draftTask.IsCompleted)
					{ reGenDraft = true; return; }

				draftTask = GenerateDraftAsyncInternal(graph);
				await draftTask;

				if (reGenDraft)
				{
					reGenDraft = false;
					draftTask = GenerateDraftAsyncInternal(graph);
					await draftTask;
				}
			}

			public async Task GenerateDraftAsyncInternal (Graph graph)
			{
				//cancel the task that's already running
				if (draftTask != null && !draftTask.IsCompleted)
				{
					//draft.Data.stop = true; //don't stop draft, make it refresh constantly

					//but make it don't wait if it wasn't started

					//await draftTask;
				}
				
				draft.data.area = new Area(coord, (int)mapMagic.draftResolution, mapMagic.draftMargins, mapMagic.tileSize);
				draft.data.parentGraph = graph;
				draft.data.random = graph.random;
				draft.data.isPreview = false; //don't preview draft in any case
				draft.data.isDraft = false;
				//draft.Data.stop = false;

				//draft.Data.parentGraph.CheckClear(draft.Data);
				await Task.Run( ()=> draft.data.parentGraph.CheckClear(draft.data) );

				draft.data.parentGraph.Prepare(draft.data, main.terrain);

				await Task.Run (() =>
				{
					draft.data.parentGraph.Generate(draft.data);
					draft.data.parentGraph.Finalize(draft.data);
				});

				//draft.Data.parentGraph.Generate(draft.Data);
				//draft.Data.parentGraph.Finalize(draft.Data);

				//if (draft.Data.stop) return;

				if (draft.terrain == null) draft.terrain = CreateTerrain("Draft Terrain");

				while (draft.data.ApplyCount != 0)
				{
					ITerrainData apply = draft.data.DequeueApply(); //this will remove apply from the list
					apply.Apply(draft.terrain);
				}
			}


			public async Task GenerateMainAsync (Graph graph)
			{
				//cancel the task that's already running
				if (mainTask != null && !mainTask.IsCompleted)
				{
					//draft.Data.stop = true; //don't stop draft, make it refresh constantly
					await mainTask;
				}
				
				main.data.area = new Area(coord, (int)mapMagic.tileResolution, mapMagic.tileMargins, mapMagic.tileSize);
				main.data.parentGraph = graph;
				main.data.random = graph.random;
				main.data.isPreview = preview;
				main.data.isDraft = false;
				//main.Data.stop = false;

				//clear changed nodes for main data first to see if draft should be switched
				await Task.Run( ()=> main.data.parentGraph.CheckClear(main.data) );

				//prepare
				main.data.parentGraph.Prepare(main.data, main.terrain);

				//generate
				await Task.Run ( ()=>
				{
					main.data.parentGraph.Generate(main.data);
					main.data.parentGraph.Finalize(main.data);

					//saving last generated results to use as preview
					//if (main.data.isPreview) 
					//	main.data.parentGraph.lastGeneratedResults.Target = main.data.products; //TODO: to MapMagic?

					//merging locks (by event?)
					//for (int l=0; l<main.data.lockReads.Count; l++)
					//	main.data.lockReads[l].MergeLocks(main.data.terrainApply);
				});

				//if (main.Data.stop) return;

				if (main.terrain == null) main.terrain = CreateTerrain("Main Terrain");

				while (main.data.ApplyCount != 0)
				{
					ITerrainData apply = main.data.DequeueApply(); //this will remove apply from the list
					apply.Apply(main.terrain);
				}
			}*/

		#endregion


		#region Threaded

			public void Refresh (Graph graph, bool clearAll=false) 
			/// Stops ongoing tasks, clears change, starts again - in that order.
			/// Clear change between stop and start - stopping it after clearing change might result in some output ready - with outdated data
			{
				if (main != null)
					StopTask(main); //stopping only main tiles - drafts update one by one until the end

				ClearChanged(graph, clearAll);

				StartGenerate(graph, generateMain:true, generateLod:true);
			}


			public void ClearChanged (Graph graph, bool clearAll=false)
			{
				if (clearAll)
				{
					Stop(); //this will reset tile tasks

					main?.data?.Clear(inSubs:true);
					draft?.data?.Clear(inSubs:true); 
				}

				if (main?.data!=null) 
					graph.ClearChanged(main.data, clearAll);
					
				if (draft?.data!=null) 
					graph.ClearChanged(draft.data, clearAll);
			}


			public void StartGenerate (Graph graph, bool generateMain=true, bool generateLod=true)
			/// Starts generating tile in a separate thread (or just enqueues it if `launch` is set to false)
			{
				if (graph==null) return;

				//starting draft
				if (generateLod  &&  draft != null)
				{
					if (draft.data == null) draft.data = new TileData();
					draft.data.area = mapMagic.DraftArea(coord);
					draft.data.globals = mapMagic.globals;
					draft.data.random = graph.random;
					draft.data.isPreview = false; //don't preview draft in any case
					draft.data.isDraft = true;
					draft.data.tileName = "Draft " + coord.ToStringShort();

					//if (draft.coroutines == null) draft.coroutines = new Stack<CoroutineManager.Task>();
					//while (draft.coroutines.Count != 0)
					//	CoroutineManager.Stop(draft.coroutines.Pop());

					draft.generateStarted = true;
					draft.applyReady = false;
					draft.generateReady = false;

					#if MM_DEBUG
					Log("StartGenerate Draft", graph, draft.data, draft.stop);  
					#endif

					EnqueueDraft(draft, graph, Priority+1000);
				}

				//starting main
				if (generateMain  &&  main != null)
				{
					if (main.data == null) main.data = new TileData();
					main.data.area = mapMagic.MainArea(coord);
					main.data.globals = mapMagic.globals;
					main.data.random = graph.random;
					main.data.isPreview = mapMagic.PreviewData==main.data;
					main.data.isDraft = false;
					main.data.tileName = "Main " + coord.ToStringShort();

					main.generateStarted = true;
					main.applyReady = false;
					main.generateReady = false;

					#if MM_DEBUG
					Log("StartGenerate Main", graph, main.data, main.stop);  
					#endif

					EnqueueMain(main, graph, Priority);
					//EnqueueTask(main, graph, Priority, "Main");
				}

				SwitchLod(); //switching to draft if needed
			}


			private void EnqueueMain (DetailLevel det, Graph graph, int priority=0)
			///TODO: unify enqueue and test. The only difference is in capturing stop
			{
				if (det.thread == null  ||  !ThreadManager.Enqueued(det.thread))
				{
					Prepare(graph, this, main);

					det.stop = new StopToken();
					StopToken stop = det.stop; //closure var

					Action threadFn = ()=>Generate(graph, this, det, stop);  //TODO: use parametrized thread start
					det.thread = ThreadManager.CreateThread(threadFn);

					ThreadManager.Enqueue(det.thread, priority, det.data.tileName);
				}
				//do nothing if task enqueued (but not started)
			}


			private void EnqueueDraft (DetailLevel det, Graph graph, int priority=0)
			{
				if (det.thread == null  ||  det.thread.ThreadState.HasFlag(ThreadState.Stopped))
				{
					det.stop = new StopToken();

					Action threadFn = ()=>Generate(graph, this, det, det.stop);  //graph captured, stop isn't
					det.lastExecutedAction = threadFn;
					det.thread = ThreadManager.CreateThread(threadFn); 
				}

				if (det.thread.IsAlive) 
				{
					det.stop.restart = true;

					#if MM_DEBUG
					Log("EnqueueDraft Switched restart because it's alive", graph, main.data, main.stop);  
					#endif
				}
				else
				{
					if (!ThreadManager.Enqueued(det.thread)) 
					{
						Prepare(graph, this, det);
						ThreadManager.Enqueue(det.thread, priority, det.data.tileName);
					}
				}
			}


			private void StopTask (DetailLevel det, bool dequeue=true)
			/// Will stop previous task before running
			{
				//stopping coroutines
				if (det.applyMainCoroutines == null) det.applyMainCoroutines = new Stack<CoroutineManager.Task>();
				while (det.applyMainCoroutines.Count != 0)
					CoroutineManager.Stop(det.applyMainCoroutines.Pop());

				if (det.switchLodCoroutine != null)
					CoroutineManager.Stop(det.switchLodCoroutine);

				if (det.coroutine != null)
					CoroutineManager.Stop(det.coroutine);

				//dequeue
				if (dequeue && det.thread!=null)
				{
					if (ThreadManager.TryDequeue(det.thread))
					{
						//do nothing - already dequeued

						#if MM_DEBUG
						Log("StopTask Dequeuening", null, det.data, det.stop); 
						#endif
					}
				}

				//active
				if (det.thread != null  &&  det.thread.IsAlive) 
				{
					if (det.stop != null)
					{
						det.stop.stop = true;
						det.stop.restart = false;

						#if MM_DEBUG
						Log("StopTask Active stopped", null, det.data, det.stop);  
						#endif
					}
				}

				//forgetting task if it was dequeued
				if (dequeue)
					det.thread = null;
			}


			public void Stop ()
			{
				if (main != null) StopTask(main, dequeue:true);
				if (draft != null) StopTask(draft, dequeue:true);
			}


			private void Prepare (Graph graph, TerrainTile tile, DetailLevel det)
			{
				det.edges.ready = false;

				OnBeforeTilePrepare?.Invoke(tile, det.data);

				graph.Prepare(det.data, det.terrain);
				//was using data's parent graph
			}


			private void Generate (Graph graph, TerrainTile tile, DetailLevel det, StopToken stop)
			/// Note that referencing det.task is illegal since task could be changed
			{
				OnBeforeTileGenerate?.Invoke(tile, det.data, stop);

				//do not return (for draft) until the end (apply)
//				if (!stop.stop) graph.CheckClear(det.data, stop);
				if (!stop.stop) graph.Generate(det.data, stop);
				if (!stop.stop) graph.Finalize(det.data, stop);

				//finalize event
				OnTileFinalized?.Invoke(tile, det.data, stop);
					
				//flushing products for playmode (all except apply)
				if (MapMagicObject.isPlaying)
					det.data.Clear(clearApply:false, inSubs:true);

				//welding (before apply since apply will flush 2d array)
				if (!stop.stop) Weld.ReadEdges(det.data, det.edges);
				if (!stop.stop) Weld.WeldEdgesInThread(det.edges, tile.mapMagic.tiles, tile.coord, det.data.isDraft);
				if (!stop.stop) Weld.WriteEdges(det.data, det.edges);

				//enqueue apply 
				//was: while the playmode is applied on SwitchLod to avoid unnecessary lags for main

				if (det.data.isDraft)
					det.coroutine = CoroutineManager.Enqueue(()=>ApplyNow(det,stop), Priority+1000, "ApplyNow " + coord, "Apply");

				else //main
				{
					IEnumerator coroutine = ApplyRoutine(det, stop);
					det.coroutine = CoroutineManager.Enqueue(coroutine, Priority, "ApplyRoutine " + coord, "Apply");
				}		
				
				det.generateReady = true;
			}


			private void ApplyNow (DetailLevel det, StopToken stop)
			{
				if (this == null) return;

				if (stop==null || !stop.stop)
				{
					while (det.data.ApplyMarksCount != 0)
					{
						var appDat = det.data.DequeueApply();
						appDat.Apply(det.terrain);
					}

					//MapMagicObject.OnTileApplied?.Invoke(this, det.data, stop);

					det.applyReady = true; //enabling ready before switching lod (otherwise will leave draft)

					SwitchLod();

					OnTileApplied?.Invoke(this, det.data, stop);

					//if (!mapMagic.IsGenerating()) //won't be called since this couroutine still left
					if (!ThreadManager.IsWorking("Main") && CoroutineManager.IsTagEnqueued("Apply"))
					{
						OnAllComplete?.Invoke(mapMagic);

						#if MM_DEBUG
						Log("ApplyNow OnAllComplete invoked", null, det.data, det.stop);  
						#endif
					}
				}

				if (stop.restart) 
				{ 
					stop.restart=false;

					if (!det.thread.ThreadState.HasFlag(ThreadState.Unstarted)) //do nothing if it's just in queue
					{
						det.thread = ThreadManager.CreateThread(det.lastExecutedAction); //starting new thread to go again 
						ThreadManager.Enqueue(det.thread, Priority, det.data.tileName); 

						#if MM_DEBUG
						Log("ApplyNow Enqueued for restart", null, det.data, det.stop);  
						#endif
					}
						

					//if (!ThreadManager.TryDequeue(det.thread))
					//	det.thread = ThreadManager.CreateThread(det.lastExecutedAction); //starting new thread to go again

					//ThreadManager.Enqueue(det.thread, Priority, "Restart " + (det==main ? "Main " : "Draft ") + coord.ToStringShort()); 
				}
			}


			private IEnumerator ApplyRoutine (DetailLevel det, StopToken stop)
			{
				if (this == null) yield break;

				if (stop==null || !stop.stop)
				{
					while (det.data.ApplyMarksCount != 0)
					{
						if (stop!=null && stop.stop) yield break;

						IApplyData apply = det.data.DequeueApply();	//this will remove apply from the list
																	//coroutines guarantee FIFO
						if (apply is IApplyDataRoutine)
						{
							IEnumerator routine = (apply as IApplyDataRoutine).ApplyRoutine(det.terrain);
							while (true) 
							{
								if (stop!=null && stop.stop) yield break;

								bool move = routine.MoveNext();
								yield return null;

								if (!move) break;
							}
						}
						else
						{
							apply.Apply(det.terrain);
							yield return null;
						}
					}
				}

				if (stop==null || !(stop.stop || stop.restart)) //can't set ready when restart enqueued
				{
					det.applyReady = true; //enabling ready before switching lod (otherwise will leave draft)

					SwitchLod();

					OnTileApplied?.Invoke(this, det.data, stop);
					
					//if (!mapMagic.IsGenerating()) //won't be called since this couroutine still left
					if (!ThreadManager.IsWorking("Main") && !CoroutineManager.IsTagEnqueued("Apply"))
					{
						OnAllComplete?.Invoke(mapMagic);

						#if MM_DEBUG
						Log("ApplyRoutine OnAllComplete invoked", null, det.data, det.stop);  
						#endif
					}
				}

				if (stop!=null && stop.restart) 
				{ 
					stop.restart=false; 

					if (!ThreadManager.Enqueued(det.thread)) 
						ThreadManager.Enqueue(det.thread, Priority, det.data.tileName); 

					#if MM_DEBUG
					Log("ApplyRoutine Enqueued for running again", null, det.data, det.stop);  
					#endif
				}
			}


			public (float progress, float max) GetProgress (Graph graph, float generateComplexity, float applyComplexity)
			{
				float progress = 0;
				float max = 0;

				if (main != null  &&  main.generateStarted)
				{
					max += generateComplexity + applyComplexity;

					if (main.generateReady) progress += generateComplexity;
					else if (main.data != null)  progress += graph.GetGenerateProgress(main.data);

					if (main.applyReady) progress += applyComplexity;
					else if (main.data != null) progress += graph.GetApplyProgress(main.data);
				}

				if (draft != null  &&  draft.generateStarted)
				{
					max += 2;
					if (draft.generateReady) progress ++;
					if (draft.applyReady) progress ++;
				}

				return (progress, max); 
			}


			public bool IsGenerating 
			{get{
				if (main != null  &&  main.generateStarted  &&  !main.applyReady) return true;
				if (draft != null  &&  draft.generateStarted  &&  !draft.applyReady) return true;
				return false;
			}}

			public bool Ready
			{get{
				if (main != null  &&  (!main.applyReady || !main.generateReady)) return false;
				if (draft != null  &&  (!draft.applyReady || !draft.generateReady)) return false;
				return true;
			}}

			private void Log (string msg, Graph graph, TileData data, StopToken stop)
			{
				#if MM_DEBUG
				if (Den.Tools.Log.enabled)
					Den.Tools.Log.AddThreaded("TerrainTile."+msg, idName:data.tileName,
						("coord",data.area.Coord), 
						("is draft",data.isDraft), 
						("graph ver", graph?.IdsVersionsHash()), 
						("test val",data.heights?.arr[100]),
						("stop:",stop.stop), 
						("restart:",stop.restart) );
				#endif
			}
				
			//public bool ReadyDraft
			//	{get{ return draft!=null && draft.stage != DetailLevel.Stage.Blank && draft.stage != DetailLevel.Stage.Ready; }}

		#endregion


		#region Serialization

			[SerializeField] private DetailLevel serialized_main;
			[SerializeField] private bool serialized_mainNull;

			[SerializeField] private DetailLevel serialized_draft;
			[SerializeField] private bool serialized_draftNull;

			public void OnBeforeSerialize () 
			{
				serialized_main = main;
				serialized_mainNull = main==null;

				serialized_draft = draft;
				serialized_draftNull = draft==null; 
			}


			public void OnAfterDeserialize () 
			{
				if (!serialized_mainNull)  
				{ 
					main = serialized_main;  
					//main.data = new TileData(); //data is not serialized, so it will be null

					if (!main.applyReady || !main.generateReady) //resetting ready state if it's not completely generated
						{ main.applyReady = false; main.generateReady = false; }
				}

				if (!serialized_draftNull) 
				{ 
					draft = serialized_draft;  
					//draft.data = new TileData();

					if (!draft.applyReady || !draft.generateReady) //resetting ready state if it's not completely generated
						{ draft.applyReady = false; draft.generateReady = false; }
				}
			}

		#endregion
	}

}