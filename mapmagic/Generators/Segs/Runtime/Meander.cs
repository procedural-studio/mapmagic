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
		name ="Meander", 
		iconName="GeneratorIcons/Constant",
		colorType = typeof(LineSys), 
		disengageable = true, 
		helpLink ="https://gitlab.com/denispahunov/mapmagic/-/wikis/SplinesGenerators/constant")]
	public class MeanderGen : Generator, IInlet<LineSys>, IOutlet<LineSys>, IMultiOutlet
	{
		[Val("Iterations")]	public int iterations = 10;
		[Val("Subdivide Dist")]	public int subdivideDist = 10;
		[Val("Inertia")] public float inertia = 10;

		[Val("Initial Randomize")] public float initialRandomize = 0.5f;
		[Val("Seed")] public int seed = 12345;

		[Val("Oxbows", "Outlet")] public SegsOutlet oxbowsOut = new SegsOutlet();
		[Val("Stamp", "Outlet")] public MatrixOutlet stampMaskOut = new MatrixOutlet();

		public IEnumerable<IOutlet<object>> Outlets()  { yield return oxbowsOut; yield return stampMaskOut; }

		public override void Generate (TileData data, StopToken stop)
		{
			LineSys lines = data.ReadInletProduct(this);
			if (lines == null || !enabled) return; 

			lines = new LineSys(lines);
			LineSys oxbows = new LineSys();

			Noise random = new Noise(seed);

			foreach (Line line in lines.lines)
			{
				

				line.SubdivideDist(subdivideDist); //should be re-subdivided after noise anyways
				line.Randomize(initialRandomize, random);

				Line initialLine = new Line(line);

				for (int i=0; i<iterations; i++)
				{
					line.SubdivideDist(subdivideDist);

					//TODO: goes subdividing and subdividing
					if (line.nodes.Length > initialLine.nodes.Length*10)
					{
						Den.Tools.Tests.UniversalTestAsset.copiedObject = new LineSys(initialLine);
						Debug.LogError("Infinite spline detected");
						break;
					}

					line.Inertia(inertia);
					line.ShortCut(subdivideDist, out List<Line> oxbowLines);

					if (i!=0) //do not add initial randomization oxbows and stamps - they are fake
					{
//						oxbows.Add(oxbowLines);
					}
				}
			}

			data.StoreProduct(this, lines);
			data.StoreProduct(oxbowsOut, oxbows);
		}
	}
}
