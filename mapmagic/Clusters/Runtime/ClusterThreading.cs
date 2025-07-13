using UnityEngine;
using System;
using System.Collections;
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

using System.Threading;
using UnityEditor.PackageManager;
using System.Collections.Concurrent;
using UnityEngine.XR;
using static UnityEditor.Experimental.AssetDatabaseExperimental.AssetDatabaseCounters;

namespace MapMagic.Clusters
{
	public partial class Cluster
	{ 

//TODO: GOON: write new log window and debug with it


		private class PauseQueue 
		{
			private struct CoordRev
			{
				public Coord coord;
				public uint revision;
			}

			private class TaskWait
			{
				public ThreadManagerTasks.Task task;
				public List<CoordRev> waitingForList = new List<CoordRev>();

				public bool IsWaiting => waitingForList.Count!=0;
			}

			private List<TaskWait> pausedTasks = new List<TaskWait>();  

			
			public void AddWaitFor (ThreadManagerTasks.Task task, Coord coord, uint revision)
			{
				if (task == null)
					throw new Exception("Trying to add null task"); 

				TaskWait taskWait = pausedTasks.Find(tw => tw.task==task);

				if (taskWait == null)
				{
					taskWait = new TaskWait();
					taskWait.task = task;
					pausedTasks.Add(taskWait);
				}

				taskWait.waitingForList.Add( new CoordRev() {coord=coord, revision=revision} );
			}

			public void RemoveBlocking (Coord coord, uint revision) 
			{
				foreach (TaskWait taskWait in pausedTasks)
				{
					for (int i=taskWait.waitingForList.Count-1; i>=0; i--)
						if (taskWait.waitingForList[i].coord == coord &&
							taskWait.waitingForList[i].revision <= revision)
						{
							taskWait.waitingForList.RemoveAt(i);
						}
				}
			}

			public IEnumerable<ThreadManagerTasks.Task> TasksWithoutBlocking ()
			{
				foreach (TaskWait taskWait in pausedTasks)
					if (!taskWait.IsWaiting)
						yield return taskWait.task;
			}

			public void ClearTasksWithoutBlocking ()
			///Deletes tasks that have no coords
			{
				for (int i=pausedTasks.Count-1; i>=0; i--)
					if (!pausedTasks[i].IsWaiting)
						pausedTasks.RemoveAt(i);
			}

			public void ResumeAndClearTasksWithoutBlocking ()
			///Deletes tasks that have no coords
			{
				HashSet<string> namesSet = new HashSet<string>();

				for (int i=pausedTasks.Count-1; i>=0; i--)
					if (!pausedTasks[i].IsWaiting)
					{
						#if MM_DEBUG
						Log.AddThreaded("ClusterThreading.ResumeAndClearTasksWithoutBlocking", ("name:",pausedTasks[i].task.name));
						#endif

						ThreadManagerTasks.Resume(pausedTasks[i].task);

//						if (namesSet.Contains(pausedTasks[i].task.name))
//							throw new Exception("Trying to unpause tasks with same name"); //actually tasks with same name okay, if all but one are stopped
						namesSet.Add(pausedTasks[i].task.name);

						pausedTasks.RemoveAt(i);
					}
			}

			public void RemoveBlockingAndResumeTasks (Coord coord, uint revision)
			/// Removes this coord from waiting list and resumes tasks that were waiting for it and ONLY for it
			{
				RemoveBlocking(coord, revision);
				ResumeAndClearTasksWithoutBlocking();
			}
		}


		private class GenInfo
		{
			public uint startedRevision;
			public uint generatedRevision;
			public bool inProgress;
			public StopToken stop;
			public ManualResetEvent mre = new ManualResetEvent(true);

			public bool IsReady(uint revision) => generatedRevision >= revision; //Case > should not happen
			public void SetReady(uint revision) => generatedRevision = revision;

			public Coord debugCoord;
			public Coord debugStartedBy;
		}

		[NonSerialized] private PauseQueue pauseQueue = new PauseQueue();
		private ConcurrentDictionary<Coord,GenInfo> generatingCoords = new ConcurrentDictionary<Coord, GenInfo>();

		[NonSerialized] private uint revision = 1; //increments on version of the graph and settings change
		[NonSerialized] private ulong revVersion = 0;

		[NonSerialized] private object lockObj = new object(); //access to generatingList and pauseQueue should be done under lock

		//[NonSerialized] private Dictionary<ThreadManager.Task, List<Coord>> pausedTasks = new Dictionary<ThreadManager.Task, List<Coord>>();
		//paused tasks and list of coords they are waiting to be finished
		



