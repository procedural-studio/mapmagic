using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Den.Tools;
using Den.Tools.Lines;
using Den.Tools.Polylines;
using Den.Tools.Matrices;
using Den.Tools.GUI;
using MapMagic.Core;
using MapMagic.Products;

namespace MapMagic.Nodes.SegsGenerators
{
	[System.Serializable]
	[GeneratorMenu (
		menu="Segs/Standard",  
		name ="Lichtenberg", 
		iconName="GeneratorIcons/Constant",
		colorType = typeof(LineSys), 
		disengageable = true, 
		helpLink ="https://gitlab.com/denispahunov/mapmagic/-/wikis/SplinesGenerators/constant")]
	public class Lichtenberg300 : Generator, IInlet<TransitionsList>, IOutlet<LineSys>
	{
		[Val("Raise")]	public float raise = 0;


		public override void Generate (TileData data, StopToken stop)
		{
			TransitionsList points = data.ReadInletProduct(this);

			if (stop != null && stop.stop) return;
			IrregularGridMesh mesh = Converter.TransitionsToMesh(points);

			if (stop != null && stop.stop) return;
			mesh.DelaunayTriangulate();

			if (stop != null && stop.stop) return;
			Polyline polyline = Converter.MeshToPolyline(mesh);
			polyline.verts = Converter.TransitionsToVectorArray(points); //taking verts with heights from original points

			if (stop != null && stop.stop) return;
			Polyline.LichtenbergReverse(polyline, raise);

			if (stop != null && stop.stop) return;
			LineSys line = Converter.PolylineToLine(polyline);

			data.StoreProduct(this, line);
		}
	}
}
