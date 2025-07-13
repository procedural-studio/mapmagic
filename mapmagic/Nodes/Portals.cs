using System;
using System.Reflection;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

using Den.Tools;
using MapMagic.Products;
using Den.Tools.Matrices;

namespace MapMagic.Nodes
{
	[GeneratorMenu (menu = "Map/Portals", name = "Portal", menuName = "Enter", iconName = "GeneratorIcons/PortalIn", lookLikePortal=true)] public class MatrixPortalEnter : PortalEnter<Den.Tools.Matrices.MatrixWorld> { }
	[GeneratorMenu (menu = "Map/Portals", name ="Portal", menuName = "Exit", iconName = "GeneratorIcons/PortalOut", lookLikePortal=true)] public class MatrixPortalExit : PortalExit<Den.Tools.Matrices.MatrixWorld> { }
	[GeneratorMenu (menu = "Map/Reroute", name ="Reroute", menuName = "", lookLikePortal=true, width=50)] public class MatrixReroute : Reroute<Den.Tools.Matrices.MatrixWorld> { }
	//[GeneratorMenu (menu = "Objects/Portals", name = "Enter", iconName = "GeneratorIcons/PortalIn", lookLikePortal=true)] public class ObjectsPortalEnter : PortalEnter<TransitionsList> { }
	//[GeneratorMenu (menu = "Objects/Portals", name ="Exit", iconName = "GeneratorIcons/PortalOut", lookLikePortal=true)] public class ObjectsPortalExit : PortalExit<TransitionsList> { }
	//[GeneratorMenu (menu = "Line/Portals", name = "Enter", iconName = "GeneratorIcons/PortalIn", lookLikePortal=true)] public class SplinePortalEnter : PortalEnter<Plugins.Splines.LineSys> { }
	//[GeneratorMenu (menu = "Line/Portals", name ="Exit", iconName = "GeneratorIcons/PortalOut", lookLikePortal=true)] public class SplinePortalExit : PortalExit<Plugins.Splines.LineSys> { }
	//these are in their modules


	public interface IPortalEnter<out T> :IInlet<T>, IOutlet<T> where T:class {}  //to use PortalEnter<object> in draw {}
	public interface IPortalExit<out T> :  IInlet<T>, IOutlet<T> where T:class
	{
		IPortalEnter<object> GetEnter (Graph graph);
		void AssignEnter (IPortalEnter<object> enter, Graph graph);
	}
	public interface IReroute<out T> :IInlet<T>, IOutlet<T> where T:class  {}


	[Serializable]
	[GeneratorMenu(name = "Generic Portal Enter")]
	public class PortalEnter<T> : Generator, IPortalEnter<T>, IInlet<T>, IOutlet<T>  where T: class, ICloneable
	{
		public override void Generate (TileData data, StopToken stop) 
		{ 
			if (stop!=null && stop.stop) return;
			T src = data.ReadInletProduct(this);
			if (src == null) return;
			data.StoreProduct(this, src);
		}
	}


	[Serializable]
	[GeneratorMenu (name ="Generic Portal Exit")]
	public class PortalExit<T> : Generator, IPortalExit<T>, IInlet<T>, IOutlet<T> where T: class, ICloneable 
	{
		public IPortalEnter<object> GetEnter (Graph graph) => graph.LinkedOutlet(this) as IPortalEnter<T>;

		public void AssignEnter (IPortalEnter<object> ienter, Graph graph)
		{
			graph.UnlinkInlet(this);
			graph.Link(ienter, this);
		}

		public override void Generate (TileData data, StopToken stop) 
		{ 
			if (stop!=null && stop.stop) return;
			T src = data.ReadInletProduct(this);
			if (src == null) return;
			data.StoreProduct(this, src);
		}
	}


	[Serializable]
	[GeneratorMenu (name ="Generic Reroute")]
	public class Reroute<T> : Generator, IReroute<T>, IInlet<T>, IOutlet<T> where T: class, ICloneable 
	{
		public override void Generate (TileData data, StopToken stop) 
		{ 
			if (stop!=null && stop.stop) return;
			T src = data.ReadInletProduct(this);
			if (src == null) return;
			data.StoreProduct(this, src);
		}
	}
}