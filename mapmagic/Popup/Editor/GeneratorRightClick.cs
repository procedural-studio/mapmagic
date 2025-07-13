using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
//using UnityEngine.Profiling;
using UnityEditor;

using Den.Tools;
using Den.Tools.GUI;
using Den.Tools.GUI.Popup;

using System.Xml.Linq;


namespace MapMagic.Nodes.GUI
{
	public static class GeneratorRightClick
	{
		public static Graph copiedGenerators;


		public static Item GeneratorItems (Vector2 mousePos, Generator gen, Graph graph, int priority = 3)
		{
			Item genItems = new Item("Node");
			genItems.onDraw = RightClick.DrawItem;
			genItems.icon = RightClick.texturesCache.GetTexture("MapMagic/Popup/Generator");
			genItems.color = RightClick.defaultColor;
			genItems.subItems = new List<Item>();
			genItems.priority = priority;
			genItems.disabled = gen==null  &&  copiedGenerators==null;

			genItems.subItems.Add(EnableDisableItem(gen, graph));
			genItems.subItems.Add(DuplicateItem(gen, graph));
			genItems.subItems.Add(CopyItem(gen, graph));
			genItems.subItems.Add(CutItem(gen, graph));
			genItems.subItems.Add(PasteItem(mousePos, gen, graph));
			genItems.subItems.Add(UpdateItem(gen));
			genItems.subItems.Add(ResetItem(gen));
			genItems.subItems.Add(RemoveItem(gen, graph));
			genItems.subItems.Add(UnlinkItem(gen, graph));
			if (gen != null) genItems.subItems.Add(HelpItem(gen));
			if (gen != null) genItems.subItems.Add(GoToCodeItem(gen));
			#if MM_DEBUG
			if (gen != null) genItems.subItems.Add(CopyProductItem(gen));
			if (gen != null  &&  graph != null) genItems.subItems.Add(CreateTestItem(gen,graph));
			#endif

			return genItems;
		}

		public static Item EnableDisableItem (Generator gen, Graph graph, bool extendedName=false)
		{
			HashSet<Generator> gens;
			if (GraphWindow.current.selected != null && GraphWindow.current.selected.Count != 0)
				gens = GraphWindow.current.selected;
			else
				gens = new HashSet<Generator>() { gen };

			string caption = (gen == null || gen.enabled) ? "Disable" : "Enable";
			if (extendedName  &&  gens != null)
				caption += " node" + (gens.Count > 1 ? "s" : "");

			Item item = new Item(caption, onDraw: RightClick.DrawItem, priority: 11);
			item.icon = RightClick.texturesCache.GetTexture("MapMagic/Popup/Eye");
			item.color = RightClick.defaultColor;
			item.disabled = gen == null;
			item.onClick = () =>
				GraphEditorActions.EnableDisableGenerators(graph, GraphWindow.current.selected, gen);
			return item;
		}

		public static Item DuplicateItem (Generator gen, Graph graph)
		{
			Item item = new Item("Duplicate", onDraw: RightClick.DrawItem, priority: 8);
			item.icon = RightClick.texturesCache.GetTexture("MapMagic/Popup/Duplicate");
			item.color = RightClick.defaultColor;
			item.disabled = gen == null;
			item.onClick = () =>
				GraphEditorActions.DuplicateGenerator(graph, gen, ref GraphWindow.current.selected);
			return item;
		}

		public static Item CopyItem (Generator gen, Graph graph, bool extendedName=false)
		{
			HashSet<Generator> gens;
			if (GraphWindow.current.selected != null && GraphWindow.current.selected.Count != 0)
				gens = GraphWindow.current.selected;
			else
				gens = new HashSet<Generator>() { gen };

			string name = "Copy";
			if (extendedName  &&  gens != null)
				name += " node" + (gens.Count > 1 ? "s" : "");

			Item item = new Item(name, onDraw: RightClick.DrawItem, priority: 8);
			item.icon = RightClick.texturesCache.GetTexture("MapMagic/Popup/Export");
			item.color = RightClick.defaultColor;
			item.disabled = !(gens != null && gens.Count != 0);
			item.onClick = () => copiedGenerators = graph.Export(gens);
			item.closeOnClick = true;
			return item;
		}

		public static Item CutItem (Generator gen, Graph graph, bool extendedName=false)
		{
			HashSet<Generator> gens;
			if (GraphWindow.current.selected != null && GraphWindow.current.selected.Count != 0)
				gens = GraphWindow.current.selected;
			else
				gens = new HashSet<Generator>() { gen };

			string name = "Cut";
			if (extendedName  &&  gens != null)
				name += " node" + (gens.Count > 1 ? "s" : "");

			Item item = new Item(name, onDraw: RightClick.DrawItem, priority: 8);
			item.icon = RightClick.texturesCache.GetTexture("MapMagic/Popup/Export");
			item.color = RightClick.defaultColor;
			item.disabled = !(gens != null && gens.Count != 0);
			item.onClick = () =>
			{
				copiedGenerators = graph.Export(gens);
				GraphEditorActions.RemoveGenerators(graph, gens);
			};
			item.closeOnClick = true;
			return item;
		}

		public static Item PasteItem (Vector2 mousePos, Generator gen, Graph graph, bool extendedName=false)
		{
			string name = "Paste";
			if (extendedName  &&  copiedGenerators != null)
			{
				if (copiedGenerators.groups.Length != 0)
					name = "Paste group";
				else
					name = copiedGenerators.generators.Length>1 ? "Paste nodes" : "Paste node";
			}

			Item item = new Item(name, onDraw: RightClick.DrawItem, priority: 7);
			item.icon = RightClick.texturesCache.GetTexture("MapMagic/Popup/Export");
			item.color = RightClick.defaultColor;
			item.disabled = copiedGenerators == null;
			item.onClick = () =>
			{
				(Generator[] iGens,Auxiliary[] iGrps) imported = graph.Import(GeneratorRightClick.copiedGenerators); 
				Graph.Reposition(imported.iGens, imported.iGrps, mousePos);
			};
			item.closeOnClick = true;
			return item;
		}

