using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using Den.Tools;
using Den.Tools.Matrices; //Normalize gen

using MapMagic.Products;


namespace MapMagic.Nodes
{
	public interface IUnit
	/// Either Generator or significant (processed by graph Generate) Layer
	{
		Generator Gen { get; } 
		void SetGen (Generator gen);

		ulong Id { get; set; }

		string GuiName { get; set; }

		IUnit ShallowCopy(); //TODO: replace with  MemberwiseClone?


		public static IInlet<object> Create (Type type, Generator gen, string name=null)
		//TODO unify Create for inlets, units, generators
		{
			 IInlet<object> layer = (IInlet<object>)Activator.CreateInstance(type);

			layer.SetGen(gen); 
			layer.Id = Den.Tools.Id.Generate(); 
			layer.GuiName = name;

			return layer;
		}
	}

	public interface IInlet<out T> : IUnit where T:class 
	/// The one that linked with the outlet via graph dictionary
	/// Could be generator or layer itself or a special inlet obj
	{ 
		ulong LinkedOutletId { get; set; } //is set each on clear or generate from graph luts. Just to quickly get inlet product/ready
		ulong LinkedGenId { get; set; } //the same, but gets Outlet.Gen (for ready)
		//TODO: graph uses links. These might needed to be replaced with graph.link[]
	}

	public interface IOutlet<out T> : IUnit where T:class { }
	/// The one that generates and stores product
	/// Could be generator or layer itself or a special outlet obj

	public interface IMultiInlet
	/// Generator that stores multiple inlets (either layered or standard)
	{ 
		IEnumerable<IInlet<object>> Inlets ();
		//TODO: think about removing multiinlet at all
		//replace with inlets {this, inlet1, inlet2}
	}

	public interface IMultiOutlet 
	/// Generator that stores multiple outlets
	{ 
		IEnumerable<IOutlet<object>> Outlets ();
	}


	[Serializable]
	public class Inlet<T> : IInlet<T> where T: class 
	/// The one that is assigned in non-layered multiinlet generators
	{
		[SerializeField] private Generator gen; 
		public Generator Gen { get{return gen;} private set{gen=value;} } //auto-property is not serialized
		public void SetGen (Generator gen) => Gen=gen;

		public ulong id; //don't generate on creating (will cause error on serialization. And no need to generate it on each serialize)
		public ulong Id 
			{get{
				if (id==0) 
					//throw new Exception(gen + " inlet id is 0");
					Debug.LogError("Inlet id is 0");
				return id;
			}set{
				id=value;
			}}
		public ulong LinkedOutletId { get; set; }  //Assigned every before each clear or generate
		public ulong LinkedGenId { get; set; } 

		public string guiName;
		public string GuiName { get => guiName; set => guiName = value; }



		public IUnit ShallowCopy() => (Inlet<T>)this.MemberwiseClone(); 
	}

	//non-generic versions in case outlets exposed somewhere directly (not as sub-element)
	[Serializable] public class MatrixInlet : Inlet<MatrixWorld> { }
	[Serializable] public class TransitionsInlet : Inlet<TransitionsList> { }
	[Serializable] public class SplinesInlet : Inlet<Den.Tools.Splines.SplineSys> { }
	[Serializable] public class SegsInlet : Inlet<Den.Tools.Lines.LineSys> { }
	[Serializable] public class MatrixSetInlet : Inlet<MatrixSet> { }
	[Serializable] public class ObjectInlet : Inlet<object> { } //to serialize arrays of arbitrary inlets (MissingGenPlug)
	[Serializable] public class MathInlet : Inlet<Calculator.Vector> { }


	[Serializable]
	public class Outlet<T> : IOutlet<T> where T: class
	/// The one that is assigned in non-layered multioutlet generators
	{
		[SerializeField] private Generator gen; 
		public Generator Gen { get{return gen;} private set{gen=value;} }
		public void SetGen (Generator gen) => Gen=gen;

		public ulong id; //don't generate on creating (will cause error on serialization)
		public ulong Id
		{get{
				if (id==0) 
					//throw new Exception(gen + " inlet id is 0");
					Debug.LogError("Inlet id is 0");
				return id;
			}set{
				id=value;
			}}

		public string guiName;
		public string GuiName { get => guiName; set => guiName = value; }

		public IUnit ShallowCopy() => (Outlet<T>)this.MemberwiseClone();
	}

