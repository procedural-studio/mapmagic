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
using MapMagic.Core;
using MapMagic.Nodes;
using MapMagic.Nodes.GUI;

namespace MapMagic.Nodes.GUI
{
	public static class RightClick
	{
		public static readonly Color defaultColor = new Color(0.5f, 0.5f, 0.5f, 0);	

		private static GUIStyle itemTextStyle;
		public static TexturesCache texturesCache = new TexturesCache(); //to store icons


		public static void DrawRightClickItems (UI ui, Vector2 mousePos, Graph graph)
		{ 
			Item item = RightClickItems(ui, mousePos, graph);

			//#if MM_EXP || UNITY_2020_1_OR_NEWER || UNITY_EDITOR_LINUX
			SingleWindow menu = new SingleWindow(item);
			//#else
			//PopupMenu menu = new PopupMenu() {items=item.subItems, minWidth=150};
			//#endif

			menu.Show(Event.current.mousePosition);
		}


		public static Item RightClickItems (UI ui, Vector2 mousePos, Graph graph)
		{
			ClickedNear (ui, mousePos, 
				out Group clickedGroup, 
				out Generator clickedGen, 
				out IInlet<object> clickedLink, 
				out IInlet<object> clickedInlet, 
				out IOutlet<object> clickedOutlet, 
				out FieldChArrInfo clickedField);

			//clearing selection if clicked to graph
			if (clickedGen==null)
				GraphWindow.current.selected.Clear();

			//selecting current node if clicked to value
			if (clickedField!=null)
			{
				GraphWindow.current.selected.Clear();
				GraphWindow.current.selected.Add(clickedGen);
			}

			Item menu = new Item("Menu");
			menu.subItems = new List<Item>();

			Item createItems;
			if (clickedOutlet != null)
				createItems = CreateRightClick.AppendItems(mousePos, graph, clickedOutlet, priority:5);
			else if (clickedLink != null)
				createItems = CreateRightClick.InsertItems(mousePos, graph, clickedLink, priority:5);
			else
				createItems = CreateRightClick.CreateItems(mousePos, graph, priority:5);
			menu.subItems.Add(createItems);

			//category items
			menu.subItems.Add(Item.Separator());
			Item graphItems = GraphPopup.GraphItems(graph, priority:1);
			Item groupItems = GroupRightClick.GroupItems(mousePos, clickedGroup, graph, priority:2);
			Item generatorItems = GeneratorRightClick.GeneratorItems(mousePos, clickedGen, graph, priority:3);

			//value
			Item valueItem = GeneratorRightClick.ValueItem(graph, clickedGen, clickedField);
			menu.subItems.AddRange(new Item[] {graphItems, groupItems, generatorItems, valueItem});

			//context items
			menu.subItems.Add(Item.Separator());
			if (clickedGen != null)
			{
				menu.subItems.Add( GeneratorRightClick.CopyItem(clickedGen,graph,extendedName:true) );
				menu.subItems.Add( GeneratorRightClick.CutItem(clickedGen,graph,extendedName:true) );
				menu.subItems.Add( GeneratorRightClick.PasteItem(mousePos, clickedGen,graph,extendedName:true) );
				//menu.subItems.Add( GeneratorRightClick.RenameItem(clickedGen,graph) );
				menu.subItems.Add( GroupRightClick.GroupSelectedItem(mousePos,graph) );
				menu.subItems.Add( GeneratorRightClick.EnableDisableItem(clickedGen,graph,extendedName:true) );
				menu.subItems.Add( GeneratorRightClick.RemoveItem(clickedGen,graph,extendedName:true) );
			}
			else if (clickedGroup != null)
			{
				menu.subItems.Add( GroupRightClick.CopyItem(clickedGroup,graph,extendedName:true) );
				menu.subItems.Add( GroupRightClick.CutItem(clickedGroup,graph,extendedName:true) );
				menu.subItems.Add( GeneratorRightClick.PasteItem(mousePos, clickedGen, graph, extendedName:true) );
				menu.subItems.Add( GroupRightClick.UngroupItem(clickedGroup,graph) );
				menu.subItems.Add( GroupRightClick.RemoveGroupItem(clickedGroup,graph,extendedName:true) );
			}
			else //graph clicked
			{
				menu.subItems.Add( GeneratorRightClick.PasteItem(mousePos, clickedGen, graph, extendedName:true) );
			}

			/*if (clickedExpose != null)
				valueItems.initial = true;
			else if (clickedGen != null)
				generatorItems.initial = true;
			else if (clickedGroup != null)
				groupItems.initial = true;*/
				

			

			return menu;
		}