		public static Item UpdateItem (Generator gen)
		{
			Item item = new Item("Update", onDraw: RightClick.DrawItem, priority: 7);
			item.icon = RightClick.texturesCache.GetTexture("MapMagic/Popup/Update");
			item.color = RightClick.defaultColor;
			item.closeOnClick = true;
			item.disabled = gen == null;
			return item;
		}

		public static Item ResetItem (Generator gen)
		{
			Item item = new Item("Reset", onDraw: RightClick.DrawItem, priority: 4);
			item.icon = RightClick.texturesCache.GetTexture("MapMagic/Popup/Reset");
			item.color = RightClick.defaultColor;
			item.closeOnClick = true;
			item.disabled = gen == null;
			return item;
		}

		public static Item RemoveItem (Generator gen, Graph graph, bool extendedName=false)
		{
			HashSet<Generator> gens;
			if (GraphWindow.current.selected != null && GraphWindow.current.selected.Count != 0)
				gens = GraphWindow.current.selected;
			else
				gens = new HashSet<Generator>() { gen };

			string name = "Remove";
			if (extendedName  &&  gens != null)
				name += " node" + (gens.Count > 1 ? "s" : "");

			Item item = new Item(name, onDraw: RightClick.DrawItem, priority: 5);
			item.icon = RightClick.texturesCache.GetTexture("MapMagic/Popup/Remove");
			item.color = RightClick.defaultColor;
			item.disabled = gen == null;
			item.onClick = () => GraphEditorActions.RemoveGenerators(graph, gens);
			item.closeOnClick = true;
			return item;
		}

		public static Item UnlinkItem (Generator gen, Graph graph)
		{
			Item item = new Item("Unlink", onDraw: RightClick.DrawItem, priority: 6);
			item.icon = RightClick.texturesCache.GetTexture("MapMagic/Popup/Unlink");
			item.color = RightClick.defaultColor;
			item.disabled = gen == null;
			item.onClick = () =>
			{
				graph.UnlinkGenerator(gen);
				//undo
			};
			item.closeOnClick = true;
			return item;
		}

		public static Item HelpItem (Generator gen)
		{
			Item item = new Item("Help", onDraw: RightClick.DrawItem, priority: 7);
			item.color = RightClick.defaultColor;
			item.onClick = () =>
			{
				GeneratorMenuAttribute att = GeneratorDraw.GetMenuAttribute(gen.GetType());
				if (att.helpLink != null)
					Application.OpenURL(att.helpLink);
			};
			item.closeOnClick = true;
			return item;
		}

		public static Item GoToCodeItem (Generator gen)
		{
			Item item = new Item("GoTo Code", onDraw: RightClick.DrawItem, priority: 8);
			item.color = RightClick.defaultColor;
			item.onClick = () =>
			{
				(string path, int line) = gen.GetCodeFileLine();

				bool found = false;
				if (path != null && !path.Contains("Generator.cs"))
				{
					string file = System.IO.Path.GetFileNameWithoutExtension(path);
					string[] assets = AssetDatabase.FindAssets(file);
					if (assets.Length != 0)
					{
						// Open the file in the editor
					}
				}
			};
			item.closeOnClick = true;
			return item;
		}

		public static Item CopyProductItem (Generator gen)
		{
			Item item = new Item("Copy Product", onDraw: RightClick.DrawItem, priority: 8);
			item.color = RightClick.defaultColor;
			item.onClick = () =>
			{
				if (!(gen is IOutlet<object> outlet))
					return;

				MapMagic.Core.MapMagicObject mapMagic = (MapMagic.Core.MapMagicObject)GraphWindow.current.mapMagic;
				Den.Tools.Tests.UniversalTestAsset.copiedObject = mapMagic.PreviewData.ReadOutletProduct(outlet);
			};
			item.closeOnClick = true;
			return item;
		}

		public static Item CreateTestItem (Generator gen, Graph graph)
		{
			Item item = new Item("Create Test", onDraw: RightClick.DrawItem, priority: 8);
			item.color = RightClick.defaultColor;
			item.onClick = () =>
			{
				MapMagic.Core.MapMagicObject mapMagic = (MapMagic.Core.MapMagicObject)GraphWindow.current.mapMagic;
				NodeTester tester = Den.Tools.Tests.NodeTestAssetInspector.CreateTestFromMapMagic(gen, graph, mapMagic);
			};
			item.closeOnClick = true;
			return item;
		}

		public static Item ValueItem (Graph graph, Generator gen, FieldChArrInfo fieldInfo)
		{
			bool fieldClicked = fieldInfo.field != null;
			bool alreadyInlet = gen != null  &&  gen.fieldInlets.Contains(fieldInfo);

			string caption = alreadyInlet ? "Make Field" : "Make Inlet";
			string textureName = alreadyInlet ? "MapMagic/Popup/UnExpose" : "MapMagic/Popup/Expose";
			Action onClick = alreadyInlet ? 
				() => gen.fieldInlets.ConvertToField(fieldInfo, graph) :
				() => gen.fieldInlets.ConvertToInlet(gen, fieldInfo);

			Item item = new Item(caption, onDraw: RightClick.DrawItem, priority: 8);
			item.icon = RightClick.texturesCache.GetTexture(textureName);
			item.color = RightClick.defaultColor;
			item.disabled = !fieldClicked;
			item.onClick = onClick;
			item.closeOnClick = true;
			return item;
		}
	}
}