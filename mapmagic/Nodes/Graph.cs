using System;

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

using Den.Tools;
using MapMagic.Products;
using MapMagic.Core; //version number
using UnityEditor;
using System.Linq;
using MapMagic.Nodes.Biomes;


namespace MapMagic.Nodes
{
	[System.Serializable]
	[HelpURL("https://gitlab.com/denispahunov/mapmagic/wikis/home")]
	[CreateAssetMenu(menuName = "MapMagic/Empty Graph", fileName = "Graph.asset", order = 101)]
	public class Graph : ScriptableObject , ISerializationCallbackReceiver
	{
		[SerializeReference] public Generator[] generators = new Generator[0];  //gens and groups serialized directly, all other via ISerCallback
		[SerializeReference] public Auxiliary[] groups = new Auxiliary[0]; //Auxiliary is group and comment. Name 'groups' for compatibility
		[NonSerialized] public Dictionary<IInlet<object>, IOutlet<object>> links = new Dictionary<IInlet<object>, IOutlet<object>>();
		[NonSerialized] public Noise random = new Noise(12345, 32768); 

		//public int serializedVersion = 0; //mapmagic version graph was last serialized to update it 
//		public int instanceId; //cached instanceId during serialization

		public static Action<Graph, TileData> OnBeforeClearPrepareGenerate; //called on any significant generate-related action
		public static Action<Generator, TileData> OnBeforeNodeCleared;
		public static Action<Generator, TileData> OnAfterNodeGenerated;
		public static Action<Type, TileData, IApplyData, StopToken> OnOutputFinalized; //TODO: rename onAfterFinalize? onBeforeApplyAssign?

		public Vector2 guiScroll;
		public int guiZoomStage = 1;
		[NonSerialized] public bool guiScrollZoomLoaded = false; //checking this from GraphWindow instead of making serialization event

		public bool guiShowDependent;
		public bool guiShowShared;
		public bool guiShowExposed;
		public bool guiShowDebug;
		public bool guiShowColors;
		public Vector2 guiMiniPos = new Vector2(20,20);
		public Vector2 guiMiniAnchor = new Vector2(0,0);

		#if MM_DEBUG
		public string debugName;
		public bool debugGenerate = true;
		public bool debugGenInfo = false;
		public bool debugGraphBackground = true;
		public float debugGraphBackColor = 0.5f;
		public bool debugGraphFps = true;
		public bool drawInSceneView = false;
		public bool saveChanges = true;
		public bool debugDrawPortalLinks = false;
		public bool debugDrawMousePosition = true;
		#endif


		public static Graph Create (Graph src=null, bool inThread=false)
		{
			Graph graph;

			if (inThread)
				graph = (Graph)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Graph));  //not stable, better create in prepare
			else 
				graph = ScriptableObject.CreateInstance<Graph>();

			//copy source graph
			if (src==null) graph.generators = new Generator[0];
			else 
			{
				graph.generators = (Generator[])Serializer.DeepCopy(src.generators);
				graph.random = src.random;
			}

