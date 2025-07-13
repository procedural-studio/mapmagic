using System;

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

using Den.Tools;
using MapMagic.Products;



namespace MapMagic.Nodes.Biomes
{
	public abstract class BaseFunctionGenerator : Generator, ISerializationCallbackReceiver
	/// Separate class mainly for gui purpose, to not distinguish between fn, loop and cluster
	{
		public IFnInlet<object>[] inlets = new IFnInlet<object>[0];
		public IEnumerable<IInlet<object>> Inlets() => inlets;

		public IFnOutlet<object>[] outlets = new IFnOutlet<object>[0];
		public IEnumerable<IOutlet<object>> Outlets() => outlets;
		//TODO: maybe switch to sorteddictionaries with their Sync?

		public Graph subGraph;
		public Graph SubGraph => subGraph;
		public TileData SubData (TileData parent) => parent.GetSubData(id);


		[Serializable] 
		public class ValTypeName
		{
			public Calculator.Vector val;
			public Type type;
			public string name;

			public ValTypeName (Calculator.Vector val, Type type, string name)
				{this.val = val; this.type = type; this.name = name;}
		}

		public SortedDictionary<ulong,ValTypeName> values = new SortedDictionary<ulong,ValTypeName>();

		[NonSerialized] public ulong guiPrevGraphVersion = 0;
		[NonSerialized] public string guiSameInletsName = null;
		[NonSerialized] public string guiSameOutletsName = null;

		#region Serialization

			[SerializeField] private ulong[] serValuesKeys; 
			[SerializeField] private ValTypeName[] serValuesValues;

			public override void OnBeforeSerialize ()
			{
				int count = values.Count;
				if (serValuesKeys == null || serValuesKeys.Length != count)
				{
					serValuesKeys = new ulong[count];
					serValuesValues = new ValTypeName[count];
				}

				int i = 0;
				foreach (var kvp in values)
				{
					serValuesKeys[i] = kvp.Key;
					serValuesValues[i] = kvp.Value;
					i++;
				}
			}

			public override void OnAfterDeserialize ()
			{
				values = new SortedDictionary<ulong, ValTypeName>();
				for (int i = 0; i < serValuesKeys.Length; i++)
				{
					values[serValuesKeys[i]] = serValuesValues[i];
				}
			}

		#endregion
	}


	[Serializable]
	[GeneratorMenu (menu="Functions", name ="Function", iconName="GeneratorIcons/Function", priority = 1, colorType = typeof(IBiome))]
	public class Function210 : BaseFunctionGenerator, IMultiInlet, IMultiOutlet, IPrepare, IBiome, ICustomComplexity, ICustomClear, IRelevant
	//IRelevant since it could be used as biome
	{
		public float Complexity => subGraph!=null ? subGraph.GetGenerateComplexity() : 0;
		public float Progress (TileData data)
		{
			if (subGraph == null) return 0;

			TileData subData = SubData(data);
			if (subData == null) return 0;

			return subGraph.GetGenerateProgress(subData);
		}


		public void Prepare (TileData data, Terrain terrain)
		{
			if (subGraph == null) return;
			
			TileData subData = data.CreateLoadSubData(id);

			subGraph.Prepare(subData, terrain);
		}


		public override void Generate (TileData data, StopToken stop)
		{
			if (stop!=null && stop.stop) return;
			if (subGraph == null) return;

			TileData subData = data.CreateLoadSubData(id);

			//sending inlet products to sub-graph enters
			if (stop!=null && stop.stop) return;
			for (int i=0; i<inlets.Length; i++)
			{
				IFnInlet<object> inlet = inlets[i];
				object product = data.ReadInletProduct(inlet);

				IFnEnter<object> fnEnter = (IFnEnter<object>)inlet.GetInternalPortal(subGraph); 
				subData.StoreProduct(fnEnter, product);
				subData.MarkReady(fnEnter.Id, fnEnter.Gen.version); //TODO:check. or use this node version
			}

			//same way injecting value overrides
			if (stop!=null && stop.stop) return;
			foreach (var kvp in values)
			{
				ulong id = kvp.Key;
				Generator gen = subGraph.GetGeneratorById(kvp.Key);

				subData.StoreProduct(id, kvp.Value.val);
				subData.MarkReady(id, gen.version); //gen is treated is ready when result.version==gen.version
			}

			//generating
			if (stop!=null && stop.stop) return;
			subGraph.Generate(subData, stop:stop); //with fn override, not graph defaults

			//returning products back from sub-graph exists
			if (stop!=null && stop.stop) return;
			for (int o=0; o<outlets.Length; o++)
			{
				IFnOutlet<object> outlet = outlets[o];
				IFnExit<object> fnExit = (IFnExit<object>)outlet.GetInternalPortal(subGraph);
				object product = subData.ReadInletProduct(fnExit);

				data.StoreProduct(outlet, product);
			}
		}

		public void OnClearing (Graph graph, TileData data, ref bool isReady, bool totalRebuild=false) 
		{
			// What should be cleared and when:
			// - On this graph modification (inlet change):			this node (done by default), all subgraph outputs (for biomes)
			// - Subgraph modification (any subgraph node change):	this node, all subgraph relevants
			// - Exposed values change (this node change):			this node (done by default), exp related subgraph node, subgraph relevants

			//clarifying whether this generator changed directly or recursively
			bool versionChanged = data.VersionChanged(this);
			bool thisChanged = !isReady && versionChanged;
			bool inletChanged = !isReady && !versionChanged;

			if (subGraph == null) return;
			TileData subData = data.CreateLoadSubData(id);

			//resetting exposed related nodes on this node change
			if (thisChanged)
			{
				//TODO: reset only changed generators
				//foreach (IUnit expUnit in subGraph.exposed.AllUnits(subGraph))
				//	subData.ClearReady((Generator)expUnit);
			}

			//resetting outputs/relevants on inlet or this changed
			if (inletChanged || thisChanged)
			{
				foreach (Generator relGen in subGraph.RelevantGenerators(data.isDraft)) 
					subData.ClearReady(relGen);
			}

			//iterating in sub-graph after
			subGraph.ClearChanged(subData);

			//at the end clearing this if any subgraph relevant changed
			if (isReady)
			{
				foreach (Generator relGen in subGraph.RelevantGenerators(data.isDraft)) 
					if (!subData.IsReady(relGen))
						isReady = false;
			}
		}
	}
}
