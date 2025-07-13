using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

using Den.Tools;
using Den.Tools.GUI;
using System.Drawing;
using Den.Tools.SceneEdit;


namespace MapMagic.Nodes.GUI
{
	public static class GroupDraw
	{
		private static Generator[] draggedGroupNodes;
		private static Auxiliary[] draggedGroups;
		private static Vector2[] initialGroupNodesPos;
		private static Vector2[] initialGroupGroupsPos;
		private static GUIStyle miniNameStyle;
		private static GUIStyle commentStyle;
		private static GUIStyle commentMiniStyle;
		private static GUIStyle groupStyle;
		private static GUIStyle groupMiniStyle;
		private static GUIStyle groupSrcStyle;
		private const float groupTextureScale = 0.4f;  //0.5 for dpi and 0.5 to adjust corner roundness

		public static void DragGroup (Group group, Generator[] allGens, Auxiliary[] allAux)
		{
			GraphWindow graphWindow = GraphWindow.current;
			
			if (UI.current.layout || group.pinned)
				return;

				if (DragDrop.TryDrag(group, UI.current.mousePos)  &&  DragDrop.totalDelta.magnitude >= 2) //oif drag actually done (more than pixel))
				{
					for (int i=0; i<draggedGroupNodes.Length; i++)
					{
						Generator gen = draggedGroupNodes[i];

						gen.guiPosition = initialGroupNodesPos[i] + DragDrop.totalDelta;
						gen.guiPosition = GeneratorDraw.RefinePosition(gen.guiPosition);
						GeneratorDraw.UpdateGeneratorPosition(gen, graphWindow.genCellsCache, graphWindow.inletsPosCache, graphWindow.outletsPosCache);
					}

					for (int i=0; i<draggedGroups.Length; i++)
					{
						Auxiliary aux = draggedGroups[i];

						aux.guiPos = initialGroupGroupsPos[i] + DragDrop.totalDelta;

						//re-layouting cells
						Cell cell = graphWindow.groupCellsCache[aux];
						cell.worldPosition = UI.current.scrollZoom.RoundToPixels( aux.guiPos );
						cell.CalculateSubRects();
					}

					group.guiPos = DragDrop.initialRect.position + DragDrop.totalDelta;
					Cell.current.worldPosition = group.guiPos;
					Cell.current.CalculateSubRects();
				}

				if (DragDrop.TryRelease(group, UI.current.mousePos))
				{
					draggedGroupNodes = null;
					initialGroupNodesPos = null;

					draggedGroups = null;
					initialGroupGroupsPos = null;

					#if UNITY_EDITOR //this should be an editor script, but it doesnt mentioned anywhere
					UnityEditor.EditorUtility.SetDirty(GraphWindow.current.graph);
					#endif
				}

				if (DragDrop.TryStart(group, UI.current.mousePos, Cell.current.InternalRect))
				{
					draggedGroupNodes = GraphWindow.current.graph.GetGroupGenerators(group).ToArray();
					draggedGroups = GraphWindow.current.graph.GetGroupAux(group).ToArray();
					
					initialGroupNodesPos = new Vector2[draggedGroupNodes.Length];
					for (int i=0; i<draggedGroupNodes.Length; i++)
						initialGroupNodesPos[i] = draggedGroupNodes[i].guiPosition;

					initialGroupGroupsPos = new Vector2[draggedGroups.Length];
					for (int i=0; i<draggedGroups.Length; i++)
						initialGroupGroupsPos[i] = draggedGroups[i].guiPos;
				}

				Rect cellRect = Cell.current.InternalRect;
				if (DragDrop.ResizeRect(group, UI.current.mousePos, ref cellRect, minSize:new Vector2(100,100)))
				{
					group.guiPos = cellRect.position;
					group.guiSize = cellRect.size;

					Cell.current.InternalRect = cellRect;
					Cell.current.CalculateSubRects();
				}
		}

		private static void MoveGroup (Group group)
		{

		}


		public static void DrawGroup (Group group)
		{
			bool isScheme = UI.current.scrollZoom.zoom.x < 0.4f;
			bool superScheme = UI.current.scrollZoom.zoom.x < 0.2f;

			//background
			Texture2D groupTex = UI.current.textures.GetTexture("MapMagic/RoundedNode/Group");
			Draw.Element(groupTex, new Vector4(20, 20, 20, 20), color:group.color, scale:isScheme ? groupTextureScale*2 : groupTextureScale);

			//text style
			if (!isScheme && groupStyle == null)
			{
				groupSrcStyle = new GUIStyle(UI.current.styles.bigLabel);
				groupSrcStyle.fontSize = 16;
				groupStyle = new GUIStyle(groupSrcStyle);
				groupStyle.alignment = TextAnchor.UpperLeft;
				UI.current.styles.AddStyleToResize(groupStyle);
			}

			if (isScheme && groupMiniStyle == null)
			{
				groupMiniStyle = new GUIStyle(UI.current.styles.bigLabel);
				groupMiniStyle.fontSize = 7; //(int)(16*0.8f);
				groupMiniStyle.alignment = TextAnchor.UpperLeft;
				//UI.current.styles.AddStyleToResize(commentStyle); //not resizing mini style
			}

			//contents
			using (Cell.Padded(4))
				using (Cell.Line)
				{
					GUIStyle labelStyle = !isScheme ? groupStyle : groupMiniStyle;
					group.name = Draw.EditableLabelText(group.name, style:labelStyle, multiline:true);
				}

				//DrawContents(group, groupStyle, groupMiniStyle, groupSrcStyle, isScheme, additionalLabelHeight:8);
		}