			return graph;
		}

		#region Validation

			[NonSerialized] public bool validated = false;

			public void Validate (bool silent=false, bool recursive=false)
			/// Called before each graph use (but cannot be called on deserialize)
			/// At least if validated is false
			/// No priority - if want to remove links on duplication reset all ids first
			{
				assetName = name; //to log errors

				//checking if some of the serialized classes were removed
				#if UNITY_EDITOR
				if (UnityEditor.SerializationUtility.HasManagedReferencesWithMissingTypes(this)) //should be called in main thread
				{
					ManagedReferenceMissingType[] missingGens = UnityEditor.SerializationUtility.GetManagedReferencesWithMissingTypes(this);

					Dictionary<ulong,int> idToNum = new Dictionary<ulong, int>();
					for (int g = 0; g < serializedGeneratorIds300.Length; g++)
						idToNum.Add(serializedGeneratorIds300[g], g);

					for (int g=0; g<missingGens.Length; g++)
					{
						ManagedReferenceMissingType data = missingGens[g];

						ulong id = MissingGenPlug.GetMissingId(data.serializedData);
						if (!idToNum.ContainsKey(id))
							continue; //don't know why but it happened
						int num = idToNum[id];

						if (generators[num] == null)
						{
							if (!silent)
								Debug.LogWarning($"MapMagic Graph {assetName} validation error: {data.className} doesn't exist. Not installed or was removed/renamed.");

							MissingGenPlug plug = MissingGenPlug.CreatePlugFromData(data.assemblyName, data.namespaceName, data.className, data.referenceId, data.serializedData);
							generators[num] = plug;
						}
					}
				}
				#endif

				//if after this it still has nulls in generator - remove those and unlinking
				if (generators.HasNull())
				{
					if (!silent)
						Debug.LogWarning($"MapMagic Graph {assetName} validation error: it contained null generators. Removing.");

					generators = generators.RemoveNulls();
				}

				//making sure that one generator is not mentioned twice
				//TODO

				//ensuring all inlets/outlets/layers/units are iteratable (might be null array)
				HashSet<Generator> gensToRemove = null;
				foreach (Generator gen in generators)
				{
					string remove = null;

					try  { foreach (var unit in gen.AllUnits(includeSelf:false)) {} }
					catch (Exception e) { remove = "Units"; }

					try  { foreach (var unit in gen.AllInlets()) {} }
					catch (Exception e) { remove = "Inlets"; }

					try  { foreach (var unit in gen.AllOutlets()) {} }
					catch (Exception e) { remove = "Outlets"; }

					try  { 
						if (gen is IMultiLayer mgen)
							foreach (var unit in mgen.UnitLayers) {} }
					catch (Exception e) { remove = "Layers"; }

					if (remove != null)
					{
						if (!silent)
							Debug.LogWarning($"MapMagic Graph {assetName} validation error: {gen} {remove} are not iteratable. Removing node.");
					
						if (gensToRemove == null)
							gensToRemove = new HashSet<Generator>();

						gensToRemove.Add(gen);
					}
				}

				if (gensToRemove != null)
					generators = generators.Remove(gensToRemove);


				//if any of generator units is null
				gensToRemove = null;
				foreach (Generator gen in generators)
					foreach (var unit in gen.AllUnits(includeSelf:false))
					{
						if (unit == null)
						{
							if (gensToRemove == null)
								gensToRemove = new HashSet<Generator>();

							if (!silent)
								Debug.LogWarning($"MapMagic Graph {assetName} validation error: {gen} contains null inlets/outlets/layers. Removing node.");
						
							gensToRemove.Add(gen);
						}
					}

				if (gensToRemove != null)
					generators = generators.Remove(gensToRemove);
				

				//checking duplicate or zero ids
				HashSet<ulong> allUsedIds = new HashSet<ulong>();
				foreach (Generator gen in generators)
					foreach (var unit in gen.AllUnits(includeSelf:true))
					{
						ulong id = unit.Id;
						if (allUsedIds.Contains(id)  ||  unit.Id == 0)
						{
							if (!silent)
							{
								if (allUsedIds.Contains(id))
									Debug.LogWarning($"MapMagic Graph {assetName} validation error: {gen}.{unit} has duplicated id. Generating new.");
								if (unit.Id == 0)
									Debug.LogWarning($"MapMagic Graph {assetName} validation error: {gen}.{unit} has no id assigned. Generating new.");
							}

							id = Id.Generate();
							while (allUsedIds.Contains(id))
								id = Id.Generate();

							unit.Id = id;
						}
						allUsedIds.Add(id);
					}

				//fixing assigned gens
				foreach (Generator gen in generators)
					foreach (var unit in gen.AllUnits(includeSelf:false))
						if (unit.Gen == null)
						{
							if (!silent)
								Debug.LogWarning($"MapMagic Graph {assetName} validation error: {gen}.{unit} has no generator assigned. Fixing.");
							
							unit.SetGen(gen);
						}

				//removing links that lead to not used ids
				HashSet<IInlet<object>> linksToRemove = new HashSet<IInlet<object>>();
				foreach (var kvp in links)
					if (!allUsedIds.Contains(kvp.Key.Id)   ||  
						!allUsedIds.Contains(kvp.Value.Id)  ||  
						!allUsedIds.Contains(kvp.Key.Gen.Id)  || //checking generator ids too
						!allUsedIds.Contains(kvp.Value.Gen.Id) )
					{
						if (!silent)
							Debug.LogWarning($"MapMagic Graph {assetName} validation error: link {kvp.Key.Gen}.{kvp.Key} - {kvp.Value.Gen}.{kvp.Value} leads to non-existing id. Unlinking.");

						linksToRemove.Add(kvp.Key);
					}

				foreach (IInlet<object> link in linksToRemove)
					links.Remove(link);

				//circular dependency
				//TODO

				//sub-graph next
				if (recursive)
					foreach (IBiome biome in UnitsOfType<IBiome>())
					{
						if (biome.SubGraph != null)
							biome.SubGraph.Validate(silent);
					}

				validated = true;
			}

		#endregion


		#region Node Operations

		public void Add (Generator gen)
			{
				if (ArrayTools.Contains(generators,gen))
						throw new Exception("Could not add generator " + gen + " since it is already in graph");

				//gen.id = NewGenId; //id is assigned on create

				ArrayTools.Add(ref generators, gen);
				//cachedGuidLut = null;
			}


			public void Add (Auxiliary grp)
			{
				if (ArrayTools.Contains(groups, grp))
						throw new Exception("Could not add group " + grp + " since it is already in graph");

				ArrayTools.Add(ref groups, grp);
				//cachedGuidLut = null;
			}


			public void Add (Group grp)
			{
				if (ArrayTools.Contains(groups, grp))
						throw new Exception("Could not add group " + grp + " since it is already in graph");

				ArrayTools.Add(ref groups, grp);
				//cachedGuidLut = null;
			}


			public void Remove (Generator gen)
			{
				if (!ArrayTools.Contains(generators,gen))
					throw new Exception("Could not remove generator " + gen + " since it is not in graph");

				UnlinkGenerator(gen);

				ArrayTools.Remove(ref generators, gen);
				//cachedGuidLut = null;
			}


			public void Remove (Auxiliary aux, bool withContents=false)
			{
				if (!ArrayTools.Contains(groups, aux))
					throw new Exception("Could not remove group " + aux + " since it is not in graph");

				if (withContents  &&  aux is Group grp)
				{
					List<Generator> containedGens = GetGroupGenerators(grp);
					for (int i=0; i<containedGens.Count; i++)
						Remove(containedGens[i]);

					List<Auxiliary> containedGroups = GetGroupAux(grp);
					for (int i=0; i<containedGroups.Count; i++)
						Remove(containedGroups[i]);
				}

				ArrayTools.Remove(ref groups, aux);
				//cachedGuidLut = null;
			}


			public void Remove (Layer layer)
			///won't actually remove it from array, but will unlink and re-list from expose
			{
				if (layer is IOutlet<object> outlet) UnlinkOutlet(outlet);
				if (layer is IInlet<object> inlet) UnlinkInlet(inlet);
			}

			
			public (Generator[],Auxiliary[]) Import (Graph other, bool createOverride=false)
			/// Returns the array of imported grGens (they are copy of the ones in souce graph)
			{
				Graph copied = SerializedCopy(other);

				//gens
				ArrayTools.AddRange(ref generators, copied.generators);

				//links
				foreach (var kvp in copied.links)
					links.Add(kvp.Key, kvp.Value);

				//groups
				ArrayTools.AddRange(ref groups, copied.groups);
				
				//ids
				Dictionary<ulong,ulong> replacedIds = CheckFixIds( new HashSet<IUnit>(copied.generators) ); 

				return (copied.generators, copied.groups);
			}


			public Graph Export (HashSet<Generator> gensHash, ICollection<Auxiliary> groupsHash=null)
			{
				Graph exported = ScriptableObject.CreateInstance<Graph>();

				//gens
				exported.generators = gensHash.ToArray();

				//links
				foreach (var kvp in links)
				{
					if (gensHash.Contains(kvp.Key.Gen) && gensHash.Contains(kvp.Value.Gen))
						exported.links.Add(kvp.Key, kvp.Value);
				}

				//groups
				if (groupsHash != null)
					exported.groups = groupsHash.ToArray();

				//copy to duplicate grGens and links
				Graph copied = SerializedCopy(exported);

				return copied;
			}


			public Graph Export (Group grp)
			{
				HashSet<Generator> gens = new HashSet<Generator>();
				gens.AddRange(GetGroupGenerators(grp).ToArray());

				List<Auxiliary> grps = GetGroupAux(grp);
				grps.Insert(0,grp); //inserting itself

				return Export(gens, grps);
			}


			public Generator[] Duplicate (HashSet<Generator> gens)
			/// Returns the list of duplicated grGens
			{
				Graph exported = Export(gens);
				Generator[] expGens = exported.generators;
				Import(exported);

				return expGens;
			}


			public static void Reposition (IList<Generator> gens, IList<Auxiliary> groups, Vector2 newCenter)
			/// Moves array of grGens center to new position
			{
				Vector2 currCenter = Vector2.zero;
				foreach (Generator gen in gens)
					currCenter += new Vector2(gen.guiPosition.x + gen.guiSize.x/2, gen.guiPosition.y);

				currCenter /= gens.Count;

				Vector2 delta = newCenter - currCenter;

				foreach (Generator gen in gens)
					gen.guiPosition += delta;

				foreach (Auxiliary group in groups)
					group.guiPos += delta;
			}


			public List<Generator> GetGroupGenerators (Group group)
			/// Removes dragged-off gens and adds new ones
			{
				List<Generator> grGens = new List<Generator>();
				Rect rect = new Rect(group.guiPos, group.guiSize);
				for (int g=0; g<generators.Length; g++)
				{
					if (rect.Contains(generators[g].guiPosition, generators[g].guiSize)) 
						grGens.Add(generators[g]);
				}

				return grGens;
			}

			public List<Auxiliary> GetGroupAux (Group group)
			/// Sub-groups and comments inside group
			{
				List<Auxiliary> grAux = new List<Auxiliary>();
				Rect rect = new Rect(group.guiPos, group.guiSize);
				for (int g=0; g<groups.Length; g++)
				{
					if (!rect.Contains(groups[g].guiPos, groups[g].guiSize)) continue;
					grAux.Add(groups[g]);
				}

				return grAux;
			}


			public Dictionary<ulong,ulong> CheckFixIds (HashSet<IUnit> susGens=null)
			// Ensures that ids of grGens and layers are never match
			// Will try to change ids in susGens if any (leaving othrs intact)
			// Returns approx dict of what ids were replaced with what. Duplicated ids and 0 are not included in list
			{
				Dictionary<ulong,IUnit> allIds = new Dictionary<ulong,IUnit>(); //not hash set but dict to skip generator if it's his id
				Dictionary<ulong,ulong> oldNewIds = new Dictionary<ulong,ulong>();
				List<ulong> zeroNewIds = new List<ulong>();

				CheckFixIdsRecursively(susGens, allIds, oldNewIds, zeroNewIds);

				if (oldNewIds.Count != 0  )
					Debug.Log($"Changed generators ids on serialize: {oldNewIds.Count + zeroNewIds.Count}");

				return oldNewIds;
			}

			private void CheckFixIdsRecursively (HashSet<IUnit> susGens, Dictionary<ulong,IUnit> allIds, Dictionary<ulong,ulong> oldNewIds, List<ulong> zeroNewIds)
			//populates a dict of renamed ids
			//those ids that were renamed from 0 are added to zeroNewIds
			{
				if (generators == null)
					OnAfterDeserialize(); //in some cases got to de-serialize sub-graphs

				//not-suspicious grGens first
				foreach (IUnit unit in AllUnits())
				{
					if (susGens != null  &&  susGens.Contains(unit))
						continue;

					if (allIds.TryGetValue(unit.Id, out IUnit contUnit)  || unit.Id == 0)
					{
						if (contUnit == unit) //is itself
							continue;

						ulong newId = Id.Generate();
						while (allIds.ContainsKey(newId))
							newId = Id.Generate();
						
						if (unit.Id != 0)
							oldNewIds.Add(unit.Id, newId);
						else
							zeroNewIds.Add(newId);

						unit.Id = newId;
					}

					allIds.Add(unit.Id, unit);
				}

				//sub-graph next
				foreach (IBiome biome in UnitsOfType<IBiome>())
				{
					if (biome.SubGraph != null)
						biome.SubGraph.CheckFixIdsRecursively(susGens, allIds, oldNewIds, zeroNewIds);
				}

				//suspicious generator last (after all other grGens are marked and won't change)
				if (susGens != null)
					foreach (IUnit unit in AllUnits())
					{
						if (!susGens.Contains(unit))
							continue;

						if (allIds.TryGetValue(unit.Id, out IUnit contUnit) || unit.Id == 0)
						{
							if (contUnit == unit) //is itself
								continue;

							ulong newId = Id.Generate();
							while (allIds.ContainsKey(newId))
								newId = Id.Generate();
						
							if (unit.Id != 0)
								oldNewIds.Add(unit.Id, newId);
							else
								zeroNewIds.Add(newId);

							unit.Id = newId;
						}

						allIds.Add(unit.Id, unit);
					}
			}

		#endregion


		#region Linking

			public void Link (IOutlet<object> outlet, IInlet<object> inlet)
			{
				//unlinking
				if (outlet == null  &&  links.ContainsKey(inlet))
					links.Remove(inlet);

				//linking
				else //if (CheckLinkValidity(outlet, inlet)) 
				{
					if (links.ContainsKey(inlet)) links[inlet] = outlet;
					else links.Add(inlet, outlet);
				}

				inlet.Gen.version++;
			}

			public bool CheckLinkValidity (IOutlet<object> outlet, IInlet<object> inlet)
			{
				if (Generator.GetGenericType(outlet) != Generator.GetGenericType(inlet))
					return false;

				if (AreDependent(inlet.Gen, outlet.Gen)) //in this order
					return false;

				return true;
			}

			public bool AreDependent (Generator startGen, Generator endGen)
			{
				HashSet<Generator> notDependent = new HashSet<Generator>();
				return AreDependent(startGen, endGen, notDependent);
			}

			private bool AreDependent (Generator startGen, Generator nextGen, HashSet<Generator> notDependent)
			/// startGen is always the same, nextGen changes recursively
			{
				foreach (Generator preGen in Predecessors(nextGen))
				{
					if (preGen == startGen)
						return true;

					if (notDependent.Contains(preGen))
						return false;

					bool dependent = AreDependent(startGen, preGen, notDependent);

					if (!dependent)
						notDependent.Add(preGen);

					else //if dependent
						return true;
				}

				return false;
			}


			public Dictionary<IOutlet<object>, HashSet<IInlet<object>>> InverseLinks ()
			/// Generates a dict of inverse links - or should I say FORWARD dependency
			/// Not used but keeping it just in case
			{
				Dictionary<IOutlet<object>, HashSet<IInlet<object>>> inverseLinks = new Dictionary<IOutlet<object>, HashSet<IInlet<object>>>();

				using (ProfilerExt.Profile("Populating Inverse Links"))
					foreach (var link in links)
					{
						IOutlet<object> outlet = link.Value;
						IInlet<object> inlet = link.Key;

						if (!inverseLinks.TryGetValue(outlet, out HashSet<IInlet<object>> inletsList))
						{
							inletsList = new HashSet<IInlet<object>>();
							inverseLinks.Add(outlet, inletsList);
						}

						if (!inletsList.Contains(inlet))
							inletsList.Add(inlet);
					}

				return inverseLinks;
			}


			public bool IsLinked (IInlet<object> inlet) => links.ContainsKey(inlet);
			/// Is this inlet linked to anything


			public IOutlet<object> LinkedOutlet (IInlet<object> inlet) => links.TryGetValue(inlet, out IOutlet<object> outlet) ? outlet : null;
			/// Simply gets inlet's link

			private List<IInlet<object>> LinkedInlets (IOutlet<object> outlet)
			/// Isn't fast, so using for internal purpose only. For other cases use cachedBackwardsLinks
			{
				List<IInlet<object>> linkedInlets = new List<IInlet<object>>();
				
				foreach (var kvp in links)
					if (kvp.Value == outlet)
						linkedInlets.Add(kvp.Key);

				return linkedInlets;
			}


			public void UnlinkInlet (IInlet<object> inlet)
			{
				if (links.ContainsKey(inlet))
					links.Remove(inlet);
				
				inlet.Gen.version++;
			}


			public void UnlinkOutlet (IOutlet<object> outlet)
			/// Removes any links to this outlet
			{
				List<IInlet<object>> linkedInlets = new List<IInlet<object>>();

				foreach (IInlet<object> inlet in linkedInlets)
				{
					links.Remove(inlet);
					inlet.Gen.version++;
				}
			}


			public void UnlinkGenerator (Generator gen)
			/// Removes all links from and to this generator
			{
				List<IInlet<object>> genLinks = new List<IInlet<object>>(); //both that connected to this gen inlets and outlets

				foreach (var kvp in links)
				{
					IInlet<object> inlet = kvp.Key;
					IOutlet<object> outlet = kvp.Value;

					//unlinking this from others (not needed on remove, but Unlink could be called not only on remove)
					if (inlet.Gen == gen)
					{
						genLinks.Add(inlet);
						outlet.Gen.version++;
					}

					//unlinking others from this
					if (outlet.Gen == gen)
					{
						genLinks.Add(inlet);
						inlet.Gen.version++;
					}
				}

				foreach (IInlet<object> inlet in genLinks)
					links.Remove(inlet);

				gen.version++;
			}


			public void ThroughLink (Generator gen)
			/// Connects previous gen outlet with next gen inlet maintaining link before removing this gen
			/// This will not unlink generator completely - other inlets may remain
			{
				//choosing the proper inlets and outlets for re-link
				IInlet<object> inlet = null;
				IOutlet<object> outlet = null;

				if (gen is IInlet<object>  &&  gen is IOutlet<object>)
					{ inlet = (IInlet<object>)gen; outlet = (IOutlet<object>)gen; }

				if (gen is IMultiInlet multInGen  &&  gen is IOutlet<object> outletGen)
				{
					Type genericType = Generator.GetGenericType(gen);
					foreach (IInlet<object> genInlet in multInGen.Inlets())
					{
						if (!IsLinked(genInlet)) continue;
						if (Generator.GetGenericType(genInlet) == genericType) inlet = genInlet; //the first inlet of gen mode
					}
				}

				if (gen is IInlet<object> inletGen  &&  gen is IMultiOutlet multOutGen)
				{
					Type genericType = Generator.GetGenericType(gen);
					foreach (IOutlet<object> genOutlet in multOutGen.Outlets())
					{
						if (Generator.GetGenericType(genOutlet) == genericType) outlet = genOutlet; //the first outlet of gen mode
					}
				}

				if (inlet == null || outlet == null) return;
					
				// re-linking
				List<IInlet<object>> linkedInlets = LinkedInlets(outlet); //other generator's inlet connected to this gen
				if (linkedInlets.Count == 0)
					return;
				
				IOutlet<object> linkedOutlet;
				if (!links.TryGetValue(inlet, out linkedOutlet))
					return;
				
				foreach (IInlet<object> linkedInlet in linkedInlets)
					Link(linkedOutlet, linkedInlet);
			}


			public void AutoLink (Generator gen, IOutlet<object> outlet)
			/// Links with first gen's inlet of the same mode as outlet
			{
				Type outletType = Generator.GetGenericType(outlet);

				if (gen is IInlet<object> inletGen)
				{
					if (Generator.GetGenericType(inletGen) == outletType)
						Link(outlet, inletGen);
				}

				else if (gen is IMultiInlet multInGen)
				{
					foreach (IInlet<object> inlet in multInGen.Inlets())
						if (Generator.GetGenericType(inlet) == outletType)
							{ Link(outlet,inlet); break; }
				}
			}


			public void ReplaceGen (Generator gen, Generator newGen)
			/// switches one generatgor with another, and re-links them if possible
			/// removes impossible links
			{
				int index = generators.Find(gen);
				if (index < 0)
					throw new Exception("Generator " + gen + " not found in graph " + assetName);

				//replacing inlets/outlets in links if they have same id
				foreach (IInlet<object> inletGen in gen.AllInlets())
					foreach (IInlet<object> inletNewGen in newGen.AllInlets())
						if (inletGen.Id == inletNewGen.Id)
						{
							//removing old inlet key and adding new one
							if (links.ContainsKey(inletGen))
							{
								IOutlet<object> outlet = links[inletGen];
								links.Remove(inletGen);
								links.Add(inletNewGen, outlet);
							}
						}

				foreach (IOutlet<object> outletGen in gen.AllOutlets())
					foreach (IOutlet<object> outletNewGen in newGen.AllOutlets())
						if (outletGen.Id == outletNewGen.Id)
						{
							//replacing outlet value
							List<IInlet<object>> inlets = new List<IInlet<object>>();
							foreach (var kvp in links)
								if (outletGen == kvp.Value)
									inlets.Add(kvp.Key);

							foreach (var inlet in inlets)
								links[inlet] = outletNewGen;
						}

				//unlinking remaining links anyways
				//UnlinkGenerator(gen);

				generators[index] = newGen;
			}

		#endregion


		#region Iterating Nodes

			// Not iteration nodes in subGraphs
			// Using recursive fn calls instead (with Graph in SubGraphs)

			public IEnumerable<Generator> GetGenerators (Predicate<Generator> predicate)
			/// Iterates in all grGens that match predicate condition
			{
				int i = -1;
				for (int g=0; g<generators.Length; g++)
				{
					i = Array.FindIndex(generators, i+1, predicate);
					if (i>=0) yield return generators[i];
					else break;
				}
			}


			public Generator GetGenerator (Predicate<Generator> predicate)
			/// Finds first generator that matches condition
			/// Returns null if nothing found (no need to use TryGet)
			{
				int i = Array.FindIndex(generators, predicate);
				if (i>=0) return generators[i]; 
				else return null;
			}

			public Generator GetGeneratorById (ulong id)
			/// Finds first generator with given id
			{
				//TODO: generate id-to-gen cache on first run
				for (int g=0; g<generators.Length; g++)
				{
					if (generators[g].id == id)
						return generators[g];
				}
				return null;
			}

			public IEnumerable<T> GeneratorsOfType<T> ()
			/// Iterates all grGens of given mode
			{
				for (int g=0; g<generators.Length; g++)
				{
					if (generators[g] is T tGen)
						yield return tGen;
				}
			}


			public int GeneratorsCount (Predicate<Generator> predicate)
			/// Finds the number of grGens that match given condition
			{
				int count = 0;

				int i = -1;
				for (int g=0; g<generators.Length; g++)
				{
					i = Array.FindIndex(generators, i+1, predicate);
					if (i>=0) count++;
				}

				return count;
			}


			public bool ContainsGenerator (Generator gen) => GetGenerator(g => g==gen) != null;

			public bool ContainsGeneratorOfType<T> () => GetGenerator(g => g is T) != null;


			public int GeneratorsCount<T> () where T: class
			{
				bool findByType (Generator g) => g is T;
				return GeneratorsCount(findByType);
			}


			public IEnumerable<Generator> Predecessors (Generator gen) 
			/// Returns first-level nodes this generator depends on
			/// "Parents" is not the correct word
			/// TODO: use AllInlets instead. ICustomDependence should be removed
			{
				foreach (IInlet<object> minlet in gen.AllInlets())
					if (links.TryGetValue(minlet, out IOutlet<object> moutlet))
						yield return moutlet.Gen;

				if (gen is ICustomDependence cusDepGen)
					foreach (Generator priorGen in cusDepGen.PriorGens())
						yield return priorGen;
			}


			public IEnumerable<Generator> AllPredecessors (Generator gen, bool includeSelf=false) 
			{
				if (includeSelf)
					yield return gen;

				foreach (IInlet<object> minlet in gen.AllInlets())
					if (links.TryGetValue(minlet, out IOutlet<object> moutlet))
					{
						foreach (Generator pre in AllPredecessors(moutlet.Gen))
							yield return pre;
							
						yield return moutlet.Gen;
					}
			}


			public IEnumerable<IInlet<object>> AllPredecessorLinks (Generator gen)
		{
				foreach (Generator pre in AllPredecessors(gen, includeSelf:true))
					foreach (IInlet<object> minlet in pre.AllInlets())
						yield return (minlet);
			}


			public IEnumerable<IUnit> AllUnits ()
			/// Iterates all grGens and layers in graph. May iterate twice if layer is input and output
			{
				foreach (Generator gen in generators)
					foreach (IUnit unit in gen.AllUnits())
						yield return unit;
			}


			public IEnumerable<T> UnitsOfType<T> ()
			/// Iterates all grGens and layers in graph. May iterate twice if layer is input and output
			{
				foreach (Generator gen in generators)
				{
					if (gen is T tgen)
						yield return tgen;

					if (gen is IMultiLayer multGen)
						foreach (IUnit layer in multGen.UnitLayers)
							if (layer is T tlayer)
								yield return tlayer;

					if (gen is IMultiInlet inlGen)
						foreach (IInlet<object> layer in inlGen.Inlets())
							if (layer is T tlayer)
								yield return tlayer;

					if (gen is IMultiOutlet outGen)
						foreach (IOutlet<object> layer in outGen.Outlets())
							if (layer is T tlayer)
								yield return tlayer;
				}
			}


			public ulong IdsVersions ()
			/// Practically unique identifier that describes this graph and it's current state.
			/// Used to update functions and clusters only when graph changed
			/// Summary of all generator ids + versions + seed. 
			{
				ulong ids = 0;
				ulong versions = 0;
				foreach (Generator gen in generators)
				{
					ids += gen.id;
					versions += gen.version;

					if (gen is IMultiLayer multGen)
						foreach (IUnit layer in multGen.UnitLayers)
							ids += layer.Id;
				}

				//return (ulong)(random.Seed<<24) + ids + versions;
				return (ulong)(ids<<16) + versions;
			}


			public string IdsVersionsHash ()
			{
				ulong idsVers = IdsVersions();
				int hashCode = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(idsVers);
				return Convert.ToBase64String( BitConverter.GetBytes(idsVers) );
			}


			public IEnumerable<Graph> SubGraphs (bool recursively=false)
			/// Enumerates in all child graphs recursively
			{
				foreach (IBiome biome in UnitsOfType<IBiome>())
				{
					Graph subGraph = biome.SubGraph;
					if (subGraph == null) continue;

					yield return biome.SubGraph;

					if (recursively)
						foreach (Graph subSubGraph in subGraph.SubGraphs(recursively:true))
							yield return subSubGraph;
				}
			}


			public bool ContainsSubGraph (Graph subGraph, bool recursively=false)
			{
				foreach (IBiome biome in UnitsOfType<IBiome>())
				{
					Graph biomeSubGraph = biome.SubGraph;
					if (biomeSubGraph == null) continue;
					if (biomeSubGraph == subGraph) return true;
					if (recursively && biomeSubGraph.ContainsSubGraph(subGraph, recursively:true)) return true;
				}
				
				return false;
			}


			public IEnumerable<Generator> RelevantGenerators (bool isDraft)
			/// All nodes that end chains, have previes, etc - all that should be generated
			{
				for (int g=0; g<generators.Length; g++)
					if (IsRelevant(generators[g], isDraft))
						yield return generators[g];
			}


			public IEnumerable<IInlet<object>> Inlets ()
			/// All inlets in graph, no matter if they are linked or not
			{
				for (int g=0; g<generators.Length; g++)
				{
					/*if (generators[g] is IInlet<object> inlet)
						yield return inlet;

					if (generators[g] is IMultiInlet inletGen)
						foreach(IInlet<object> i in inletGen.Inlets())
							yield return i;*/

					foreach(IInlet<object> i in generators[g].AllInlets())
							yield return i;

					//TODO: use AllInlets everywhere! 
				}
			}


			public IEnumerable<IOutlet<object>> Outlets ()
			/// All outlets in graph, no matter if they are linked or not
			{
				for (int g=0; g<generators.Length; g++)
				{
					if (generators[g] is IOutlet<object> outlet)
						yield return outlet;

					if (generators[g] is IMultiOutlet outletGen)
						foreach(IOutlet<object> o in outletGen.Outlets())
							yield return o;
				}
			}


			public bool IsRelevant (Generator gen, bool isDraft)
			{
				//TODO: outputs are relevant only when they are at last stage actually applying. Not inside cluster.

				if (gen is OutputGenerator outGen)
				{
					if (isDraft  &&  outGen.OutputLevel.HasFlag(OutputLevel.Draft)) return true;
					if (!isDraft  && outGen.OutputLevel.HasFlag(OutputLevel.Main)) return true;
				}

				else if (gen is IRelevant)
					return true;

				else if (gen is IBiome biomeGen && biomeGen.SubGraph!=null)
					return true;

				else if (gen is IMultiLayer multiLayerGen)
				{
					foreach (IUnit layer in multiLayerGen.UnitLayers)
						if (layer is IBiome)
							return true;
				}

				else if (gen.guiPreview)
					return true;

				return false;
			}

		#endregion


		#region Generate

			//And all the stuff that takes data into account

			public bool ClearChanged (TileData data, bool totalRebuild=false)
			/// Removes ready state if any of prev gens is not ready
			/// Clears all relevants if prior generator is clear
			{
				OnBeforeClearPrepareGenerate?.Invoke(this, data);

				RefreshInputHashIds();

				//clearing nodes that are not in graph
				data.ClearStray(this);

				Dictionary<Generator,bool> processed = new Dictionary<Generator,bool>();
				//using processed instead of ready since non-ready grGens should be cleared too - they might have NonReady-ReadyOutdated-NonReady chanis

				//clearing graph nodes
				bool allReady = true;
				foreach (Generator relGen in RelevantGenerators(data.isDraft))
					allReady = allReady & ClearChangedRecursive(relGen, data, processed, totalRebuild);
				
				return allReady;
			}


			private bool ClearChangedRecursive (Generator gen, TileData data, Dictionary<Generator,bool> processed, bool totalRebuild=false)
			/// Removes ready state if any of prev gens is not ready, per-gen
			/// Will iterate at least once all the nodes, even if they were not changed
			{
				if (processed.TryGetValue(gen, out bool processedReady))
					return processedReady;

				bool ready = data.IsReady(gen);
				//don't return if not ready, got to check inlet chains and biomes subs

				if (gen is IInlet<object> inletGen)  
				{
					if (links.TryGetValue(inletGen, out IOutlet<object> precedingOutlet))
					{
						Generator precedingGen = precedingOutlet.Gen;

						bool inletReady; //loading from lut or clearing recursive
						if (!processed.TryGetValue(gen, out inletReady))
							inletReady = ClearChangedRecursive(precedingGen, data, processed, totalRebuild);

						ready = ready && inletReady;
					}
				}

				if (gen is IMultiInlet multInGen)
					foreach (IInlet<object> inlet in multInGen.Inlets())
					{
						//the same as inletGen
						if (links.TryGetValue(inlet, out IOutlet<object> precedingOutlet))
						{
							Generator precedingGen = precedingOutlet.Gen;

							bool inletReady; //loading from lut or clearing recursive
							if (!processed.TryGetValue(gen, out inletReady))
								inletReady = ClearChangedRecursive(precedingGen, data, processed, totalRebuild);

							ready = ready && inletReady;
							//no break, need to check-clear other layers chains
						}
					}

				if (gen is ICustomClear cgen)
				{
					cgen.OnClearing(this, data, ref ready, totalRebuild);
					//cgen.ClearRecursive(data);
					//ready = data.IsReady(gen);
				}	
				
				if (!ready) data.ClearReady(gen); 
				processed.Add(gen, ready);
				return ready;
			}


			public void Prepare (TileData data, Terrain terrain)
			/// Executed in main thread to perform terrain reads or something
			/// Not recursive just for performance reasons. Prepares only on-ready grGens
			{
				OnBeforeClearPrepareGenerate?.Invoke(this, data);

				foreach (Generator gen in generators)
				{
					if (!(gen is IPrepare prepGen)) continue;
					if (data.IsReady(gen)) continue;

					//applying override (duplicating gen if needed)
					Generator cGen = gen;
					//if (Assigner.IsExposed(gen, exposed))
					//	cGen = (Generator)Assigner.CopyAndAssign(gen, exposed, ovd);

					//preparing
					((IPrepare)cGen).Prepare(data, terrain);
				}
			}


			public void PrepareRecursive (Generator gen, TileData data, Terrain terrain, HashSet<Generator> processed)
			{
				if (processed.Contains(gen)) //already prepared
					return;

				foreach (IInlet<object> inlet in gen.AllInlets())
					if (links.TryGetValue(inlet, out IOutlet<object> outlet))
						PrepareRecursive(outlet.Gen, data, terrain, processed);

				if (gen is ICustomDependence customDepGen)
				{
					foreach (Generator priorGen in customDepGen.PriorGens())
						PrepareRecursive(priorGen, data, terrain, processed);
				}

				//not excluding disabled: they should generate the chain before them (including outputs like Textures)
			}


			public void Generate (TileData data, StopToken stop=null)
			{
				OnBeforeClearPrepareGenerate?.Invoke(this, data);

				#if MM_DEBUG
				string cs = data.area != null ? data.area.Coord.ToString() : "null";
				Log("Generate Started", data, stop);
				#endif

				//refreshing link ids lut (only for top level graph)
				RefreshInputHashIds();

				#if MM_DEBUG
				if (!debugGenerate) return;
				#endif

				//main generate pass - all changed gens recursively
				foreach (Generator relGen in RelevantGenerators(data.isDraft))
				{
					if (stop!=null && stop.stop) 
					{
						#if MM_DEBUG
						Log("Generate StopExit", data, stop);
						#endif

						return;
					}

					GenerateRecursive(relGen, data, stop:stop); //will not generate if it has not changed
				}

				#if MM_DEBUG
				Log("Generate Complete", data, stop);
				#endif
			}


			public void GenerateRecursive (Generator gen, TileData data, StopToken stop=null)
			{
				if (stop!=null && stop.stop) return;
				if (data.IsReady(gen)) return;
				ulong startedVersion = gen.version;

				//generating inlets recursively
				foreach (IInlet<object> inlet in gen.AllInlets())
					if (links.TryGetValue(inlet, out IOutlet<object> outlet))
						GenerateRecursive(outlet.Gen, data, stop:stop);

				//checking for generating twice
				if (stop!=null && stop.stop) return;
				if (data.IsReady(gen))  
					throw new Exception($"Generating twice {gen}, id: {Id.ToString(gen.id)}, draft: {data.isDraft}, stop: {(stop!=null ? stop.stop.ToString() : "null")}");

				//before-generated event
				//if (gen is ICustomGenerate customGen)
				//	customGen.OnBeforeGenerated(this, data, stop);

				//checking if all layers Gen and Id assigned (not necessary, remove)
				if (gen is IMultiLayer layerGen)
					foreach (IUnit layer in layerGen.UnitLayers)
					{
						if (layer.Gen == null) layer.SetGen(gen);
						if (layer.Id == 0) layer.Id = Id.Generate();
					}

				//main generate fn
				long startTime = System.Diagnostics.Stopwatch.GetTimestamp();
				if (stop!=null && stop.stop) return;

				//setting inlet fields (duplicating gen if needed - made for functions)
				Generator cGen = gen;
				if (gen.fieldInlets.HasFieldInlets)
				{
					//cGen = (Generator)Assigner.CopyAndAssign(gen, exposed, ovd);
					IUnit copy = (Generator)gen.ShallowCopy();
					cGen.fieldInlets.ReadFieldInlets(gen, data, stop);
				}

				//generating
				cGen.Generate(data, stop);

				//debug data
				#if MM_DEBUG
				long deltaTime = System.Diagnostics.Stopwatch.GetTimestamp() - startTime;
				if (data.isDraft) gen.draftTime = 1000.0 * deltaTime / System.Diagnostics.Stopwatch.Frequency;
				else gen.mainTime = 1000.0 * deltaTime / System.Diagnostics.Stopwatch.Frequency;
				#endif

				//marking ready 
				if (stop!=null && stop.stop) return;
				//if (gen.version==startedVersion || data.isDraft)
				//if it's still relevant (or draft - drafts allowed to be partly wrong) - theoretically it should be so, but MM worked all the way without it
				data.MarkReady(gen);

				OnAfterNodeGenerated?.Invoke(gen, data);
			}


			public void Finalize (TileData data, StopToken stop=null)
			{
				#if MM_DEBUG
				Log("Graph.Finalize Starting", data, stop);
				#endif

				if (stop!=null && stop.stop) 
				{ 
					data.ClearFinalize(); //no need to leave finalize for further generate

					#if MM_DEBUG
					Log("Graph.Finalize Exit", data, stop);
					#endif

					return; 
				} 
				
				while (data.FinalizeMarksCount > 0)
				{
					FinalizeAction action = data.DequeueFinalize();
					//data returns finalize actions with priorities (height first)

					//if (action == MatrixGenerators.HeightOutput200.finalizeAction)
					//{
					//	data.MarkFinalize(ObjectsGenerators.ObjectsOutput.finalizeAction, stop);
					//	data.MarkFinalize(ObjectsGenerators.TreesOutput.finalizeAction, stop);
					//}
					//can't use ObjectsGenerators since they are in the other module. Using OnOutputFinalized event instead.

					if (stop!=null && stop.stop) 
					{ 
						data.ClearFinalize(); 

						#if MM_DEBUG
						Log("Graph.Finalize Stopped", data, stop);
						#endif

						return; 
					}
					action(data, stop);
				}

				#if MM_DEBUG
				if (data.heights != null)
					Log("Graph.Finalize Complete", data, stop);
				#endif
			}


			[Obsolete] private IEnumerator Apply (TileData data, Terrain terrain, StopToken stop=null)
			/// Actually not a graph function. Here for the template. Not used.
			{
				if (stop!=null && stop.stop) yield break;
				while (data.ApplyMarksCount != 0)
				{
					IApplyData apply = data.DequeueApply(); //this will remove apply from the list
					
					//applying routine
					if (apply is IApplyDataRoutine)
					{
						IEnumerator e = ((IApplyDataRoutine)apply).ApplyRoutine(terrain);
						while (e.MoveNext()) 
						{
							if (stop!=null && stop.stop) yield break;
							yield return null;
						}
					}

					//applying at once
					else
					{
						apply.Apply(terrain);
						yield return null;
					}
				}

				
				#if UNITY_EDITOR
				if (data.isPreview)
					UnityEditor.EditorWindow.GetWindow<UnityEditor.EditorWindow>("MapMagic Graph");
				#endif

				//OnGenerateComplete.Raise(data);
			}


			public void Purge (Type type, TileData data, Terrain terrain)
			/// Purges the results of all output grGens of mode
			{
				for (int g=0; g<generators.Length; g++)
				{
					if (!(generators[g] is OutputGenerator outGen)) continue;

					Type genType = outGen.GetType();
					if (genType==type  ||  type.IsAssignableFrom(genType))
						outGen.ClearApplied(data, terrain);
				}

				foreach (Graph subGraph in SubGraphs())
					subGraph.Purge(type, data, terrain);
			}


			private void RefreshInputHashIds ()
			/// For each inlet in graph writes the id of linked outlet
			{
				//foreach (IUnit unit in AllUnits()) if (unit is IInlet<object> inlet)  inlet.LinkedOutletId = 0;  
				//do not set LinkedOutletId beforehand - it might be generating, and some generator will read this id. Caused skipped tiles issue. Better do it after

				foreach (var kvp in links)
				{
					IInlet<object> inlet = kvp.Key;
					IOutlet<object> outlet = kvp.Value;
					inlet.LinkedOutletId = outlet.Id;
					inlet.LinkedGenId = outlet.Gen.id;
				}

				foreach (IUnit unit in AllUnits()) 
					if (unit is IInlet<object> inlet  &&  !links.ContainsKey(inlet))
						inlet.LinkedOutletId = 0; 
			}


			public static IEnumerable<(Graph,TileData)> AllGraphsDatas (Graph rootGraph, TileData rootData, bool includeSelf=false)
			{
				if (includeSelf)
					yield return (rootGraph, rootData);

				foreach (IBiome biome in rootGraph.UnitsOfType<IBiome>()) //top level first
				{
					Graph subGraph = biome.SubGraph;
					if (subGraph == null)
						continue;

					TileData subData = biome.SubData(rootData);
					if (subData == null)
						continue;

					foreach ((Graph,TileData) subSub in AllGraphsDatas(subGraph, subData))
						yield return subSub;
				}
			}

		#endregion


		#region Complexity/Progress

			public float GetGenerateComplexity ()
			/// Gets the total complexity of the graph (including biomes) to evaluate the generate progress
			{
				float complexity = 0;

				for (int g=0; g<generators.Length; g++)
				{
					if (generators[g] is ICustomComplexity)
						complexity += ((ICustomComplexity)generators[g]).Complexity;

					else
						complexity ++;
				}

				return complexity;
			}


			public float GetGenerateProgress (TileData data)
			/// The summary complexity of the nodes Complete (ready) in data (shows only the graph nodes)
			/// No need to combine with GetComplexity since these methods are called separately
			{
				float complete = 0;

				//generate
				for (int g=0; g<generators.Length; g++)
				{
					if (generators[g] is ICustomComplexity)
						complete += ((ICustomComplexity)generators[g]).Progress(data);

					else
					{
						if (data.IsReady(generators[g]))
							complete ++;
					}
				}

				return complete;
			}


			public float GetApplyComplexity ()
			/// Gets the total complexity of the graph (including biomes) to evaluate the generate progress
			{
				HashSet<Type> allApplyTypes = GetAllOutputTypes();
				return allApplyTypes.Count;
			}


			private HashSet<Type> GetAllOutputTypes (HashSet<Type> outputTypes=null)
			/// Looks in subGraphs recursively
			{
				if (outputTypes == null)
					outputTypes = new HashSet<Type>();

				for (int g=0; g<generators.Length; g++)
					if (generators[g] is OutputGenerator)
					{
						Type type = generators[g].GetType();
						if (!outputTypes.Contains(type))
							outputTypes.Add(type);
					}

				foreach (Graph subGraph in SubGraphs())
					subGraph.GetAllOutputTypes(outputTypes);

				return outputTypes;
			}


			public float GetApplyProgress (TileData data)
			{
				return data.ApplyMarksCount;
			}

		#endregion


		#region Serialization

			public string assetName = "unknown"; //to show it in error during serialization
			public SemVer serializedVersion; 

			[SerializeField] private ulong[] serializedGeneratorIds300 = new ulong[0];
			[SerializeField] private ulong[] serializedInlets300 = new ulong[0]; //generics are not serialized, using ids
			[SerializeField] private ulong[] serializedOutlets300 = new ulong[0];
			[SerializeField] private int serializedNoiseSeed = 12345;
			[SerializeField] private int serializedNoisePermutation = 32768;

			[SerializeField] private GraphSerializer200Beta serializer200beta = null;


			public void OnBeforeSerialize ()
			{ 
				if (!validated)
					Validate();

				#if MM_DEBUG
				if (!saveChanges)
					return;
				#endif

				serializedVersion = MapMagicObject.version;
				try {assetName = name;}
				catch {}

				//gen ids
				if (serializedGeneratorIds300==null || serializedGeneratorIds300.Length!=generators.Length)
					serializedGeneratorIds300 = new ulong[generators.Length];

				for (int g=0; g<generators.Length; g++)
					serializedGeneratorIds300[g] = generators[g] != null ? generators[g].id : 0;

				//links
				if (serializedInlets300==null || serializedInlets300.Length!=links.Count)
					serializedInlets300 = new ulong[links.Count];

				if (serializedOutlets300==null || serializedOutlets300.Length!=links.Count)
					serializedOutlets300 = new ulong[links.Count];

				int i=0;
				foreach (var kvp in links)
				{
					serializedInlets300[i] = kvp.Key.Id;
					serializedOutlets300[i] = kvp.Value.Id;
					i++;
				}

				//noise
				(serializedNoiseSeed, serializedNoisePermutation) = random.Serialize();

				//resetting previous version if any
				serializer200beta = null;  
			}

			public void OnAfterDeserialize () 
			{
				//grGens (check removed)
				//if (UnityEditor.SerializationUtility.HasManagedReferencesWithMissingTypes(this))
				//	Debug.Log("Missing Type!");

				

				//links
				links.Clear();

				Dictionary<ulong, IInlet<object>> idToInlet = new Dictionary<ulong, IInlet<object>>();
				foreach (IInlet<object> inlet in Inlets())
				{
					ulong id = inlet.Id;
					if (!idToInlet.ContainsKey(id))
						idToInlet.Add(id, inlet);
				}

				Dictionary<ulong, IOutlet<object>> idToOutlet = new Dictionary<ulong, IOutlet<object>>();
				foreach (IOutlet<object> outlet in Outlets())
				{
					ulong id = outlet.Id;
					if (!idToOutlet.ContainsKey(id))
						idToOutlet.Add(id, outlet); 
				}

				for (int i=0; i<serializedInlets300.Length; i++)
				{
					if (!idToInlet.TryGetValue(serializedInlets300[i], out IInlet<object> linkInlet))
						{ Debug.LogError("Graph " + assetName + ": Could not deserialize link - connected inlet does not exist"); continue; }

					if (!idToOutlet.TryGetValue(serializedOutlets300[i], out IOutlet<object> linkOutlet))
						{ Debug.LogError("Graph " + assetName + ": Could not deserialize link - connected outlet does not exist"); continue; }

					links.Add(linkInlet, linkOutlet);
				}

				//noise
				random = new Noise(serializedNoiseSeed, serializedNoisePermutation);

				//Restoring v2 serialized graphs
				if ((int)serializedVersion < 30000  &&   serializer200beta != null) 
				{
					Debug.LogWarning("Loading MM2 graph. Although most nodes might be loaded, it won't generate the same result");
					serializer200beta.Deserialize(this);
				}

				Debug.Log("Graph deserialized"); 
			}



			public static Graph SerializedCopy (Graph src)
			/// Used for import, export and duplicate
			{
				return ScriptableObject.Instantiate(src);
				//seem to do the trick
				//if not use Den.Tools.Serialization.Serializer.Serialize() and Deserialize()
			}

		#endregion


		#region Debug

			#if MM_DEBUG
			private void Log (string msg, TileData data, StopToken stop)
			{
				if (Den.Tools.Log.enabled)
					Den.Tools.Log.AddThreaded("Graph."+msg, idName:data.tileName,
						("coord",data.area.Coord), 
						("is draft",data.isDraft), 
						("graph ver", IdsVersionsHash()), 
						("test val",data.heights?.arr[100]),
						("stop:",stop.stop), 
						("restart:",stop.restart) );
			}
			#endif

			public static Graph debugGraph; //assign via watch

			public string DebugUnitName (ulong id, bool useGraphName=true, string prevGraphNames=null)
			/// Gets the unit name, number, graph, layer, etc by it's. 
			/// Looks in subgraphs too
			/// Disable use graph name when running from thread
			{
				foreach (IUnit unit in AllUnits())
					if (unit.Id == id)
					{
						string unitName = DebugUnitName(unit);

						if (useGraphName) unitName += $" graph:{name}";
						if (prevGraphNames != null) unitName += $" prev:{prevGraphNames}";

						return unitName;
					}

				int biomeNum = 0;
				foreach (IUnit unit in AllUnits())
					if (unit is IBiome biome)
					{
						string subPrefix;
						if (useGraphName) subPrefix =  $"{prevGraphNames}.{name} (b:{biomeNum})";
						else subPrefix = $"(b:{biomeNum})";

						string subResult = biome.SubGraph.DebugUnitName(id, useGraphName, subPrefix);
						if (subResult != null)
							return subResult;

						biomeNum++;
					}
						
				return null;
			}


			public string[] DebugAllUnits (int subLevel=0)
			{
				List<string> strs = new List<string>();

				foreach (IUnit unit in AllUnits())
					strs.Add( DebugUnitName(unit) + " sub:" + subLevel );

				foreach (IUnit unit in AllUnits())
					if (unit is IBiome biome)
						strs.AddRange( biome.SubGraph.DebugAllUnits(subLevel+1) );

				return strs.ToArray();
			}


			public string DebugUnitName (IUnit unit)
			/// Just returns unit information
			{
				string unitName = $"{unit.GetType().Namespace}.{unit.GetType().Name}";
				string genName = $"{unit.Gen.GetType().Namespace}.{unit.Gen.GetType().Name}";

				int genNum = -1;
				for (int g=0; g<generators.Length; g++)
					if (generators[g] == unit.Gen)
						{ genNum=g; break; }

				int layerNum = -1; int layerCounter = 0;
				if (unit.Gen != unit  &&  unit.Gen is IMultiLayer multLayerGen)
					foreach (IUnit layer in multLayerGen.UnitLayers)
					{
						if (layer == unit)
							{ layerNum = layerCounter; break; }
						layerCounter++;
					}

				int inletNum = -1; int inletCounter = 0;
				if (unit.Gen != unit  &&  unit.Gen is IMultiInlet multInletGen)
					foreach (IUnit inlet in multInletGen.Inlets())
					{
						if (inlet == unit)
							{ inletNum = inletCounter; break; }
						inletCounter++;
					}

				int outletNum = -1; int outletCounter = 0;
				if (unit.Gen != unit  &&  unit.Gen is IMultiOutlet multOutletGen)
					foreach (IUnit outlet in multOutletGen.Outlets())
					{
						if (outlet == unit)
							{ outletNum = outletCounter; break; }
						outletCounter++;
					}

				return $"unit:{unitName}, gen:{genName}, id:{Id.ToString(unit.Id)}, g:{genNum}, l:{layerNum}, i:{inletNum}, o:{outletNum}";
			}

		#endregion
	}
}