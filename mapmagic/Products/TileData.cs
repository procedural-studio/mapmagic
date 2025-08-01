﻿using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Den.Tools;
using Den.Tools.Matrices;
using MapMagic.Nodes;
using MapMagic.Terrains;
using MapMagic.Core;

namespace MapMagic.Products
{
	public delegate void FinalizeAction (TileData data, StopToken stop);
	public delegate void ApplyAction (TileData data, Terrain terrain, StopToken stop);

	[Serializable, StructLayout (LayoutKind.Sequential)] //to pass to native
	public class StopToken
	/// Data is the same in all chunk's threads. Stop token is unique per thread.
	{
		public bool stop;
		public bool restart;
	}

	public class TileData
	{
		public Area area;
		public Globals globals;
		public Noise random; //a ref to the one in parentGraph (to make it possible generate with no graph) 
		
		public ulong graphChangeVersion; //copy from graph to find if cluster should be re-generated
		public int graphId; 

		public bool isPreview; //should this generate be used as a preview?
		public bool isDraft; //is this terrain low-detail (to avoid applying objects and grass)?

		//per-graph products
		private Dictionary<ulong,ulong> lastVersion = new Dictionary<ulong, ulong>();
		private Dictionary<ulong,float> progress = new Dictionary<ulong,float>();
		private Dictionary<ulong,object> prepare = new Dictionary<ulong, object>();
		private Dictionary<ulong,object> products = new Dictionary<ulong,object>();	//per-outlet intermediate results
		private Dictionary<ulong, (object,object,object)> outputs = new Dictionary<ulong, (object,object,object)>();  //id mainly needed just to overwrite results from same outGens
		
		//per-tile products (refs copied to subDatas)
		private HashSet<FinalizeAction> finalizeMarks = new HashSet<FinalizeAction>();
		private Dictionary<Type,IApplyData> applyMarks = new Dictionary<Type,IApplyData>(); //finalize marks are unique, apply datas are created new each time

		public MatrixWorld heights = null; //last heights applied to floor objects

		//sub-datas
		public Dictionary<ulong,TileData> subDatas = new Dictionary<ulong,TileData>();
		public TileData parentData = null;
		public MatrixWorld biomeMask = null;
		//IDEA: these and products are the only thing that changes in biome sub-data. 
		//finalizeMarks and applyMarks are per-tile, not per-biome (using root to store and read them)
		//Maybe send it with separate argument (and make sub-datas in product set?)

		public string tileName; //for threading debug purpose


		#region Hierarchy

			public TileData CreateLoadSubData (ulong biomeId, MatrixWorld mask)
			/// Creates/loads sub-data and assigns biome mask
			{
				if (!subDatas.TryGetValue(biomeId, out TileData subData))  //sometimes stored area is null on clearing, function node can't generate
				{
					subData = (TileData)this.MemberwiseClone();
					//taking finalize, apply, height by reference from parent data

					subData.lastVersion = new Dictionary<ulong, ulong>();
					subData.products = new Dictionary<ulong, object>();
					subData.prepare = new Dictionary<ulong, object>();
					subData.products = new Dictionary<ulong, object>();
					subData.outputs = new Dictionary<ulong, (object, object, object)>();

					subData.subDatas = new Dictionary<ulong,TileData>();
					subData.parentData = this;

					subDatas.Add(biomeId, subData);
				}

				subData.biomeMask = mask;

				if (subData.area == null) subData.area = area;
				if (subData.globals == null) subData.globals = globals;
				if (subData.random == null) subData.random = random;
				//these were added to fix node remove bug when using function. For some reason stored data on clearing has no defaults

				return subData;
			}


			public TileData CreateLoadSubData (ulong biomeId)  =>  CreateLoadSubData(biomeId, this.biomeMask);
			/// Creates/loads sub-data, but takes current mask reference (for function)


			public TileData GetSubData (ulong biomeId)
			/// Creates (or loads) sub-data for this biome unit
			{
				subDatas.TryGetValue(biomeId, out TileData subData);
				return subData;
			}


			public TileData Root 
			{get{
				if (parentData != null)
					return parentData.Root;
				else
					return this;
			}}