		public static bool ClickedNear (UI ui, Vector2 mousePos, 
			out Group clickedGroup, 
			out Generator clickedGen, 
			out IInlet<object> clickedLink,
			out IInlet<object> clickedInlet, 
			out IOutlet<object> clickedOutlet, 
			out FieldChArrInfo clickedField)
		/// Returns the top clicked object (or null) in clickedGroup-to-clickedField priority
		{
			Graph graph = GraphWindow.current.graph;
		
			clickedGen = null;
			foreach (Generator gen in graph.generators)
			{
				Rect rect = new Rect(gen.guiPosition, gen.guiSize);
				if (rect.Contains(UI.current.mousePos))
					clickedGen = gen; //don't break, use the last one
			}

			clickedGroup = null;
			foreach (Group group in graph.groups)
			{
				Rect rect = new Rect(group.guiPos, group.guiSize);
				if (rect.Contains(UI.current.mousePos))
					clickedGroup = group; //don't break, use the last one
			}

			clickedInlet = null; 
			foreach (var kvp in GraphWindow.current.inletsPosCache)
			{
				Vector2 pos = kvp.Value;
				Rect rect = new Rect(
					pos.x - GeneratorDraw.inletOutletCellWidth*2, 
					pos.y - GeneratorDraw.inletOutletCellWidth*2,
					GeneratorDraw.inletOutletCellWidth,
					GeneratorDraw.inletOutletCellWidth);
				if (rect.Contains(UI.current.mousePos))
					clickedInlet = kvp.Key; //don't break, use the last one
			}

			clickedOutlet = null; 
			foreach (var kvp in GraphWindow.current.outletsPosCache)
			{
				Vector2 pos = kvp.Value;
				Rect rect = new Rect(
					pos.x - GeneratorDraw.inletOutletCellWidth*2, 
					pos.y - GeneratorDraw.inletOutletCellWidth*2,
					GeneratorDraw.inletOutletCellWidth,
					GeneratorDraw.inletOutletCellWidth);
				if (rect.Contains(UI.current.mousePos))
					clickedOutlet = kvp.Key; //don't break, use the last one
			}

			clickedField = new FieldChArrInfo();
			foreach ((Rect rect, FieldChArrInfo field) in GraphWindow.current.fieldRectsCache)
			{
				if (rect.Contains(UI.current.mousePos))
					clickedField = field;
			}

			//checking links
			clickedLink = clickedInlet; //TODO: theoretically it should be a line
			float minDist = 10;
			//GeneratorDraw.DistToLink(mousePos, kvp.Value, kvp.Key);


			return clickedGroup != null ||  clickedGen != null || clickedLink != null || clickedInlet != null || clickedOutlet != null || clickedField != null;
		}


		public static object ClickedOn (UI ui, Vector2 mousePos)
		/// Returns the top clicked object (or null) in clickedGroup-to-clickedField priority
		{
			ClickedNear (ui, mousePos, 
				out Group clickedGroup, out Generator clickedGen, out IInlet<object> clickedLink, out IInlet<object> clickedInlet, out IOutlet<object> clickedOutlet, out FieldChArrInfo clickedField);

			if (clickedField != null) return clickedField;
			if (clickedOutlet != null) return clickedOutlet;
			if (clickedInlet != null) return clickedInlet;
			if (clickedLink != null) return clickedLink;
			if (clickedGen != null) return clickedGen;
			if (clickedGroup != null) return clickedGroup;

			return null;
		}


		public static void DrawItem (Item item, Rect rect)
		{
			Rect leftRect = new Rect(rect.x, rect.y, 28, rect.height);
			leftRect.x -= 1; leftRect.height += 2;
			item.color.a = 0.25f;
			EditorGUI.DrawRect(leftRect, item.color);

			Rect labelRect = new Rect(rect.x+leftRect.width+3, rect.y, rect.width-leftRect.width-3, rect.height);

			if (itemTextStyle == null)
			{
				itemTextStyle = new GUIStyle(UnityEditor.EditorStyles.label); 
				itemTextStyle.normal.textColor = itemTextStyle.focused.textColor = itemTextStyle.active.textColor = Color.black;
			}

			EditorGUI.LabelField(labelRect, item.name, itemTextStyle);

			if (item.icon!=null) 
			{
				Rect iconRect = new Rect(leftRect.center.x-6, leftRect.center.y-6, 12,12);
				iconRect.y -= 2;
				UnityEngine.GUI.DrawTexture(iconRect, item.icon);
			}
		}


		public static void DrawSeparator (Item item, Rect rect)
		{
			Rect leftRect = new Rect(rect.x, rect.y, 28, rect.height);
			leftRect.x -= 1; leftRect.height += 2;
			item.color.a = 0.125f;
			EditorGUI.DrawRect(leftRect, item.color);

			Rect labelRect = new Rect(rect.x+leftRect.width+3, rect.y, rect.width-leftRect.width-3, rect.height);
			Rect separatorRect = new Rect(labelRect.x, labelRect.y+2, labelRect.width-6, 1);
			EditorGUI.DrawRect(separatorRect, new Color(0.3f, 0.3f, 0.3f, 1));
		}
	}
}