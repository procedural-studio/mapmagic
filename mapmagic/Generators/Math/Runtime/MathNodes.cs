using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using Den.Tools;

using MapMagic.Products;


namespace MapMagic.Nodes.Math
{
	[System.Serializable]
	[GeneratorMenu (menu="Math", name ="Input", iconName="GeneratorIcons/Constant", disengageable = true, 
		codeFile = "MathNodes", codeLine = 14,
		helpLink = "https://gitlab.com/denispahunov/mapmagic/-/wikis/MatrixGenerators/Constant")]
	public class Constant300 : Generator, IOutlet<Calculator.Vector>
	{
		public override (string, int) GetCodeFileLine () => GetCodeFileLineBase();  //to get here with right-click on generator

		public Calculator.Vector vec = new Calculator.Vector();

		public enum ValType { Bool, Integer, Float, Coord, VectorXZ, Vector2, Vector3, Vector4, Color, UnityObject }
		public ValType valType = ValType.Float;

		public bool expose;

		public override void Generate (TileData data, StopToken stop) 
		{
			if (stop!=null && stop.stop) return;
			data.StoreProduct(this, vec);
		}
	}


	[System.Serializable]
	[GeneratorMenu (menu="Math", name ="Expression", iconName="GeneratorIcons/Constant", disengageable = true, 
		codeFile = "MathNodes", codeLine = 14,
		helpLink = "https://gitlab.com/denispahunov/mapmagic/-/wikis/MatrixGenerators/Constant")]
	public class Expression300 : Generator, IMultiInlet, IOutlet<Calculator.Vector>, ISerializationCallbackReceiver
	{
		public override (string, int) GetCodeFileLine () => GetCodeFileLineBase();  //to get here with right-click on generator
		
		public string expression = "";
		public Calculator calculator;

		public string error = null;
		public string warning = null;
		public string result = null;

		public SortedDictionary<string,MathInlet> inlets = new SortedDictionary<string,MathInlet>(); //dictionary to sync on expression change
		public IEnumerable<IInlet<object>> Inlets() 
		{
			foreach (var kvp in inlets)
				yield return kvp.Value;
		}


		public override void Generate (TileData data, StopToken stop) 
		{
			if (stop!=null && stop.stop) return;
			if (calculator==null) return; 

			#if MM_DEBUG
			HashSet<string> vars = calculator.References();
			#endif

			Dictionary <string,Calculator.Vector> varsToVecs = new Dictionary<string, Calculator.Vector>();
			foreach (var kvp in inlets)
			{
				string name = kvp.Key;
				MathInlet inlet = kvp.Value;

				#if MM_DEBUG
				if (!vars.Contains(name))
					throw new Exception($"No variable with this inlet name:{name}");
				#endif

				Calculator.Vector vec = data.ReadInletProduct(inlet);
				if (vec == null)
					vec = new Calculator.Vector(0);

				varsToVecs.Add(name,vec);
			}

			Calculator.Vector result = calculator.Calculate(varsToVecs);

			data.StoreProduct(this, result);
		}

		#region Serialization

			[SerializeField] private string[] serializedInletKeys;
			[SerializeField] private MathInlet[] serializedInletValues;

			public override void OnBeforeSerialize ()
			{
				serializedInletKeys = new string[inlets.Count];
				serializedInletValues = new MathInlet[inlets.Count];
				int i = 0;
				foreach (var kvp in inlets)
				{
					serializedInletKeys[i] = kvp.Key;
					serializedInletValues[i] = kvp.Value;
					i++;
				}
			}

			public override void OnAfterDeserialize ()
			{
				inlets = new SortedDictionary<string, MathInlet>();
				for (int i = 0; i < serializedInletKeys.Length; i++)
				{
					inlets.Add(serializedInletKeys[i], serializedInletValues[i]);
				}

				calculator = Calculator.Parse(expression);

				if (result.Length == 0) result = null;
				if (warning.Length == 0) warning  = null;
				if (error.Length == 0) error = null; 
			}

		#endregion

	}
}