	[Serializable] public class MatrixOutlet : Outlet<MatrixWorld> { }
	[Serializable] public class TransitionsOutlet : Outlet<TransitionsList> { }
	[Serializable] public class SplinesOutlet : Outlet<Den.Tools.Splines.SplineSys> { }
	[Serializable] public class SegsOutlet : Outlet<Den.Tools.Lines.LineSys> { }
	[Serializable] public class MatrixSetOutlet : Outlet<MatrixSet> { }
	[Serializable] public class ObjectOutlet : Outlet<object> { }


	public interface IPrepare
	/// Node has something to make in main thread before generate start in Prepare fn
	{
		void Prepare (TileData data, Terrain terrain);
		ulong Id { get; set; }
	}


	public interface ISceneGizmo 
	/// Displays some gizmo in a scene view
	{ 
		void DrawGizmo(); 
		bool hideDefaultToolGizmo {get;set;} 
	}


	[Flags] public enum OutputLevel { Draft=1, Main=2, Both=3 }  //Both is for gui purpose only

	public abstract class OutputGenerator : Generator
	/// Final output node (height, textures, objects, etc)
	{
		//just to mention: static Finalize (TileData data, StopToken stop);
		//Action<TileData,StopToken> FinalizeAction { get; }
		public abstract void ClearApplied (TileData data, Terrain terrain);
		public abstract OutputLevel OutputLevel {get;}
	}

	/*public interface IOutput
	/// Either output layer or output generator itself
	/// TODO: merge with output generator?
	{
		Generator Gen { get; } 
		void SetGen (Generator gen);
		ulong Id { get; set; }
	}
	*/

	public interface IApplyData
	{
		void Apply (Terrain terrain);
		int Resolution {get;}
	}


	public interface IApplyDataRoutine : IApplyData
	{
		IEnumerator ApplyRoutine (Terrain terrain);
	}


	public interface ICustomComplexity
	/// To implement both Complexity and Progress properties
	{
		float Complexity { get; } //default is 1
		float Progress (TileData data);  //can be different from Complexity if the generator is partly done. Max is Complexity, min 0
	}


	public interface IBiome : IUnit
	/// Passes the current graph commands to sub graph(s)
	{
		Graph SubGraph { get; }
		TileData SubData (TileData parent);  //clusters have non-hierarchial sub-data
	}

	public interface ICustomDependence
	/// Makes PriorGens generated before generating this one
	/// Used in Portals, mainly for checking link validity
	/// TODO: not used, remove
	{
		IEnumerable<Generator> PriorGens ();
	}

	public interface IRelevant { } 
	/// Should be generated when generating graph

	public interface IMultiLayer
	/// If generator contains layers that should be processed with graph Generate function
	/// In short - if layer contains id generator should be multi-layered
	/// Theoretically it should replace IMultiInlet/IMultiOutlet/IMultiBiome
	/// TODO: do not use layered generators
	{
//		IEnumerable<IUnit> Layers ();
		IList<IUnit> UnitLayers { get; set; }
		bool Inversed { get; } //for gui purpose, to draw mini
		bool HideFirst { get; }
	}

	public interface ICustomClear 
	/// Performs additional actions before clearing (like clearing sub-datas)
	{
		void OnClearing (Graph graph, TileData data, ref bool isReady, bool totalRebuild=false);  //calls when clearing all nodes, true if this node is changed
		//Not that regular change check if still performed on node
		//isReady: this call happens before marking ready in data, so isReady = default ready check (inlets, portals, etc).
		//Setting isReady to true will NOT mark this generator as ready on clear (while setting to false will)
		//totalRebuild: some nodes like Cluster require knowing whether user pressed "Rebuild" to do some flush actions on clear

		//void ClearDirectly (TileData data);  //Called by graph if gen field was changed. Will call data.ClearReady(gen) beforehand anyway
		//void ClearRecursive (TileData data);  //Called by graph on clearing recursive (no matter ready or not). Inlets are already cleared to this moment
		//void ClearAny (Generator gen, TileData data); //Called at top level graph each time any node changes. Iterating in sub-graph is done with this
	}


