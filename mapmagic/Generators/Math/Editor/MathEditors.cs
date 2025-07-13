using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
//using UnityEngine.Profiling;

using Den.Tools;
using Den.Tools.GUI;


namespace MapMagic.Nodes.GUI
{
	public static class MathGeneratorEditors
	{
		[Draw.Editor(typeof(Math.Constant300))]
		public static void DrawExpression (Math.Constant300 gen)
			{using(ProfilerExt.Profile("Constant Draw"))
		{
			using (Cell.Padded(4,4,0,0))
			{
				using (Cell.LineStd) Draw.Field(ref gen.valType, "Type");

				using (Cell.LineStd)
				{
					switch (gen.valType)
					{
						case Math.Constant300.ValType.Float: gen.vec.Convert(Draw.Field(gen.vec.x, "Value")); break;
						case Math.Constant300.ValType.Bool: gen.vec.Convert(Draw.Toggle(gen.vec.x > 0, "Value")); break;
						case Math.Constant300.ValType.Integer: gen.vec.Convert(Draw.Field((int)gen.vec.x, "Value")); break;
						case Math.Constant300.ValType.Coord: gen.vec.Convert(Draw.Field(new Coord((int)gen.vec.x, (int)gen.vec.y), "Value")); break;
						case Math.Constant300.ValType.VectorXZ: gen.vec.Convert(Draw.Field(new Vector2D(gen.vec.x, gen.vec.z), "Value")); break;
						case Math.Constant300.ValType.Vector2: gen.vec.Convert(Draw.Field(new Vector2(gen.vec.x, gen.vec.y), "Value")); break;
						case Math.Constant300.ValType.Vector3: gen.vec.Convert(Draw.Field(new Vector3(gen.vec.x, gen.vec.y, gen.vec.z), "Value")); break;
						case Math.Constant300.ValType.Vector4: gen.vec.Convert(Draw.Field(new Vector4(gen.vec.x, gen.vec.y, gen.vec.z, gen.vec.w), "Value")); break;
						case Math.Constant300.ValType.Color: gen.vec.Convert(Draw.Field(new Color(gen.vec.x, gen.vec.y, gen.vec.z, gen.vec.w), "Value")); break;
						case Math.Constant300.ValType.UnityObject: gen.vec.Convert(Draw.ObjectField(gen.vec.uobj, "Value")); break;
					}
				}

				using (Cell.LineStd) Draw.ToggleLeft(ref gen.expose, "Expose in Parent");
			}
		}}


		[Draw.Editor(typeof(Math.Expression300))]
		public static void DrawExpression (Math.Expression300 gen)
			{using(ProfilerExt.Profile("Expression Draw"))
		{
			using (Cell.Padded(4,4,0,0))
			{
				using (Cell.LineStd)
				{
					//field
					using (Cell.Row)
					{
						Draw.Field(ref gen.expression);

						if (Cell.current.valChanged)
						{
							//parsing expression
							gen.result = null;
							gen.error = null;
							gen.warning = null;

							if (gen.expression==null  ||  gen.expression.Length == 0)
								gen.result = "Non exposed";

							else
							{
								gen.calculator = Calculator.Parse(gen.expression);

								if (!gen.calculator.CheckValidity(out string anyError))
									gen.error = anyError; //error assigned only if non-valid

								else
									gen.result = "All okay";
							}

							//creating inlets
							HashSet<string> allRefs = gen.calculator.References();
							gen.inlets.Sync(allRefs,
								constructor: name => (MathInlet)IInlet<object>.Create(typeof(MathInlet), gen, name),
								destructor: inlet => GraphWindow.current.graph.UnlinkInlet(inlet) 
								);
						}
					}

					//errors/warnings icon
					using (Cell.RowPx(15))
					{
						if (gen.error != null) Draw.Icon(UI.current.textures.GetTexture("DPUI/Icons/Error"));
						else if (gen.warning != null) Draw.Icon(UI.current.textures.GetTexture("DPUI/Icons/Warning"));
						else Draw.Icon(UI.current.textures.GetTexture("DPUI/Icons/Okay"));
					}
				}

				//errors/warnings text
				if (gen.error != null) 
					using (Cell.LineStd)
						Draw.Label(gen.error, style:UI.current.styles.topLabel);

				else if (gen.warning != null) 
					using (Cell.LineStd)
						Draw.Label(gen.warning, style:UI.current.styles.topLabel);

				//else
				//	using (Cell.LineStd)
				//		Draw.Label(gen.result, style:UI.current.styles.topLabel);


				#if MM_DEBUG
				if (gen.calculator != null)
					using (Cell.LineStd) Draw.Label(gen.calculator.ToString());
				#endif
			}
		}}


	}
}