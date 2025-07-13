using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

using Den.Tools;
using Den.Tools.GUI;
using System.Runtime.InteropServices;
using UnityEditor;


namespace MapMagic.Nodes.GUI
{
	public static class MissingGenPlugDraw
	{
		[Draw.Editor(typeof(MissingGenPlug))]
		public static void DrawMissingGenPlug (MissingGenPlug gen)
		{
			int maxCount = System.Math.Max(gen.inlets.Length, gen.outlets.Length);
			for (int i=0; i<maxCount; i++)
				using (Cell.LineStd)
				{
					if (i<gen.inlets.Length)
						using (Cell.RowPx(0)) 
							GeneratorDraw.DrawInlet(gen.inlets[i]);

					//Cell.EmptyRowPx(10);
					//using (Cell.Row) Draw.Label(menuAtt.nameUpper, style:UI.current.styles.bigLabel);

					Cell.EmptyRow();

					if (i<gen.outlets.Length)
						using (Cell.RowPx(0)) 
							GeneratorDraw.DrawOutlet(gen.outlets[i]);

				}

			using (Cell.LinePx(0))
			using (Cell.Padded(1,1,0,0)) 
			{
				using (Cell.LineStd) Draw.Field(ref gen.className);
				using (Cell.LineStd) Draw.Field(ref gen.namespaceName);
				using (Cell.LineStd) Draw.Field(ref gen.assemblyName);

				Cell.EmptyLinePx(10);
				using (Cell.LinePx(80)) Draw.Field(ref gen.serializedData);

				Cell.EmptyLinePx(10);
				using (Cell.LineStd) 
					if (Draw.Button("Restore"))
						MissingGenPlug.Restore(gen, GraphWindow.current.graph);
			}
		}
	}
}