			public IEnumerable<TileData> AllSubs (bool includeItself=false)
			{
			//	if (includeItself)
			//		yield return this;

				foreach (var kvp in subDatas)  //top-level first
					yield return kvp.Value;
			
				foreach (var kvp in subDatas)
				{
					TileData subData = kvp.Value;
					foreach (TileData subSub in subData.AllSubs())
						yield return subSub;
				}
			}

		#endregion

        #region Version Ready

			public bool IsReady (ulong genId, ulong version)
			{
				bool inDict = lastVersion.TryGetValue(genId, out ulong lastVer);
				if (inDict) 
					return version == lastVer;
				return false;
			}

			public bool IsReady (Generator gen)
			{
				bool inDict = lastVersion.TryGetValue(gen.id, out ulong lastVer);
				if (inDict) 
					return gen.version == lastVer;
				return false;
			}

			public bool VersionChanged (Generator gen)
			/// True only if this node version was changed (to determine this node change for biome)
			{
				bool inDict = lastVersion.TryGetValue(gen.id, out ulong lastVer);
				if (inDict) 
					return true;
				return gen.version == lastVer;
			}

			#if MM_DEBUG
			public ulong? Version (Generator gen) 
			{
				bool inDict = lastVersion.TryGetValue(gen.id, out ulong lastVer);
				if (!inDict) 
					return null;
				else
					return lastVer;
			}
			#endif

			public void MarkReady (ulong genId, ulong version) 
			{
				if (!lastVersion.ContainsKey(genId))
					lastVersion.Add(genId, version);
				else
					lastVersion[genId] = version;
			}

			public void MarkReady (Generator gen) 
			{
				if (!lastVersion.ContainsKey(gen.id))
					lastVersion.Add(gen.id, gen.version);
				else
					lastVersion[gen.id] = gen.version;
			}

			public void ClearReady (ulong genId) 
			{
				#if MM_DEBUG
				//Log.AddThreaded("TileData.ClearReady genId", ("coord:",area.Coord), ("draft:",isDraft));
				#endif

				lastVersion.Remove(genId);
			}

			public void ClearReady (Generator gen)
			{
				#if MM_DEBUG
				//Log.AddThreaded("TileData.ClearReady gen", ("coord:",area.Coord), ("draft:",isDraft), ("node:",gen.GetType().Name));
				#endif

				lastVersion.Remove(gen.id);
			}

			public bool AreAllRelevantReady (Graph graph, bool inSubs=false)
			{
				foreach (Generator gen in graph.RelevantGenerators(isDraft))
				{
					bool inDict = lastVersion.TryGetValue(gen.id, out ulong lastVer);
					
					if (!inDict) 
						return false;
					else
						if (gen.version != lastVer)
							return false;
				}

				if (inSubs)
					foreach (IBiome biome in graph.UnitsOfType<IBiome>())
					{
						Graph subGraph = biome.SubGraph;
						if (subGraph == null)
							continue;

						TileData subData;
						if (!subDatas.TryGetValue(biome.Id, out subData))
							continue;

						if (!subData.AreAllRelevantReady(subGraph, inSubs))
							return false;
					}
							
				return true;
			}


			public bool AllOutputsReady (Graph graph, OutputLevel level, bool inSubs=false)
			/// Are all enabled output nodes with level of Level or Both are marked as ready in data
			/// Similar to AreAllRelevantReady, but using proven OutputLevel
			{
				foreach (OutputGenerator outGen in graph.GeneratorsOfType<OutputGenerator>())
				{
					//if enabled level output is NOT ready
					if (outGen.enabled && 
						outGen.OutputLevel.HasFlag(level))
						{
							bool inDict = lastVersion.TryGetValue(outGen.id, out ulong lastVer);
					
							if (!inDict) 
								return false;
							else
								if (outGen.version != lastVer)
									return false;
						}
				}

				if (inSubs)
					foreach (IBiome biome in graph.UnitsOfType<IBiome>())
					{
						if (biome.Gen is MapMagic.Nodes.Biomes.ClusterRead230)
							continue;
						//ANOTHER HACK: clusters might have outputs for cluster setup purpose. They are not ready when using cluster read.

						Graph subGraph = biome.SubGraph;
						if (subGraph == null)
							continue;

						//HACK: check biome for outputs is right, but not always work with current clear/ready system
						//Fails in draft when cleared while generating biome - some internal outputs might not be ready
						//While biome switches to ready on finish, and it is skipped with next wave of generate (after clear)
						//So drafts are using hacky simplified check
						//Update: might work okay with new clear sys
						if (isDraft)
						{
							if (!IsReady(biome.Gen))
								return false;
						}

						else
						{
							TileData subData;
							if (!subDatas.TryGetValue(biome.Id, out subData))
								continue;

							if (!subData.AllOutputsReady(subGraph, level, inSubs))
								return false;
						}
					}

				return true;
			}


