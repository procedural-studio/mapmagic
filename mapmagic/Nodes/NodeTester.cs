using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

using Den.Tools;
using Den.Tools.Tests;
using MapMagic.Products;
using MapMagic.Terrains;
using MapMagic.Core;
using System;
using System.Runtime.InteropServices;
using static Den.Tools.Tests.UniversalTestAsset;


[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("MapMagic.Editor")]

namespace MapMagic.Nodes
{
	[CreateAssetMenu(fileName = "Test", menuName = "MapMagic Node Test")]
	public class NodeTester : ScriptableObject, Gizmo.IGizmoHolder
	///Based on UniversalTester, but uses node instead arbitrary object
	{
		public string genTypeName;
		public bool genTypeValid;
		[SerializeReference] public Generator generator;

		public Area area = new Area(new Coord(0,0), 513, 32, new Vector2D(1000,1000));
		public float height = 200;
		public int seed = 12345;

		public Argument[] inputs = new Argument[0];
		public Argument[] results = new Argument[0];

		public bool updateOnValueChange = false;
		public bool updateOnSceneChange = false;

		[SerializeField] internal bool guiInputsExpanded;
		[SerializeField] internal bool guiGenExpanded; //using GeneratorInspector folded list instead
		[SerializeField] internal bool guiResultsExpanded;


		public void Test (bool catchExceptions=true, int iterations=1)
		{
			//increment undo on calling - otherwise will create lag on each result.obj assignments
			//UnityEditor.Undo.IncrementCurrentGroup();

			//(object instanceObj, object[] argObjs) = PrepareObjects();
			
			var stopwatch = new System.Diagnostics.Stopwatch();
			double minTime = double.MaxValue;

			TileData tileData = new TileData();
			tileData.area = area;
			tileData.globals = new Globals(); //TODO: avoid creating it al the time
			tileData.globals.height = height;
			tileData.random = new Noise(seed, 32768);
			tileData.isPreview = true;
			tileData.isDraft = false;

			int i = 0;
			foreach (IInlet<object> inlet in generator.AllInlets())
			{
				if (i >= inputs.Length)
					throw new Exception("Not enough arguments provided to test this generator");
				
				if (Generator.GetGenericType(inlet)  !=  inputs[i].type)
					throw new Exception($"Input {i} expected to be {Generator.GetGenericType(inlet)}, but got {inputs[i].type}");

				ulong linkedId = (ulong)(1000 + i);
				inlet.LinkedOutletId = linkedId; //this is what used by generator to get product

				tileData.StoreProduct(linkedId, inputs[i].obj);

				i++;
			}

			try
			{
				for (int it=0; it<iterations; it++)
				{
					stopwatch.Start();

					generator.Generate(tileData, null);

					stopwatch.Stop();
					if (stopwatch.ElapsedMilliseconds < minTime)
						minTime = stopwatch.ElapsedMilliseconds;
					stopwatch.Reset();
				}
			}
			catch (Exception e)
			{
				//if (catchExceptions)
				//	throw e;
				//else
					Debug.LogError(e.InnerException);
			}

			//syncing outpout arguments arrays
			List<object> allProducts = new List<object>();
			foreach (IOutlet<object> outlet in generator.AllOutlets())
				allProducts.Add( tileData.ReadProduct(outlet.Id) );

			if (results.Length != allProducts.Count)
				ArrayTools.Resize(ref results, allProducts.Count, n => new Argument() );

			//renaming unnamed output arguments
			int o = 0;
			foreach (IOutlet<object> outlet in generator.AllOutlets())
			{
				if (results[o].name == "argument"  ||  results[o].name.Length==0) //default or no name
					results[o].name = outlet.GuiName;
				o++;
			}

			//setting products to output arguments
			for (o=0; o<results.Length; o++)
			{
				Type oType = null;
				if (allProducts[o] != null)
					oType = allProducts[o].GetType();

				if (results[o].type != oType)
				{
					results[o].type = oType;
					results[o].typeName = oType!=null ? oType.ToString() : "null";
				}
				results[o].obj = allProducts[o];
			}
		}


		private static string[] genTypeNamespaces = new string[] { 
			"MapMagic.Nodes.MatrixGenerators", 
			"MapMagic.Nodes.ObjectsGenerators", 
			"MapMagic.Nodes.SegsGenerators",
			"MapMagic.Nodes.SplinesGenerators",
			"MapMagic.Nodes.Biomes",
			"MapMagic.Nodes.Math" };

		internal bool RefreshGeneratorType ()
		///True if mode has changed
		{
			Type newType = ReflectionExtensions.GetTypeFromAllAssemblies(genTypeName);
			
			if (newType == null)
				foreach (string ns in genTypeNamespaces)
				{
					newType = ReflectionExtensions.GetTypeFromAllAssemblies(ns + "." + genTypeName);
					if (newType != null)
						break;
				}

			genTypeValid = newType != null;

			if (newType == null  &&  generator == null)
				return false;

			else if (newType == null  &&  generator != null)
			{
				generator = null;
				return true;
			}

			else if (generator != null  &&  generator.GetType() == newType)
				return false;

			else
			{
				generator = Generator.Create(newType);
				return true;
			}
		}


		public IEnumerable<Argument> AllArguments ()
		///iterates in instance, arguments and results
		{
			if (inputs != null)
				foreach (Argument a in inputs)
					yield return a;

			if (results != null)
				foreach (Argument a in results)
					yield return a;
		}

		public IEnumerable<Gizmo> AllGizmos ()
		{
			foreach (Argument arg in AllArguments())
				yield return arg.gizmo;
		}
	}
}