		public static void DrawComment (Comment group)
		{
			bool isScheme = UI.current.scrollZoom.zoom.x < 0.4f;
			bool superScheme = UI.current.scrollZoom.zoom.x < 0.2f;

			//background
			Texture2D commentTex = UI.current.textures.GetTexture("MapMagic/RoundedNode/Comment");
			Draw.Element(commentTex, 
				borders: new Vector4(4,40,10,40),
				//overflow: new Vector4(3,9,9,5),
				color:group.color, scale:UI.current.scrollZoom.SoftZoom);

			//text style
			if (!isScheme && commentStyle == null)
			{
				commentStyle = new GUIStyle(UnityEditor.EditorStyles.wordWrappedLabel);
				commentStyle.fontSize = StylesCache.defaultFontSize;
				commentStyle.alignment = TextAnchor.UpperLeft;
				UI.current.styles.AddStyleToResize(commentStyle);
			}

			if (isScheme && commentMiniStyle == null)
			{
				commentMiniStyle = new GUIStyle(UI.current.styles.labelWordWrap);
				commentMiniStyle.fontSize = 6;
				commentMiniStyle.alignment = TextAnchor.UpperLeft;
				//UI.current.styles.AddStyleToResize(commentStyle); //not resizing mini style
			}

			//contents
			using (Cell.Padded(10,4,4,15))
				using (Cell.Line)
				{
					GUIStyle labelStyle = !isScheme ? groupStyle : groupMiniStyle;
					group.name = Draw.EditableLabelText(group.name, style:labelStyle, multiline:true);
				}
			//DrawContents(group, commentStyle, commentMiniStyle, UnityEditor.EditorStyles.wordWrappedLabel, isScheme, additionalLabelHeight:2);
		}


		/*private static void DrawContents (Auxiliary group, GUIStyle fullStyle, GUIStyle miniStyle, GUIStyle srcStyle, bool isMini=false, int additionalLabelHeight=2)
		{
			Cell labelCellParent;
			using (Cell.LineStd)
			{
				GUIStyle labelStyle = !isMini ? fullStyle : miniStyle;

				float labelHeight = labelStyle.CalcHeight(new GUIContent(group.name),  Cell.current.InternalRect.width*UI.current.scrollZoom.zoom.x - 4) / UI.current.scrollZoom.zoom.y;

				labelCellParent = Cell.current;

				using (Cell.LinePx(labelHeight+additionalLabelHeight)) 
					group.name = Draw.EditableLabelText(group.name, style:labelStyle);
			}

			//Cell.EmptyLinePx(spaceFromLabelToButtons);

			using (Cell.LinePx(18))
			{
				Cell.EmptyRowPx(4);

				using (Cell.RowPx(18)) 
				{
					//Cell.EmptyRowPx(3); //hacky offset
					//using (Cell.RowPx(18))
						Draw.EditableLabelButton(UI.current.textures.GetTexture("DPUI/Icons/Edit"), labelCellParent:labelCellParent, iconScale:0.5f);
				}

				using (Cell.RowPx(18))
				if (Draw.Button("", icon: UI.current.textures.GetTexture("DPUI/Icons/Pallete"), visible: false, iconScale: 0.5f))
					GroupRightClick.DrawGroupColorSelector(group);

				using (Cell.RowPx(18))
					Draw.CheckButton(ref group.pinned, icon: UI.current.textures.GetTexture("MapMagic/Icons/PinDraft"));
			}

			Cell.EmptyLine();

			Cell.EmptyLinePx(4);
		}*/



		public static void ResizeComment (Comment comment)
		{
			if (!UI.current.layout)
			{
				if (DragDrop.TryDrag(comment, UI.current.mousePos))
				{
					comment.guiPos = DragDrop.initialRect.position + DragDrop.totalDelta;
					Cell.current.worldPosition = comment.guiPos;
					Cell.current.CalculateSubRects();
				}

				if (DragDrop.TryRelease(comment, UI.current.mousePos))
				{
					draggedGroupNodes = null;
					initialGroupNodesPos = null;

					#if UNITY_EDITOR //this should be an editor script, but it doesnt mentioned anywhere
					UnityEditor.EditorUtility.SetDirty(GraphWindow.current.graph);
					#endif
				}

				DragDrop.TryStart(comment, UI.current.mousePos, Cell.current.InternalRect);

				Rect cellRect = Cell.current.InternalRect;
				//cellRect = cellRect.Extended(-4, -12, -2, -12);
				if (DragDrop.ResizeRect(comment, UI.current.mousePos, ref cellRect, minSize:new Vector2(60,60), additionalBottomRightMargin:new RectOffset(4,0,4,0)))
				{
					//cellRect = cellRect.Extended(4, 12, 2, 12);

					comment.guiPos = cellRect.position;
					comment.guiSize = cellRect.size;

					Cell.current.InternalRect = cellRect;
					Cell.current.CalculateSubRects();
				}
			}
		}
	}
}