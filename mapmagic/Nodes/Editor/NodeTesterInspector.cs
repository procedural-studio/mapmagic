using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using System.Reflection;

using Den.Tools.GUI;
using Den.Tools.Tests;
using MapMagic.Nodes;
using MapMagic.Nodes.GUI;
using MapMagic.Products;
using MapMagic.Terrains;
using static MapMagic.Nodes.NodeTester;
using static Den.Tools.Tests.UniversalTestAsset;
//using static Den.Tools.Tests.UniversalTestAsset.Argument;


namespace Den.Tools.Tests
{
	[CustomEditor(typeof(NodeTester))]
	[ExecuteInEditMode]
	[InitializeOnLoad]
	public class NodeTestAssetInspector : Editor
	{
		protected UI ui = new UI();

		private static Texture2D unknownIcon;
		private static Texture2D warningIcon;
		private static Texture2D okayIcon;
		private static Texture2D failIcon;


		/*public void OnEnable () 
		{
			SceneView.duringSceneGui -= OnSceneGUI; //without subscription works for monobehaviours only (maybe)
			SceneView.duringSceneGui += OnSceneGUI;
		}


		public void OnDisable ()
		{
			SceneView.duringSceneGui -= OnSceneGUI;
		}


		public void OnSceneGUI (SceneView sceneView)
		{
			if (Selection.activeObject != target)
			{
				SceneView.duringSceneGui -= OnSceneGUI;
				return;
			}

			NodeTester tester = (NodeTester)target;

			//arguments
			foreach (Argument arg in tester.inputs)
				if (arg.gizmo.enabled  &&  arg.obj != null)
					UniversalTestAssetInspector.DrawArgumentGizmo(arg, undoObject:tester);

			foreach (Argument arg in tester.results)
				if (arg.gizmo.enabled  &&  arg.obj != null)
					UniversalTestAssetInspector.DrawArgumentGizmo(arg, undoObject:tester);

			//test
			if (tester.updateOnSceneChange)
			{
				UnityEditor.Undo.IncrementCurrentGroup(); //for always update increment group  undo  is enough to prevent lag
				tester.Test();
			}
		}*/


		
		public override void OnInspectorGUI ()
		{
			//DrawDefaultInspector();
			ui.Draw(DrawGUI, inInspector:true);
		}

		private void DrawGUI ()
		//if at some point will like to convert it to Tools.UI
		{
			//init
			NodeTester tester = (NodeTester)target;

			if (ui.undo==null) ui.undo = new GUI.Undo();
			ui.undo.undoObject = tester;

			//icons
			if (unknownIcon == null) unknownIcon = EditorGUIUtility.IconContent("TestNormal").image as Texture2D;
			if (warningIcon == null) warningIcon = EditorGUIUtility.IconContent("console.warnicon.sml").image as Texture2D;
			if (okayIcon == null) okayIcon = EditorGUIUtility.IconContent("TestPassed").image as Texture2D;
			if (failIcon == null) failIcon = EditorGUIUtility.IconContent("TestFailed").image as Texture2D;

			//area
			using (Cell.LinePx(0))
			{
				using (Cell.LineStd) Draw.Label("Active Area:");
				using (Cell.LineStd) Draw.FieldOneLine(ref tester.area.active.worldPos, "World Pos");
				using (Cell.LineStd) Draw.FieldOneLine(ref tester.area.active.worldSize, "World Size");

				int resolution = tester.area.active.rect.size.x;
				using (Cell.LineStd) Draw.Field(ref resolution, "Resolution");

				int margins = tester.area.Margins;
				using (Cell.LineStd) Draw.Field(ref margins, "Margins");

				if (Cell.current.valChanged)
					tester.area = Area.FromActive(tester.area.active.worldPos, tester.area.active.worldSize, resolution, margins);
			}
			using (Cell.LineStd) Draw.Field(ref tester.height, "Height");
			using (Cell.LineStd) Draw.Field(ref tester.seed, "Seed");

			//node mode
			Cell.EmptyLinePx(10);
			using (Cell.LineStd) 
			{
				Cell.current.fieldWidth = 0.66f;

				using (Cell.Row)
				{
					Draw.Field(ref tester.genTypeName, "Node Type");

					if (Cell.current.valChanged)
						tester.RefreshGeneratorType();
				}

				using (Cell.RowPx(20))
				{
					Draw.Icon(tester.genTypeValid ? okayIcon : failIcon);
					if (Draw.Button(visible:false))
						tester.RefreshGeneratorType();
				}
			}

			//inputs
			Cell.EmptyLinePx(10);
			using (Cell.LineStd)
				LayersEditor.DrawLayers(ref tester.inputs, ref tester.guiInputsExpanded, 
				onDraw: (n) => UniversalTestAssetInspector.DrawArgument(tester.inputs[n], tester),
				label:"Inputs");

			//node itself
			Cell.EmptyLinePx(10);
			if (tester.generator != null)
				GeneratorInspector.DrawGenerator(tester.generator);

			//results
			Cell.EmptyLinePx(10);
			using (Cell.LineStd)
				LayersEditor.DrawLayers(ref tester.results, ref tester.guiResultsExpanded, 
				onDraw: (n) => UniversalTestAssetInspector.DrawArgument(tester.results[n], tester),
				label:"Results");

			//assigning ids here since I don't remember how inputs are actually created
			bool anyIdAssigned = false;
			foreach (IUnit unit in tester.generator.AllUnits())
				if (unit.Id == 0)
				{
					unit.Id = Id.Generate();
					anyIdAssigned = true;
				}
			if (anyIdAssigned)
				EditorUtility.SetDirty(tester);

			//test
			Cell.EmptyLinePx(10);
			using (Cell.LineStd)
			{
				Cell.current.trackChange = false; //otherwise will start 2 tests, 1 because of ui change

				if (Draw.Button("Test"))
				{
					//undo recording - otherwise will create lag on each result.obj assignments
					UnityEditor.Undo.IncrementCurrentGroup();
					UnityEditor.Undo.SetCurrentGroupName("NodeTester Test Operation");
					int undoGroup = UnityEditor.Undo.GetCurrentGroup();

					tester.Test();

					UnityEditor.Undo.CollapseUndoOperations(undoGroup);
				}
			}

			using (Cell.LineStd)
				Draw.ToggleLeft(ref tester.updateOnValueChange, "Update on Value Change");
			using (Cell.LineStd)
				Draw.ToggleLeft(ref tester.updateOnSceneChange, "Update on Scene Change");
			if (tester.updateOnValueChange && Cell.current.valChanged)
			{
				UnityEditor.Undo.IncrementCurrentGroup(); //for always update increment group  undo  is enough to prevent lag
				tester.Test();
			}
		}