			/*private ProductSet GetAddProductSet (Coord coord)
			///Reads product set - or creates new one if it doesn;t exist
			{
				ProductSet productSet;
				if (!products.TryGetValue(coord, out productSet))
				{
					productSet = new ProductSet();
					products.Add(coord, productSet);
				}

				return productSet;
			}*/

		public void RefreshAreaMutex (Graph graph, Area area, StopToken stop)
		{
			CoordRect clusterCoords = ClusterCoordsByArea(area);

			ThreadManagerTasks.Task task = ThreadManagerTasks.GetCurrentTask();

			lock (lockObj) 
			{

				ulong graphVersion = graph.IdsVersions() + (settingsVersion<<5); 
				if (graphVersion != revVersion)
					revision++;
				revVersion = graphVersion;
			}
		}

		
		public void RefreshArea (Graph graph, Area area, StopToken stop)
		/// Finds a need to update cluster tile containing this area
		/// And generates it if needed
		/// Use case: two tiles share the same two clusters. One tile should start generating cluster1, while the other cluster2
		/// optionalVersion is for example a 
		{
			//snippet for logging
			#if MM_DEBUG
			void Log (string msg, GenInfo clusterInfo)
			{
				if (Den.Tools.Log.enabled)
					Den.Tools.Log.AddThreaded("ClusterThreading.RefreshArea " + msg, idName:area.Coord.ToStringShort(),
						("tile coord:",area.Coord.ToStringShort()), ("tile stop:", stop),
						("cluster coord:", clusterInfo?.debugCoord), ("cl graph ver:",graph.IdsVersionsHash()), ("rev:",revision));
			}
			Log("Starting", null);
			#endif

			//increment revision if needed
			ulong graphVersion = graph.IdsVersions() + (settingsVersion<<5); 
			if (graphVersion != revVersion)
			{
				revision++;
				revVersion = graphVersion;
			}

			//getting clusters of this tile
			CoordRect clusterCoords = ClusterCoordsByArea(area);
			
			GenInfo[] clusterInfos = new GenInfo[clusterCoords.Count]; //tile clusters won't change
			
			int c = 0;
			foreach (Coord coord in clusterCoords)
			{
				GenInfo GenInfoFactory (Coord coord) => new GenInfo();
				GenInfo genInfo = generatingCoords.GetOrAdd(coord, GenInfoFactory(coord)); //returns empty genInfo if not found, and adds it to dict
				genInfo.debugCoord = coord;
				clusterInfos[c] = genInfo;
				c++;
			}

			bool[] pauseOnCluster = new bool[clusterInfos.Length];  
				//checking both: WaitOne(0) and pauseOnCluster. But pauseOnCluster first for performance reasons
				//should be local - depends  on revision
		
			//finding whether need to start clusters
			//and locking them
			for (c=0; c<clusterInfos.Length; c++)
			{
				GenInfo clusterInfo = clusterInfos[c];

				//if this or later revision ready - do nothing
				//if this or later revision generating - pausing and wait for it to finish
				//if it's generating outdated version - stopping it, forgetting, starting new and pausing
				//if it's (not started) or (outdated and not generating) or (generating outdated revision) - starting new thread and pausing this

				if (clusterInfo.generatedRevision < revision)  //if this or later revision ready - do nothing. But if not - run this
					lock (clusterInfo)  //pause, might become ready meanwhile
						if (clusterInfo.generatedRevision < revision)  //checking again after pause
						{
							if (clusterInfo.inProgress  &&  clusterInfo.startedRevision >= revision)
							{
								#if MM_DEBUG
								Log("Deciding to pause while generating cluster", clusterInfo);
								#endif

								pauseOnCluster[c] = true;
							}

							if (clusterInfo.inProgress  &&  clusterInfo.startedRevision < revision)
							{
								#if MM_DEBUG
								Log("Stopping previous cluster thread", clusterInfo);
								#endif

								clusterInfo.stop.stop = true; //TODO: currently it's sharing same stop token with tile. There might be a problem!
								clusterInfo.stop = new StopToken();
								clusterInfo.inProgress = false;
							}

							//if it's (not started) or (outdated and not generating) or (generating outdated revision) - starting new thread and pausing this
							if (!clusterInfo.inProgress) //not else - it's resetting inProgress above
							{
								#if MM_DEBUG
								Log("Starting cluster thread", clusterInfo);
								#endif

								clusterInfo.mre.Reset();
								clusterInfo.inProgress = true;
								clusterInfo.startedRevision = revision;
								clusterInfo.debugStartedBy = area.Coord;
								clusterInfo.stop = new StopToken();
								StopToken clusterStop = clusterInfo.stop; //to capture in ThreadFn

								ProductSet productSet;
								if (!products.TryGetValue(clusterInfo.debugCoord, out productSet))
								{
									productSet = new ProductSet();
									products.Add(clusterInfo.debugCoord, productSet);
								}

								//Why separate thread: one tile can require up to 4 (or even more) clusters
								void ThreadFn() => GenerateThreadedSafe(clusterInfo.debugCoord, clusterInfo, productSet, graph, clusterStop);

								ThreadManager.Enqueue(ThreadFn, 1000000, "Cl " + clusterInfo.debugCoord.ToStringShort());  //priority 1000000 before draft

								pauseOnCluster[c] = true;
							}
						}
			}

			//pausing thread (several times for each cluster) and resuming locks if any
			for (c=0; c<clusterInfos.Length; c++)
			{
				GenInfo clusterInfo = clusterInfos[c];

				if (pauseOnCluster[c]  &&  !stop.stop   &&  !clusterInfo.mre.WaitOne(0))
				//WaitOne(0) true if the MRE is signaled (non-blocked), false if the MRE is not signaled (blocked).
				{
					#if MM_DEBUG
					Log("Pausing tile thread", clusterInfo);
					#endif

					ThreadManager.MarkPaused();
					clusterInfo.mre.WaitOne(); // Wait until the ManualResetEvent is signaled
					ThreadManager.MarkResumed();

					#if MM_DEBUG
					Log("Resumed tile thread", clusterInfo);
					#endif
				
				}
			}

			if (!stop.stop)
				Log("Done", null);
			else
				Log("Stopped", null);

		}