	public sealed class GeneratorMenuAttribute : Attribute
	{
		public string menu;
		public int section;
		public string name;
		public string menuName; //if not defined using real name
		public string iconName;
		public bool disengageable;
		public bool disabled;
		public int priority;
		public string helpLink;
		public bool lookLikePortal = false; //brush reads/writes are portals, but should look like gens
		public bool drawInlets = true; //displays inlets in header. Disable this if using custom node editor that shows inlets in field
		public bool drawOutlet = true;
		public bool drawButtons = true;
		public bool advancedOptions = false;
		public Type colorType = null; ///> to display the node in the color of given outlet mode
		public Type updateType;  ///> The class legacy generator updates to when clicking Update
		public string codeFile;
		public int codeLine;
		public int width; //starting width when creating new node

		//these are assigned on load attribute in gen and should be null by default
		public string nameUpper;
		public float nameWidth; //the size of the nameUpper in pixels
		public Texture2D icon;
		public Type type;
		public Color color;
	}


	[System.Serializable]
	public class Layer : IUnit
	/// Standard values to avoid duplicating them in each of generators layers
	{
		public Generator Gen { get { return gen; } private set { gen = value;} }
		public Generator gen; //property is not serialized
		public void SetGen (Generator gen) => this.gen=gen;

		public ulong id; //properties not serialized
		public ulong Id { get{return id;} set{id=value;} } 
		public ulong LinkedOutletId { get; set; }  //if it's inlet. Assigned every before each clear or generate //TODO: I don't like it
		public ulong LinkedGenId { get; set; } 

		public string guiName;
		public string GuiName { get => guiName; set => guiName = value; }

		public IUnit ShallowCopy() => (Layer)this.MemberwiseClone();

		public static Layer Create (Type type, Generator gen)
		{
			Layer layer = (Layer)Activator.CreateInstance(type);

			layer.SetGen(gen); 
			layer.Id = Den.Tools.Id.Generate(); 

			return layer;
		}
		public static T Create<T> (Generator gen) where T: Layer  => (T)Create(typeof(T), gen);
	}

	public interface IFnPortal<out T>  { string Name { get; set; } }
	public interface IFnEnter<out T> : IFnPortal<T>, IOutlet<T>  where T: class { }  //to use objects of mode IFnEnter<object>
	public interface IFnExit<out T> : IFnPortal<T>, IInlet<T>, IRelevant where T: class { } //fnExit is always generated (should be IRelevant)
	//interfaces required in draw editor, so they are stored here, not module
	//TODO: move to functions module if they will have new interface


	[Serializable]
	public abstract class Generator : IUnit, ISerializationCallbackReceiver
	{
		public bool enabled = true;

		public ulong id; //generated with timestamp and session counter  //0 is empty id - reassigning it automatically
		public ulong Id { get{return id;} set{id=value;} } 
		public ulong LinkedOutletId { get; set; }  //if it's inlet. Assigned every before each clear or generate
		public ulong LinkedGenId { get; set; } 

		public ulong version; //increment with GUI each time any parameter change to compare with data's last generated version to see if it's ready

		#if MM_DEBUG
		public double draftTime;
		public double mainTime;
		#endif

		public Vector2 guiPosition;
		public Vector2 guiSize;  //x is changable, y is to add this node to group
		public string guiName;
		public string GuiName { get => guiName; set => guiName = value; }

		public bool guiPreview; //is preview for this generator opened
		public bool guiAdvanced;
		public bool guiDebug;
		public bool guiField = true;
		public float guiFieldHeight = 60; //to resize with mouse, changed when gui displayed
		public float guiAdvancedHeight = 40;
		public bool GuiCollapsed => !guiField && !guiAdvanced && !guiPreview && !guiDebug;

		public FieldInlets fieldInlets = new FieldInlets(); 

		public Generator Gen { get{ return this; } }
		public void SetGen (Generator gen) { }


		public static Generator Create (Type type, Action<Generator> onGenInstantiated=null)
		///Factory instead of constructor since could not create instance of abstract class
		///onGenInstantiated is executed once gen appeared, before any ids assigned. For example, add default layers here.
		{
			if (type.IsGenericTypeDefinition) type = type.MakeGenericType(typeof(Den.Tools.Matrices.MatrixWorld)); //if mode is open generic mode - creating the default matrix world
			
			Generator gen = (Generator)Activator.CreateInstance(type);
			onGenInstantiated?.Invoke(gen);

			gen.id = Den.Tools.Id.Generate();

			foreach (IUnit unit in gen.AllUnits(includeSelf:false))
			{
				unit.SetGen(gen);
				unit.Id = Den.Tools.Id.Generate();
			}

			return gen;
		}