		public static NodeTester CreateTestFromMapMagic (Generator gen, Graph graph, MapMagic.Core.MapMagicObject mapMagic)
		{
				if (mapMagic == null)
					throw new Exception("No MapMagic found to take area");
		
				NodeTester tester = Den.Tools.Tests.NodeTestAssetInspector.CreateAssetNextTo(graph, gen.GetType().Name + "_" + graph.assetName + "_Test");
				
				tester.genTypeName = gen.GetType().ToString();
				tester.genTypeValid = true; //I hope
				tester.generator = gen;
				tester.area = mapMagic.MainArea(mapMagic.PreviewTile.coord);
				tester.height = mapMagic.globals.height;
				tester.seed = graph.random.seed;

				TileData data = mapMagic.PreviewData;
				if (data == null)
					Debug.LogWarning("Data is null, so no objects will be assigned to test input");

				List<Argument> inputs = new List<Argument>();
				foreach (IInlet<object> inlet in gen.AllInlets())
					inputs.Add(new Argument(Generator.GetGenericType(inlet), data?.ReadInletProduct(inlet), inlet.GuiName));
				tester.inputs = inputs.ToArray();

				List<Argument> outputs = new List<Argument>();
				foreach (IOutlet<object> outlet in gen.AllOutlets())
					outputs.Add(new Argument(Generator.GetGenericType(outlet), null, outlet.GuiName));
				tester.results = outputs.ToArray();

				UnityEditor.AssetDatabase.SaveAssets();
				return tester;
		}


		private static NodeTester CreateAssetNextTo (UnityEngine.Object asset, string name="Test")
		{
				NodeTester tester = ScriptableObject.CreateInstance<NodeTester>();

				string mapMagicPath = UnityEditor.AssetDatabase.GetAssetPath(asset);
				if (string.IsNullOrEmpty(mapMagicPath))
					mapMagicPath = "Assets";

				string dir = System.IO.Path.GetDirectoryName(mapMagicPath);
				string baseName = System.IO.Path.GetFileNameWithoutExtension(mapMagicPath);
				string testerName = $"{name}.asset";
				string testerPath = System.IO.Path.Combine(dir, testerName);

				testerPath = UnityEditor.AssetDatabase.GenerateUniqueAssetPath(testerPath);

				UnityEditor.AssetDatabase.CreateAsset(tester, testerPath);
				UnityEditor.AssetDatabase.SaveAssets();
				UnityEditor.Selection.activeObject = tester;
				UnityEditor.EditorGUIUtility.PingObject(tester);

				return tester;
		}
	}
}