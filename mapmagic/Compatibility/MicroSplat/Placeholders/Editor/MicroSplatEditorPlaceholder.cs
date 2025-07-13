using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

using Den.Tools;
using Den.Tools.GUI;
using MapMagic.Core;  //used once to get tile size
using MapMagic.Products;
using MapMagic.Nodes.MatrixGenerators;

#if !__MICROSPLAT__
namespace MapMagic.Nodes.GUI
{

	public static class MicroSplatEditor 
	{
		[Draw.Editor(typeof(MicroSplatOutput200))]
		public static void DrawMicroSplat (MicroSplatOutput200 gen)
		{
			using (Cell.LinePx(60))
				Draw.Helpbox("MicroSplat doesn't seem to be installed, or MicroSplat compatibility is not enabled in settings");

			GeneratorEditors.DrawLayers(gen, ref gen.layers, DrawMicroSplatLayer);

			Graph graph = GraphWindow.current.graph;

			using (Cell.LinePx(20)) 
				LayersEditor.DrawLayersThemselves(Cell.Parent, gen.layers.Length, 
					onDraw: n => DrawMicroSplatLayer(gen, n), 
					roundBottom:true);

			using (Cell.LinePx(20)) 
				LayersEditor.DrawAddRemove(Cell.Parent, "Layers", ref gen.layers,
					onAdd: n => Layer.Create<MicroSplatOutput200.MicroSplatLayer>(gen),
					onRemove: n => GraphWindow.current.graph.Remove(gen.layers[n]),
					onMove: (f,t) => { graph.UnlinkInlet(gen.layers[f]); graph.UnlinkInlet(gen.layers[t]); } //unlinking both
				);
		}

		private static void DrawMicroSplatLayer (Generator tgen, int num)
		{
			MicroSplatOutput200 gen = (MicroSplatOutput200)tgen;
			MicroSplatOutput200.MicroSplatLayer layer = gen.layers[num];
			if (layer == null) return;


			Cell.EmptyLinePx(3);
			using (Cell.LinePx(28))
			{
				if (num!=0) 
					using (Cell.RowPx(0)) GeneratorDraw.DrawInlet(layer);

				Cell.EmptyRow();

				using (Cell.RowPx(0)) GeneratorDraw.DrawOutlet(layer);
			}
			Cell.EmptyLinePx(3);
		}
	}
}
#endif