		public IUnit ShallowCopy() => (Generator)this.MemberwiseClone();  //1000 noise generators in 0.1 ms

		public virtual (string, int) GetCodeFileLine () => GetCodeFileLineBase();

		public (string, int) GetCodeFileLineBase (
			[System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "",
			[System.Runtime.CompilerServices.CallerLineNumber] int sourceLineNumber = 0)
		{
			return (sourceFilePath, sourceLineNumber);
			//var sf = new System.Diagnostics.StackTrace(1).GetFrame(0);
			//return (sf.GetFileName(), sf.GetFileLineNumber());
		}


		public abstract void Generate (TileData data, StopToken stop);
		/// The stuff generator does to read inputs (already prepared), generate, and write product(s). Does not affect previous generators, ready state or event, just the essence of generate


		#region Generic Type

			public static Type GetGenericType (Type type)
			/// Finds out if it's map, objects or lines node/inlet/outlet.
			/// Returns T if mode is T, IOutlet<T>, Inlet<T>, or inherited from any of those (where T is MatrixWorls, TransitionsList or LineSys, or OTHER mode)
			{
				Type[] interfaces = type.GetInterfaces();
				foreach (Type itype in interfaces)
				{
					if (!itype.IsGenericType) continue;
					//if (!typeof(IOutlet).IsAssignableFrom(itype) && !typeof(Inlet).IsAssignableFrom(itype)) continue;
					return itype.GenericTypeArguments[0];
				}

				return null;
			}

			//shotcuts to avoid evaluating by mode
			public static Type GetGenericType<T> (IOutlet<T> outlet) where T: class  =>  typeof(T);
			public static Type GetGenericType<T> (IInlet<T> inlet) where T: class  =>  typeof(T);
			public static Type GetGenericType (Generator gen) 
			{
				if (gen is IOutlet<object> outlet) return GetGenericType(outlet);
				if (gen is IInlet<object> inlet) return GetGenericType(inlet);
				return null;
			}
			public static Type GetGenericType (IOutlet<object> outlet)
			{
				if (outlet is IOutlet<MatrixWorld>) return typeof(MatrixWorld);
				else if (outlet is IOutlet<TransitionsList>) return typeof(TransitionsList);
				else if (outlet is IOutlet<Den.Tools.Splines.SplineSys>) return typeof(Den.Tools.Splines.SplineSys);
				else if (outlet is IOutlet<Den.Tools.Lines.LineSys>) return typeof(Den.Tools.Lines.LineSys);
				else if (outlet is IOutlet<MatrixSet>) return typeof(MatrixSet);
				else return GetGenericType(outlet.GetType());
			}
			public static Type GetGenericType (IInlet<object> inlet)
			{
				if (inlet is IInlet<MatrixWorld>) return typeof(MatrixWorld);
				else if (inlet is IInlet<TransitionsList>) return typeof(TransitionsList);
				else if (inlet is IInlet<Den.Tools.Splines.SplineSys>) return typeof(Den.Tools.Splines.SplineSys);
				else if (inlet is IInlet<Den.Tools.Lines.LineSys>) return typeof(Den.Tools.Lines.LineSys);
				else if (inlet is IInlet<MatrixSet>) return typeof(MatrixSet);
				else return GetGenericType(inlet.GetType());
			}
			public static Type GetGenericType (string typeName)
			{
				if (typeName.Contains("MatrixSet")) return typeof(MatrixSet);
				else if (typeName.Contains("MatrixWorld")) return typeof(MatrixWorld);
				else if (typeName.Contains("TransitionsList")) return typeof(TransitionsList);
				else if (typeName.Contains("Splines.SplineSys")) return typeof(Den.Tools.Splines.SplineSys);
				else if (typeName.Contains("Segs.SplineSys")) return typeof(Den.Tools.Lines.LineSys);
				else return Type.GetType(typeName);
			}

		#endregion


		#region Serialization/Placeholders

			//public ulong[] serializedUnitIds = null;

			//in case this generator code will be removed 
			public string serializedMissingDownloadString = null;
			public string[] serializedMissingInletTypes; 
			public string[] serializedMissingOutletTypes;
			public ulong[] serializedMissingInletIds;
			public ulong[] serializedMissingOutletIds;
			[NonSerialized] private bool serializedMissingDone = false; //calculating these only once - data can't be changed

			public virtual void OnBeforeSerialize ()
			{
				/*if (IsMultiUnit)
				{
					List<ulong> ids = new List<ulong>();
					foreach (IUnit unit in AllUnits(includeSelf:false))
						ids.Add(unit.Id);
					serializedUnitIds = ids.ToArray();
				}*/

				if (!serializedMissingDone)
				{
					List<string> inletTypes = new List<string>();
					List<ulong> inletIds = new List<ulong>();
					foreach (IInlet<object> inlet in AllInlets())
					{
						Type inletType = GetGenericType(inlet);
						inletTypes.Add($"{inletType.ToString()}, {inletType.Assembly.GetName()}");
						inletIds.Add(inlet.Id);
					}
					serializedMissingInletTypes = inletTypes.ToArray();
					serializedMissingInletIds = inletIds.ToArray();

					List<string> outletTypes = new List<string>();
					List<ulong> outletIds = new List<ulong>();
					foreach (IOutlet<object> outlet in AllOutlets())
					{
						Type outletType = GetGenericType(outlet);
						outletTypes.Add($"{outletType.ToString()}, {outletType.Assembly}");
						outletIds.Add(outlet.Id);
					}
					serializedMissingOutletTypes = outletTypes.ToArray();
					serializedMissingOutletIds = outletIds.ToArray();

					serializedMissingDone = true;
				}
			}

			public virtual void OnAfterDeserialize ()
			{
				/*if (IsMultiUnit  &&  serializedUnitIds != null  ||  serializedUnitIds.Length!=0) //might become multiUnit
				{
					int i=0;
					foreach (IUnit unit in AllUnits(includeSelf:false))
					{
						unit.SetGen(this);
						
						if (i < serializedUnitIds.Length)
							unit.Id = serializedUnitIds[i];
						else
							unit.Id = Den.Tools.Id.Generate();

						i++;
					}
				}*/

			}

			public Type AlternativeSerializationType
			{get{
				//if (this is IInlet<object> 
				return typeof(Placeholders.InletOutletPlaceholder);
			}}

		#endregion


		#region Units enumeration

			public IEnumerable<IInlet<object>> AllInlets ()
			/// combines all inlets - whether it is IInlet or IMultiInlet
			/// for editor only since it adds one enumerable layer
			{
				if (fieldInlets.HasFieldInlets) //math inlets first for no reason
					foreach (MathInlet infi in fieldInlets.Inlets())
						yield return infi;

				if (this is IInlet<object> thisInlet)
					yield return thisInlet;

				if (this is IMultiInlet multiInlet)
					foreach (IInlet<object> inlet in multiInlet.Inlets())
						yield return inlet;
			}

			public IEnumerable<IOutlet<object>> AllOutlets ()
			/// combines all inlets - whether it is IInlet or IMultiInlet. For editor only since it adds one enumerable layer
			{
				if (this is IOutlet<object> thisOutlet)
					yield return thisOutlet;

				if (this is IMultiOutlet multiOutlet)
					foreach (IOutlet<object> outlet in multiOutlet.Outlets())
						yield return outlet;
			}

			public bool IsMultiUnit => this is IMultiLayer || this is IMultiInlet || this is IMultiOutlet ;

			public IEnumerable<IUnit> AllUnits (bool includeSelf=false)
			/// inlets, outlets, layers, whatever. Gen istelf is also a unit
			{
				if (includeSelf)  
					yield return this;

				if (fieldInlets.HasFieldInlets) //math inlets first for no reason
					foreach (MathInlet infi in fieldInlets.Inlets())
						yield return infi;
			
				//if (this is IInlet<object>  ||  this is IOutlet<object>)
				//	yield return this;
				//	not including if self is disabled (for ex to set Gen or id)

				if (this is IMultiLayer lgen)
					foreach (IUnit layer in lgen.UnitLayers)
						yield return layer;

				if (this is IMultiInlet multiInlet)
					foreach (IInlet<object> inlet in multiInlet.Inlets())
						yield return inlet;

				if (this is IMultiOutlet multiOutlet)
					foreach (IOutlet<object> outlet in multiOutlet.Outlets())
						yield return outlet;
			}

		#endregion
	}
}