			public string[] DebugReadyVar => DebugReady(Graph.debugGraph, false);
			public string[] DebugReady (Graph rootGraph, bool useGraphName=true)
			/// Returns the names of graphs, generators and layers that are marked as ready
			/// Disable graph name when running from thread
			{
				string[] debugs = new string[lastVersion.Count];

				int i=0;
				foreach (ulong id in lastVersion.Keys)
				{
					debugs[i] = rootGraph.DebugUnitName(id,useGraphName);
					if (debugs[i] == null) 
						debugs[i] = $"Null id:{Id.ToString(id)}";

					i++;
				}

				return debugs;
			}

		#endregion

        #region Progress

        public void SetProgress (ICustomComplexity cc, float progress)
			{
				Generator gen = (Generator)cc;
				if (this.progress.ContainsKey(gen.id)) this.progress[gen.id] = progress;
				else this.progress.Add(gen.id, progress);
			}

			public float GetProgress (ICustomComplexity cc)
			{
				Generator gen = (Generator)cc;
				if (this.progress.TryGetValue(gen.id, out float progress)) return progress;
				else return 0;
			}

		#endregion

		#region Prepare

			public T ReadPrepare<T> (Generator gen) where T: class  =>  (T)ReadPrepare(gen.id);
		
			public object ReadPrepare (ulong genId)
			{
				if (prepare.TryGetValue(genId, out object obj)) return obj;
				return null;
			}

			public void StorePrepare (ulong genId, object obj)
			{
				if (obj == null)
					RemovePrepare (genId);

				if (prepare.ContainsKey(genId)) prepare[genId] = obj;
				else prepare.Add(genId, obj);
			}

			public void RemovePrepare (Generator gen)   =>  RemoveProduct(gen.id);

			public void RemovePrepare (ulong genId)  =>  prepare.Remove(genId);

			public int PrepareCount => prepare.Count;

			public void ClearPrepare () => prepare.Clear();

		#endregion

		#region Products

			public T ReadInletProduct<T> (IInlet<T> inlet) where T: class =>  (T)ReadProduct(inlet.LinkedOutletId);
			public T ReadOutletProduct<T> (IOutlet<T> outlet) where T: class  =>  (T)ReadProduct(outlet.Id);
		
			public object ReadProduct (ulong outletId)
			{
				if (outletId == 0) return null; //not connected

				if (products.TryGetValue(outletId, out object obj)) return obj;
				return null;
			}


			public void StoreProduct<T> (IOutlet<T> outlet, T product) where T: class =>  StoreProduct(outlet.Id, product); //generic to avoid assigning wrong object mode

			public void StoreProduct (ulong outletId, object obj)
			{
				if (obj == null)
					RemoveProduct (outletId);

				if (products.ContainsKey(outletId)) products[outletId] = obj;
				else products.Add(outletId, obj);
			}


			public void RemoveProduct (IOutlet<object> outlet)   =>  RemoveProduct(outlet.Id);

			public void RemoveProduct (ulong outletId)  =>  products.Remove(outletId);

			public int ProductsCount  =>  products.Count;

			public void ClearProducts ()  => products.Clear();

			public string[] DebugProductsVar => DebugProducts(Graph.debugGraph, false);
			public string[] DebugProducts (Graph rootGraph, bool useGraphName=true)
			/// Returns the names of graphs, generators and layers that have stored products
			/// Disable graph name when running from thread
			{
				string[] debugs = new string[products.Count];

				int i=0;
				foreach (ulong id in products.Keys)
				{
					string unitName = rootGraph.DebugUnitName(id,useGraphName);
					if (unitName == null) unitName = "Null";

					debugs[i] = unitName + " product:" + products[id];
					i++;
				}

				return debugs;
			}

		#endregion

		#region Outputs

