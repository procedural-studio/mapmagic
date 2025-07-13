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
	[HelpURL("https://gitlab.com/denispahunov/mapmagic/wikis/home")]
	[CreateAssetMenu(menuName = "MapMagic/Cluster Asset", fileName = "Cluster.asset", order = 113)]
	[Serializable, PreferBinarySerialization]
	public class ClusterAsset : ScriptableObject, ISerializationCallbackReceiver
	/// MapMagic object with no terrains and one tile that outputs results to dictionary
	/// And no Apply, that simplyfies things a bit
	{ 
		private class ProductInfo
		{
			public string name;
			public Type type; //got to display outputs of this mode in parent graph, even if the product is null
			public object product;
		}

		[NonSerialized] private Dictionary<ulong,ProductInfo> products = new Dictionary<ulong, ProductInfo>();
		[NonSerialized] private List<ulong> order = new List<ulong>();

		//two independent dictionaries: for types (they go together for gui) and products (they used only in generate)
		//[NonSerialized] private Dictionary<string, Type> storedTypes = new Dictionary<string,Type>();
		//[NonSerialized] private Dictionary<string, object> storedObjs = new Dictionary<string,object>();
		//[SerializeField] private List<string> storedOrder = new List<string>();

		//private Dictionary<string, (Type mode, object obj)> dict = new Dictionary<string, (Type, object)>();
		//private DictionaryOrdered<string, Obj> dict = new DictionaryOrdered<string,Obj>();



		public Graph graph;
		public Graph Graph => graph;

		public Vector2D worldPos = new Vector2D(0,0);
		public Vector2D worldSize = new Vector2D(1000,1000);
		public int resolution = 513;

		public Globals globals;
		public Globals Globals => globals;

		[NonSerialized] public TileData data;
		[NonSerialized] public StopToken stop;  //a tag to stop last assigned task, dont confuse with Generate argument

		[NonSerialized] public bool generateStarted = false;
		[NonSerialized] public bool generateReady = false;

		public bool CheckUpToDate () => graph!=null  &&  graph.IdsVersions()==lastChangeVersion;
		public ulong lastChangeVersion;
		public ulong lastStartedVersion; //graph version that started to generate, but not yet finished
		public ulong lastDraftWaitingVesrion; //to pause only 1 draft thread

		public object generateLocker = new object();

		public static Action<ClusterAsset> OnClusterGenerated;

		#region Sorted Dictionary

			public int Count => order.Count;

			public bool ContainsId (ulong id) => products.ContainsKey(id);

			public object GetProduct (ulong id)
			{
				if (products.TryGetValue(id, out ProductInfo info))
					return info.product;
				else
					return null;
			}

			public T GetProduct<T> (ulong id) where T: class
			{
				if (products.TryGetValue(id, out ProductInfo info)  &&  info.type==typeof(T)  &&  info.product is T tp)
					return tp;
				else
					return null;
			}

			public string GetName (ulong id) //used to rename outlets on sync
			{
				if (products.TryGetValue(id, out ProductInfo info))
					return info.name;
				else
					return null;
			}

			public void GetProductNameType (ulong id, out object product, out string name, out Type type)
			{
				if (products.TryGetValue(id, out ProductInfo info))
				{
					product = info.product;
					name = info.name;
					type = info.type;
				}
				else
				{
					product = null;
					name = null;
					type = null;
				}
			}

			public IEnumerable<(ulong id, object product, string name, Type type)> IdsProductsNamesTypes ()
			{
				foreach (ulong id in order)
				{
					ProductInfo info = products[id];
					yield return (id, info.product, info.name, info.type);
				}
			}

			public string[] StoredNames ()
			{
				int count = order.Count;
				string[] arr = new string[count];

				for (int i=0; i<count; i++)
					arr[i] = products[order[i]].name; 

				return arr;
			}

			public Type[] StoredTypes ()
			{
				int count = order.Count;
				Type[] arr = new Type[count];

				for (int i=0; i<count; i++)
					arr[i] = products[order[i]].type;

				return arr;
			}

			public object[] StoredProducts ()
			{
				int count = order.Count;
				object[] arr = new object[count];

				for (int i=0; i<count; i++) 
					arr[i] = products[order[i]].product;

				return arr;
			}

			public object GetProductByNum (int num)
			{
				ulong id = order[num];
				return products[id].product;
			}

			public T GetProductByNum<T> (int num) where T: class
			{
				ulong id = order[num];
				object product = products[id].product;

				if (product == null) 
					return null;

				if (product is T tp)
					return tp;
				else 
					return null;
			}

			public void StoredCachedIdsNames (ref ulong[] ids, ref string[] names, Type type=null)
			/// Just like getting StoredNames, but does not create new arrays. Used for GUI dropdown lists
			/// Always return in order stored in order list
			/// If mode is provided operates on current mode only
			{
				int count;
				if (type==null)
					count = order.Count;
				else
				{
					count = 0;
					foreach (var kvp in products)
						if (kvp.Value.type == type)
							count++;
				}

				if (ids==null || ids.Length != count) ids = new ulong[count];
				if (names==null || names.Length != count) names = new string[count];

				int i=0;
				foreach (ulong id in order)
				{
					ProductInfo info = products[order[i]];
					if (info.type == type)
					{
						ids[i] = id;
						names[i] = info.name;
						i++;
					}
				}
			}

		#endregion

		#region Producing

			public void Produce ()
			///Main function to force cluster produce something
			{
				CheckInitData();
                Prepare();
				Generate(); //generating with stop null will crash Unity in thread
			}

			public void RepopulateOutputs (bool removeUnused = false)
			///Just updates outputs types and names, without actual generate (nulls for new products)
			{
				lock (products)
				//TODO: lock products on generate as well!
					foreach (IFnExit<object> exit in graph.GeneratorsOfType<IFnExit<object>>())
				{
					ulong id = exit.Id;
					string name = exit.Name;
					Type type = Generator.GetGenericType(exit);

					ProductInfo info;
					if (products.TryGetValue(id, out info))
					{
						info.name = name;
						info.type = type;
					}
					else
					{
						info = new ProductInfo() {name=name, type=type, product=null};
						products.Add(id, info);
						order.Add(id);
					}
				}
			}

			public void UpdateCluster (Coord coord, bool isDraft, StopToken stop=null) //Do not name it just Update - won't work in OpenGL (!)
			{
				//do not pause execution for drafts - this will pause appearing of far tiles (when clusters will be infinite)
				//and will not update the drafted view while mouse pressed

				//pausing execution for draft until cluster is generated from main thread
//				if (isDraft  &&  ThreadManager.useMultithreading)
				{
					//pausing will just stack draft threads (with higher priority) and will not allow cluster to be jenerated
					//instead it is refreshing MM with event on cluster ready from ClusterEditor.UpdateDraftsOnClusterGenerated
				}

//				else //draft generates trash without stop, this trash should not go to cluster
				{
					lock (generateLocker)
					//tiles here one by one
					{
						#if MM_DEBUG
						Debug.Log("Starting Tile: " + coord + " isDraft:" + isDraft);
						#endif

						if (isDraft) //draft will not stop on graph change
						{
							if (!CheckUpToDate()) //if graph changed during generate - it's okay for draft
								Generate();
						}

						else //if main
						{
							while (!CheckUpToDate()) //if graph changed during generate - starting again. Trash should not go to main tile
								Generate();
						}

						#if MM_DEBUG
						Debug.Log("Completed Tile: " + coord + " isDraft:" + isDraft);
						#endif
					}
				}
			}


			public void CheckInitData ()
			{
				if (data == null) data = new TileData();
				data.area = new Area(worldPos, worldSize, resolution, 0);
				data.globals = globals;
				data.random = graph.random;
				data.isPreview = false;
				data.isDraft = false;
			}

			public void Prepare ()
			{
				graph.Prepare(data, null);
			}

			public void Generate ()
			/// Generating graph (possibly in thread)
			{
//TODO: ended that the product is not upadating when changing seed fast
//IDEA: refactor Clear, all nodes will have their Version in graph, and LastChangedVersion in data
//will force ClearChanged in tile, directly before generate
//and ClearChanged will not recieve any node

				lastStartedVersion = graph.IdsVersions(); //should assign version at the end, but it should not reflect all the changes made during generate

				//if (stop!=null && stop.stop) 
				//	return;

				//generating
				CheckInitData();
				StopToken innerStop = new StopToken();
				graph.ClearChanged(data); //hacky, cleared was not designed to work in thread, but seem to be working
				graph.Generate(data, innerStop);

				CopyProductsFromDataToAsset();

				//if (stop!=null && stop.stop) 
				//	return;

				lastChangeVersion = lastStartedVersion; //NB this doesnt guarantee it marking as up to date!

				OnClusterGenerated?.Invoke(this);
			}

			private void CopyProductsFromDataToAsset ()
			{
				HashSet<ulong> usedIds = new HashSet<ulong>(); //to remove unused products (do not clear products - it could be read)

				//copying products to asset
				foreach (IFnExit<object> exit in graph.GeneratorsOfType<IFnExit<object>>())
				{
					string name = exit.Name;

					object product = data.ReadInletProduct(exit);
					if (product == null) 
						continue;

					ulong id = exit.Id;
					ProductInfo info = new ProductInfo() {name=exit.Name, type=product.GetType(), product=product};

					if (products.ContainsKey(id))
						products[id] = info;
					else
					{
						products.Add(id, info);
						order.Add(id);
					}

					usedIds.Add(id);
				}
			}

		#endregion

		#region Graph Data

			public string[] GraphPortalNames ()
			/// Returns all graph fn outputs (not only the ones that stored)
			/// Note that since iterating graph in same order will return names in same order
			{
				List<string> names = new List<string>();
				foreach (IFnOutlet<object> portal in graph.GeneratorsOfType<IFnOutlet<object>>())
					names.Add(portal.Name);
				return names.ToArray();
			}

			public (string[],Type[]) GraphPortalNamesTypes ()
			{
				List<string> names = new List<string>();
				List<Type> types = new List<Type>();
				foreach (IFnOutlet<object> portal in graph.GeneratorsOfType<IFnOutlet<object>>())
				{
					names.Add(portal.Name);
					types.Add(Generator.GetGenericType(portal));
				}
				return (names.ToArray(), types.ToArray());
			}

		#endregion

		//TODO: cluster asset should store id-to-product dict. 
		//probably


		#region IMapMagic

			public void Refresh () 
			/// Makes current mapMagic to re-generate all
			{
				if (graph == null) return;

				ClearAll();
				
				CheckInitData();
				Prepare();
				Generate();
			}

			public void Refresh (Generator gen) => Refresh(gen, null);
			public void Refresh (Generator gen1, Generator gen2)
			/// Makes MM to reset only selected generators and generate
			/// This will not check if these generators are really contained in graph, so check it beforehand. Not a big deal though mm will generate 
			{
				if (graph == null) return;

				if (gen1!=null) Clear(gen1);
				if (gen2!=null) Clear(gen2);

				CheckInitData();
				Prepare();
//				Generate(stop);
			}

			public void Refresh (bool draft, bool main) 
			/// Makes current mapMagic to re-generate drafts or mains only
			{
				if (graph == null) return;
				StartGenerate(draft, main);
			}

			public void ClearAll ()
			{
//				StopGenerate(); //this will reset tile tasks
				data?.Clear(inSubs:true);
			}


			public void StartGenerate (bool main=true, bool draft=true)
			{
				CheckInitData();
				Prepare();
//				Generate(stop);
			}


			public bool ContainsGraph (Graph graph)
			/// Does MM has graph assigned in any way (as a root graph or biome) 
			{
				if (this.graph == null) return false;
				if (this.graph.generators == null) return false; //avoiding breaking all if something went wrong on loading
				if (this.graph == graph  ||  this.graph.ContainsSubGraph(graph, recursively:true)) return true;
				return false;
			}

			public void Clear (Generator gen)
			{
				if (data != null) 
					graph.ClearChanged(data);
			}




			public float GetProgress ()
			/// Returns minimum and maximum of the generated tiles (excluding previews), in percent 0-1
			{
				float complexity = graph.GetGenerateComplexity();

				float progress = 0;
				if (generateReady) progress = complexity;
				else 
					if (data != null)  progress = graph.GetGenerateProgress(data);

				return progress / complexity;
			}

		#endregion

		#region Serialization

			[SerializeField] private ulong[] serOrder = new ulong[0]; //aka serKeys
			[SerializeField] private string[] serNames = new string[0];
			[SerializeField] private string[] serTypes = new string[0];
			[SerializeField] private MatrixWorld[] serDictMatrices = new MatrixWorld[0];
			[SerializeField] private ObjectsPool[] serDictObjects = new ObjectsPool[0];
			[SerializeField] private SplineSys[] serDictSplines = new SplineSys[0];

			public void OnBeforeSerialize () 
			{
				int count = order.Count;
				if (serOrder.Length != count) serOrder = new ulong[count];
				if (serNames.Length != count) serNames = new string[count];
				if (serTypes.Length != count) serTypes = new string[count]; //unity is not serializing types?
				if (serDictMatrices.Length != count) serDictMatrices = new MatrixWorld[count];
				if (serDictObjects.Length != count) serDictObjects = new ObjectsPool[count];
				if (serDictSplines.Length != count) serDictSplines = new SplineSys[count];

				for (int i=0; i<count; i++)
				{
					ulong id = order[i];
					serOrder[i] = id;

					ProductInfo info = products[id];
					serNames[i] = info.name;
					serTypes[i] = info.type.AssemblyQualifiedName;

					switch (info.product)
					{
						case MatrixWorld matrix: serDictMatrices[i] = matrix; serDictObjects[i]=null; serDictSplines[i]=null; break;
						case ObjectsPool objPool: serDictObjects[i] = objPool; serDictMatrices[i]=null; serDictSplines[i]=null; break;
						case SplineSys spline: serDictSplines[i] = spline; serDictMatrices[i]=null; serDictObjects[i]=null; break;
					}
				}
			}

			public void OnAfterDeserialize ()
			{
				order.Clear(); 
				products.Clear();

				for (int i=0; i<serOrder.Length; i++)
				{
					order.Add(serOrder[i]);

					object obj = null;
					if (serDictMatrices[i] != null) obj = serDictMatrices[i];
					else if (serDictObjects[i] != null) obj = serDictObjects[i];
					else if (serDictSplines[i] != null) obj = serDictSplines[i];

					ProductInfo info = new ProductInfo() {
						name = serNames[i],
						type = Type.GetType(serTypes[i]),
						product = obj };

					products.Add(serOrder[i], info);
				}
			}

		#endregion
	}
}