		private void GenerateThreadedSafe (Coord coord, GenInfo clusterInfo, ProductSet productSet, Graph graph, StopToken stop)
		{
			try
			{
				GenerateThreaded(coord, clusterInfo, productSet, graph, stop);
			}
			catch (Exception e)
			{
				lock (lockObj) 
					generatingCoords.TryRemove(coord, out GenInfo tmp);

				throw e; 
			}
		}


		private void GenerateThreaded (Coord coord, GenInfo clusterInfo, ProductSet productSet, Graph graph, StopToken stop)
		{
			//snippet for logging
			#if MM_DEBUG
			void Log (string msg, bool isError=false)
			{
				if (Den.Tools.Log.enabled)
					Den.Tools.Log.AddThreaded("ClusterThreading.GenerateThreaded " + msg, idName:"Cl " + coord.ToStringShort(),
						("started rev:", clusterInfo.startedRevision),
						("coord:", coord), ("cl graph ver:", graph.IdsVersionsHash()), ("rev:", revision), ("stop:", stop.stop));
				if (isError)
					throw new Exception("ClusterThreading.GenerateThreaded " + msg + ". This should not happen");
			}
			Log("Starting");
			#endif

			//data
			TileData data = new TileData();
			data.area = new Area(coord, (int)resolution, margins, tileSize);
			data.globals = globals;
			data.random = graph.random;
			data.isPreview = false;
			data.isDraft = false;

			//generate
			graph.ClearChanged(data); //hacky, cleared was not designed to work in thread, but seem to be working
			graph.Generate(data, stop);

			//mark generated
			lock (clusterInfo)
			{
				uint lastGeneratedRevision = clusterInfo.generatedRevision;
				uint justGeneratedRevision = clusterInfo.startedRevision;
				if (!stop.stop) //do not mark revision if it's cancelled
				{
						if (lastGeneratedRevision < justGeneratedRevision)
						//common case - just generated new version other than last marked
						{
							#if MM_DEBUG
							Log("Storing products");
							#endif

							StoreProductsFromData(productSet, graph, data);  //stores products from tile data to cluster

							clusterInfo.generatedRevision = clusterInfo.startedRevision;
						}

						else if (lastGeneratedRevision > justGeneratedRevision)
						//could generate outdated revision after new one stored - doing nothing
							{ }

						else if (lastGeneratedRevision == justGeneratedRevision)
						{
							#if MM_DEBUG
							Log("REVISION EQUALITY", isError:true);
							#endif
						}

						#if MM_DEBUG
						Log("Unpausing");
						#endif

						clusterInfo.inProgress = false;
						clusterInfo.mre.Set();
				}
			}

			#if MM_DEBUG
			Log("All Done");
			#endif
		}

	}
}