			public void StoreOutput (IUnit unit, object key, object prototype, object product) => 
				StoreOutput(unit.Id, key, prototype, product);
				//key is any kind of object. Action, mode, string, anything. Returns output if keys in get and store are equal
				//prototype: generator or layer

			public void StoreOutput (ulong outputId, object key, object prototype, object obj)
			{
				if (outputs.ContainsKey(outputId)) outputs[outputId] = (key, prototype, obj);
				else outputs.Add(outputId, (key, prototype, obj));
			}


			public IEnumerable<(TPrototype,TProduct,MatrixWorld)> Outputs<TPrototype,TProduct,TMask> (object key, bool inSubs=false)  //TMask mode not used, just made for uniformity
			{
				foreach ((object prototype, object product, MatrixWorld mask) in Outputs(key, inSubs:inSubs))
					yield return ((TPrototype)prototype, (TProduct)product, mask);
			}


			public void GatherOutputs<TPrototype,TProduct> (object key, out TPrototype[] prototypes, out TProduct[] products, out MatrixWorld[] masks, bool inSubs=false) 
			where TProduct: class
			//For all textures outputs - they blend arrays of matrices
			{
				List<TPrototype> prototypesList = new List<TPrototype>();
				List<TProduct> productsList = new List<TProduct>();
				List<MatrixWorld> masksList = new List<MatrixWorld>();

				foreach ((object prototype, object product, MatrixWorld mask) in Outputs(key, inSubs:inSubs))
				{ 
					prototypesList.Add((TPrototype)prototype);
					productsList.Add((TProduct)product);
					masksList.Add(mask);
				}

				prototypes = prototypesList.ToArray();
				products = productsList.ToArray();
				masks = masksList.ToArray();
			}


			public IEnumerable<(object prototype, object product, MatrixWorld biomeMask)> Outputs (object key, bool inSubs=false)
			{
				foreach (var kvp in outputs)
				{
					(object k, object p, object o) = kvp.Value;
					if (k==key)
						yield return (p, o, biomeMask);
				}

				if (inSubs)
					foreach (TileData sub in subDatas.Values)
						foreach (var ppb in sub.Outputs(key, true))
							yield return ppb;
			}



			public int OutputsCount (object key, bool inSubs=false)
			{
				int count = 0;

				foreach (var kvp in outputs)
				{
					(object k, object p, object o) = kvp.Value;
					if (k==key)
						count++;
				}

				if (inSubs)
					foreach (var kvp in subDatas)
						count += kvp.Value.OutputsCount(key, true);

				return count;
			}


			public void RemoveOutput (ulong id, bool inSubs=false) 
			{
				outputs.Remove(id);

				if (inSubs)
					foreach (var kvp in subDatas)
						kvp.Value.RemoveOutput(id, inSubs);
			}

		#endregion

		#region Finalize / Apply

			public void MarkFinalize (FinalizeAction action, StopToken stop)   
			{ 
				if (!finalizeMarks.Contains(action)) 
					//lock (finalizeMarks)
					{
						//Debug.Log("Adding " + finalizeMarks.GetVersion() + " stop:" + stop.stop);
						finalizeMarks.Add(action); 
					}
			}

			public void RemoveFinalize (FinalizeAction action)   => finalizeMarks.Remove(action); 
			
			public void ClearFinalize ()
			{
				//lock (finalizeMarks)
				{
					//Debug.Log("ClearFinalize " + finalizeMarks.GetVersion());
					finalizeMarks.Clear();
				}
			}

			public IEnumerable<FinalizeAction> MarkedFinalizeActions (StopToken stop) //finalize and apply are returned with priorities (height first in this case)
			{ 
				lock (finalizeMarks)
				{
					FinalizeAction heightAction = Nodes.MatrixGenerators.HeightOutput200.finalizeAction;

					if (finalizeMarks.Contains(heightAction))
						yield return heightAction;

					foreach (FinalizeAction action in finalizeMarks) 
					{
						Debug.Log("Iterating " + finalizeMarks.GetVersion() + " stop:" + stop.stop);

						if (action != heightAction)
							yield return action;
					}
				}
			}

			public int FinalizeMarksCount => finalizeMarks.Count;

			public FinalizeAction DequeueFinalize ()
			/// Some finalize actions might be added while finalizing (like objects while finalizing height)
			/// And this is thread safe (IEnumerable isn't)
			/// So using this instead of IEnumerable
			{
				//lock (finalizeMarks)
				{
					//returnning height
					FinalizeAction heightAction = Nodes.MatrixGenerators.HeightOutput200.finalizeAction;
					if (finalizeMarks.Contains(heightAction))
					{ 
						finalizeMarks.Remove(heightAction); 
						return heightAction;
					}

					//returning other actions
					FinalizeAction action = null;
					foreach (FinalizeAction iAction in finalizeMarks)
						{ action = iAction; break; }
				
					finalizeMarks.Remove(action);
					return action;
				}
			}


			public void MarkApply (IApplyData data)  
			{ 
				Type type = data.GetType();
				lock (applyMarks) //added from thread, but read from main
				{
					if (applyMarks.ContainsKey(type))
						applyMarks[type] = data;
					else
						applyMarks.Add(type, data); 
				}
			}

			public int ApplyMarksCount  =>  applyMarks.Count;

			public T ApplyOfType<T> () where T: class,IApplyData
			{
				if (applyMarks.TryGetValue(typeof(T), out IApplyData data))
					return (T)data;
				else return null;
			}

			public IApplyData DequeueApply ()
			{
				IApplyData topPriorityApply = null;
				int topPriority = -1;

				lock (applyMarks) //otherwise can cause "Collection was modified; enumeration operation may not execute."
					foreach (IApplyData data in applyMarks.Values)
					//TODO: enumerators here (especially this one) 
					{
						int priority = GetApplyPriority(data);
						if (priority > topPriority)
							{ topPriority=priority; topPriorityApply=data; }
					}
			
				applyMarks.Remove(topPriorityApply.GetType());
				return topPriorityApply;
			}

			private int GetApplyPriority (IApplyData data)
			{
				switch (data)
				{
					case Nodes.MatrixGenerators.HeightOutput200.ApplySetData setApply: return 10;
					case Nodes.MatrixGenerators.HeightOutput200.ApplySplitData splitApply: return 9;
					case Nodes.MatrixGenerators.HeightOutput200.ApplyTexData texApply: return 8;
					case Nodes.MatrixGenerators.TexturesOutput200.ApplyData texturesApply: return 7;
					case Nodes.MatrixGenerators.GrassOutput200.ApplyData texturesApply: return 6;
				}
				return 0;
			}

		#endregion

		
			
		public void Clear (bool clearApply=true, bool inSubs=false)
		/// Clears all of the unnecessary data in playmode
		{
			lastVersion.Clear();
			products.Clear();
			prepare.Clear();
			outputs.Clear();

			lock (finalizeMarks)
				finalizeMarks.Clear();

			if (clearApply) applyMarks.Clear();

			heights = null; //if (heights != null) heights.Clear(); //clear is faster, but tends to miss an error

			if (inSubs)
			{
				foreach (TileData subData in subDatas.Values)
					subData.Clear(clearApply:clearApply, inSubs:inSubs);
				subDatas.Clear();
			}
		}


		public void Remove (Generator gen, bool inSubs=false)
		{
			lastVersion.Remove(gen.id);
			products.Remove(gen.id);
			prepare.Remove(gen.id);
			outputs.Remove(gen.id);

			if (gen is IMultiOutlet mulOutGen)
				foreach (IOutlet<object> outlet in mulOutGen.Outlets())
					foreach (TileData set in AllSubs())
						set.products.Remove(outlet.Id);

			if (inSubs)
				foreach (TileData subData in subDatas.Values)
					subData.Remove(gen, inSubs:inSubs);
		}


		public void ClearStray (Graph graph, bool inSubs=false)
		/// Clears products that are not in graph
		/// Performance checked: 40ms per 1000 iterations on TutorialSplines graph
		{
			HashSet<ulong> unitsIds = new HashSet<ulong>();
			foreach (IUnit unit in graph.AllUnits())
				unitsIds.Add(unit.Id);

			lastVersion.RemoveNotContained(unitsIds);
			products.RemoveNotContained(unitsIds);
			prepare.RemoveNotContained(unitsIds);
			outputs.RemoveNotContained(unitsIds);

			if (inSubs)
				foreach ((Graph subGraph, TileData subData) in Graph.AllGraphsDatas(graph,this))
					subData.ClearStray(subGraph, inSubs:inSubs);
		}


	}
}