using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

using Den.Tools;
using Den.Tools.GUI;
using Den.Tools.Matrices;

using MapMagic.Core;  //used once to get tile size
using MapMagic.Products;

using UnityEditor;
using Den.Tools.SceneEdit;
using UnityEngine.UIElements;


namespace MapMagic.Nodes.GUI
{
	public static partial class GeneratorDraw
	{
		public const int nodeWidth = 150; //initial width on creation
		public const int minNodeWidth = 100;
		public const int minPortalWidth = 50;
		public const int miniWidth = 140;
		public const int headerHeight = 24;
		public const float nodeTextureScale = 0.4f; //0.5 for dpi and 0.5 to adjust corner roundness
		public const int inletOutletDragArea = 20;
		public const int inletOutletCellWidth = 4; //to make a cell be a bit inside the node
		public const int inletOutletLineOffset = 5; //where the line starts exactly relatively to inlet/outlet center
		public static readonly RectOffset shadowBorders = new RectOffset(38,38,38,38);
		public static readonly RectOffset shadowOverflow = new RectOffset(32, 32, 32, 32);
		public static readonly RectOffset frameBorders = new RectOffset(1, 1, 1, 1);
		public static readonly RectOffset selectionBorders = new RectOffset(2, 2, 2, 2);
		public const int portalHeight = 30;

		[Obsolete] private const float miniInletsScale = 0.875f / 0.375f;
		[Obsolete] private const float miniInletLineHeight = 12 / 0.375f;
		private static GUIStyle schemeNameLabelStyle;
		private static GUIStyle schemeInletLabelStyle;
		private static GUIStyle schemeOutletLabelStyle;

		private static Material nodeMat;
		private static Material shadowMat;
		private static Material collapsedMat;
		private static Material portalInMat;
		private static Material portalOutMat;
		private static Texture2D nodeTex; //can use texture cache
		private static Texture2D collapsedTex;
		private static Texture2D portalInTex;
		private static Texture2D portalOutTex;
		private static Mesh nodeMesh;
		private static int[] nodeIds;
		private static int[] collapsedIds;

		public static float tempShadowSize = 70; //TODO: remove
		public static float tempShadowOffset = 15; 
		public static float tempShadowFill = 0.25f; 
		public static float tempShadowGamma = 3f; 
		public static float tempShadowOpacity = 0.3f;

		private static Dictionary<IInlet<object>,Cell> inletsDrawn = new Dictionary<IInlet<object>,Cell>(); //together with cells to collapse for those that drawn


		public static void DrawGenerator (Generator gen, Graph graph, bool isSelected)
		{
			//using cached cell
			Cell cell = GraphWindow.current.genCellsCache[gen]; //should be already added on update cache

			//drawing gen texture if needed and possible
			bool drawAsTexNeeded = UI.current.scrollZoom.isScrolling  ||
				(DragDrop.IsDragging<Generator>()  &&  !isSelected);
			bool drawAsTexPossible = GraphWindow.current.genTextures.TryGetValue(gen, out (Texture2D t,Rect r) texRect)  &&
				cell.InternalRect.size.x > 0.1f  &&  cell.InternalRect.size.y > 0.1f  &&
				Event.current.type != EventType.MouseDown;  //if DAT on mouse down control (field) can't be selected

			if (drawAsTexNeeded && drawAsTexPossible)
				GeneratorDraw.DrawAsTexture(gen, cell, isSelected, texRect.t);

			//drawing generator  regular way (and rendering to texture)
			else
				using (Cell.Cached(cell, gen.guiPosition.x, gen.guiPosition.y, gen.guiSize.x, 0))
				{
					Type genType = gen.GetType();
					GeneratorMenuAttribute menuAtt = GeneratorDraw.GetMenuAttribute(genType);

					#if !MM_DEBUG
					try 
					{ 
					#endif
						Vector2 newSize = GeneratorDraw.ResizeGeneraror(gen);

						if (!menuAtt.lookLikePortal)
							GeneratorDraw.DrawStandard(gen, graph, selected:isSelected); 
						else
							GeneratorDraw.DrawPortal(gen, graph, selected:isSelected);

						GeneratorDraw.ResizeGeneratorMenus(gen, newSize.y);
					#if !MM_DEBUG
					}
					catch (ExitGUIException)
						{ } //ignoring
					catch (Exception e) 
						{ throw e; } 
					#endif

					//rendering drawn generator to texture
					if (Event.current.type == EventType.Repaint  &&  !UI.current.layout)
						GeneratorDraw.TryRenderGenToTexture(gen, cell, GraphWindow.current.genTextures);
					
				}
		}



		public static void DrawStandard (Generator gen, Graph graph, bool selected=false, bool activeLinks=true, GeneratorMenuAttribute menuAtt=null)
			{using(ProfilerExt.Profile("DrawStandardGenerator"))
		{
			if (menuAtt == null)
			{
				Type genType = gen.GetType(); //TODO: cache types? or attributes?
				menuAtt = GetMenuAttribute(genType);
			}

			bool collapsed = gen.GuiCollapsed;
			bool isScheme = UI.current.scrollZoom.zoom.x < 0.4f;
			bool superScheme = UI.current.scrollZoom.zoom.x < 0.2f;


			Color color = GetGeneratorColor(gen, menuAtt);
			color.a = 1;

			//shadow
			if (!UI.current.layout)
				DrawShadow(selected);

			//background
			if (!UI.current.layout)
				using(ProfilerExt.Profile("Background"))
				{
					Texture2D collapsedTex = UI.current.textures.GetTexture("MapMagic/Node/Collapsed");
					Draw.Element(collapsedTex, new Vector4(10, 10, 10, 10), color:color, scale:isScheme ? nodeTextureScale*2 : nodeTextureScale);

					if (collapsed  ||  isScheme)
					{

					}

					else //drawing background
					{
						//Draw.Rect(new Color(0.219f, 0.219f, 0.219f, 1));

					}
				}

			//clearing inlets/outlets bools
			inletsDrawn.Clear();

			#region Header
			using(ProfilerExt.Profile("Header"))
				using (Cell.LinePx(0)) 
				{
					//background
					if (!collapsed  &&  !isScheme  && !UI.current.layout)  //do not display header and footer background for collapsed or scheme
					{
//						Texture2D headerTex = UI.current.textures.GetTexture("MapMagic/Node/Header");
//						//Draw.Element(headerTex, new Vector4(10, 1, 10, 10), color:color, scale:nodeTextureScale); //shadow:34, corners:22, header:60, footer:30
					}

					//styles
					if (schemeNameLabelStyle == null)
					{
						schemeNameLabelStyle = new GUIStyle(UI.current.styles.bigLabel);
						schemeNameLabelStyle.fontSize = 24;
						schemeNameLabelStyle.alignment = TextAnchor.LowerCenter;
						UI.current.styles.AddStyleToResize(schemeNameLabelStyle);
					}
					if (schemeInletLabelStyle == null)
					{
						schemeInletLabelStyle = new GUIStyle(UI.current.styles.label);
						schemeInletLabelStyle.fontSize = 20;
						UI.current.styles.AddStyleToResize(schemeInletLabelStyle);
					}
					if (schemeOutletLabelStyle == null)
					{
						schemeOutletLabelStyle = new GUIStyle(UI.current.styles.rightLabel);
						schemeOutletLabelStyle.fontSize = 20;
						UI.current.styles.AddStyleToResize(schemeOutletLabelStyle);
					}
					GUIStyle inletLabelStyle = isScheme ? schemeInletLabelStyle : UI.current.styles.label;
					GUIStyle outletLabelStyle = isScheme ? schemeOutletLabelStyle : UI.current.styles.rightLabel;


					//superescheme view
					if (superScheme)
					{
						using (Cell.Center(100,100))
							Draw.Icon(menuAtt.icon, scale:2f); //in the whole cell

						if (GraphWindow.current.inletsPosCache != null)
						{
							foreach (IInlet<object> inlet in gen.AllInlets())
								GraphWindow.current.inletsPosCache.ForceAdd(inlet, Cell.current.InternalLeftEdge);
						}

						if (GraphWindow.current.outletsPosCache != null)
						{
							foreach (IOutlet<object> outlet in gen.AllOutlets())
								GraphWindow.current.outletsPosCache.ForceAdd(outlet, Cell.current.InternalRightEdge);
						}
					}


					//scheme view
					else if (isScheme)
						using (Cell.LinePx(0))
					{
						//inlet
						if (gen is IInlet<object> inletGen)
							using (Cell.RowPx(inletOutletCellWidth)) 
							{
								using (Cell.LinePx(60)) //drawing on top
									DrawInlet(inletGen, addCellObj:activeLinks);
								Cell.EmptyLine();
							}

						using (Cell.Row)
						{
							//icon
							if (!collapsed)
								using (Cell.LinePx(60)) Draw.Icon(menuAtt.icon, scale:1f);

							//label
							using (Cell.LinePx(40))
							{
								Cell.EmptyRowPx(10); //to clamp with background
								using (Cell.Row) Draw.Label(menuAtt.nameUpper, style:schemeNameLabelStyle);
								Cell.EmptyRowPx(10); 
							}

							Cell.EmptyLinePx(10);
						}

						//outlet
						if (gen is IOutlet<object> outletGen)
							using (Cell.RowPx(inletOutletCellWidth)) 
							{
								using (Cell.LinePx(60))
									DrawOutlet(outletGen);
								Cell.EmptyLine();
							}
					}


					//standard header
					else
					{
						Cell.EmptyLinePx(7);

						using (Cell.LinePx(20)) //one line
						{
							//inlet
							if (gen is IInlet<object> inletGen)
							{
								using (Cell.RowPx(inletOutletCellWidth)) 
									DrawInlet(inletGen, addCellObj:activeLinks);
							}

							Cell.EmptyRowPx(10);

							//icon
							using (Cell.RowPx(24)) Draw.Icon(menuAtt.icon, scale:0.5f);

							//label
							bool nameFit = true;
							using (Cell.Row) 
							{
								string label = menuAtt.nameUpper;
								if (gen.guiName != null  &&  gen.guiName.Length != 0)
									label = gen.guiName;

								gen.guiName = Draw.EditableLabelText(label, style:UI.current.styles.bigLabel);

								if (Cell.current.valChanged)
									gen.version++;

								if (!UI.current.layout  &&  menuAtt.nameWidth > Cell.current.InternalRect.width)
									nameFit = false;
							}

							using (Cell.RowPx(25)) 
							{
								if (!nameFit)
									Draw.Label("...", style:UI.current.styles.bigLabel);
							}

							//outlet
							if (gen is IOutlet<object> outletGen)
							{
								using (Cell.RowPx(inletOutletCellWidth)) 
									DrawOutlet(outletGen);
							}
						}

						Cell.EmptyLinePx(7);
					}


					//finding if we need to draw inlets/outlets in header
					List<IInlet<object>> inletsInHeader = null; //for most  generators will remain null
					List<IOutlet<object>> outletsInHeader = null;

					if (!superScheme) //don't draw inlets/outlets in header in superscheme
					{
						//inlets
						foreach (IInlet<object> inlet in gen.AllInlets())
						{
							if (inlet == gen)
								continue; //we draw it with label

							if (inlet is FieldInlets.MathInletField)
							{
								if (gen.guiField  &&  !isScheme) 
									continue; //we draw math inlets in field if it's visible
							}

							else if (!menuAtt.drawInlets  &&  gen.guiField  &&  !isScheme)
								continue; //if we draw inlets in field and it is visible

							if (inletsInHeader == null)
								inletsInHeader = new List<IInlet<object>>();
							inletsInHeader.Add(inlet);
						}

						//outlets
						foreach (IOutlet<object> outlet in gen.AllOutlets())
						{
							if (outlet == gen)
								continue; //we draw it with label

							if (!menuAtt.drawOutlet  &&  gen.guiField  &&  !isScheme)
								continue; //if we draw outlets in field and it is visible

							if (outletsInHeader == null)
								outletsInHeader = new List<IOutlet<object>>();
							outletsInHeader.Add(outlet);
						}
					}


					//inlets/outlets
					//drawing inlets and outlets that have no
					//in scheme view too, but not superscheme 
					if (inletsInHeader != null  ||  outletsInHeader != null)
					{
						//space in scheme view
						if (isScheme)
							Cell.EmptyLinePx(10);

						float inletOutletLineHeight = isScheme ? Cell.lineHeight*1.5f : Cell.lineHeight;

						using (Cell.LinePx(0))
						{
							if (inletsInHeader != null)
								using (Cell.Row)
								{
									foreach (IInlet<object> inlet in inletsInHeader)
									{
										using (Cell.LinePx(inletOutletLineHeight))
										{
											using (Cell.RowPx(inletOutletCellWidth))
												DrawInlet(inlet, addCellObj:activeLinks); 
											
											Cell.EmptyRowPx(10);
											using (Cell.Row) Draw.Label(inlet.GuiName, style:inletLabelStyle); 
										}
									}
								}

							if (outletsInHeader != null)
								using (Cell.Row)
								{
									foreach (IOutlet<object> outlet in outletsInHeader)
									{
										using (Cell.LinePx(inletOutletLineHeight))
										{							
											using (Cell.Row) Draw.Label(outlet.GuiName, style:outletLabelStyle);
											Cell.EmptyRowPx(12);

											using (Cell.RowPx(inletOutletCellWidth))
												DrawOutlet(outlet); 
										}
									}
								}
						}

						//layers for collapsed or scheme (mini) view
						/*if ((collapsed || isScheme)  &&  gen is IMultiLayer mlayGen)
						{
							if (isScheme)
								Cell.EmptyLinePx(10);

							IList<IUnit> layers = mlayGen.UnitLayers;
							int layersCount = layers.Count;

							//drawing layers
							for (int i=0; i<layersCount; i++)
							{
								int ii = !mlayGen.Inversed ?  i  :  layersCount-1 - i;
								IUnit layer = layers[ii];

								if (layer is IInlet<object>  ||  layer is IOutlet<object>)
									using (Cell.LinePx(inletOutletLineHeight))
									{
										//inlets
										if (layer is IInlet<object> inlet  &&  drawInlets)
											using (Cell.RowPx(inletOutletCellWidth)) //keeping cell offset even if not drawing inlet
											{
												if (!(ii==0 && mlayGen.HideFirst))
													DrawInlet(inlet);
											}
								
										Cell.EmptyRowPx(14);

										//layer name if possible
										string name = null;
										if (layer is IBiome biome  &&  biome.SubGraph != null) name = biome.SubGraph.name;
										if (layer is MatrixGenerators.BaseTextureLayer texLayer) name = texLayer.guiName;

										using (Cell.Row)
											if (name != null)
												Draw.Label(name, inletLabelStyle);

										Cell.EmptyRowPx(14);

										//outlet
										if (layer is IOutlet<object> outlet  &&  drawOutlets)
											using (Cell.RowPx(inletOutletCellWidth)) DrawOutlet(outlet); 
									}
							}
						}*/
					}
				}
			#endregion


			#region Field
			using(ProfilerExt.Profile("Field"))
				using (Cell.LinePx(0))
				{
					//background
					if (!collapsed)
					{
						Texture2D fieldTex = UI.current.textures.GetTexture("MapMagic/Node/Field");
						Draw.Element(fieldTex, new Vector4(10, 10, 10, 10), scale:nodeTextureScale); 
					}


					if (gen.guiField  &&  !isScheme)
						using(ProfilerExt.Profile("Values"))
							using (Cell.LinePx(0)) 
							{
								Cell.EmptyLinePx(4); //top padding

								using (Cell.LinePx(0))
								{
									Cell.current.fieldWidth = 0.44f;

									int leftPadding = 4;
									if (gen.fieldInlets.HasFieldInlets)
										leftPadding = 12;
									Cell.EmptyRowPx(leftPadding); //left padding

									using (Cell.Row) 
										DrawField(gen, menuAtt.advancedOptions);

									Cell.EmptyRowPx(4); //right padding
								}

								using(ProfilerExt.Profile("Custom Editor"))
									using (Cell.LinePx(0))
										Draw.Editor(gen);

								Cell.EmptyLinePx(4); //bottom padding

								if (Cell.current.valChanged)
									using(ProfilerExt.Profile("ValCahnged"))
								{
									gen.version ++;

									#if MM_DEBUG
									Log.Add("DrawGenerator ValueChanged", idName:null, ("gen ver:",gen.version), ("graph ver:", graph.IdsVersions()));
									#endif

									GraphWindow.current?.RefreshMapMagic();
								}

								if (!UI.current.layout)
									gen.guiFieldHeight = Cell.current.InternalRect.height;
							}


					if (gen.guiAdvanced  &&  !isScheme)
						using(ProfilerExt.Profile("Advanced"))
							using (Cell.LinePx(0)) 
							{
								//background
								if (!UI.current.layout)
									Draw.Rect(new Color(0.219f, 0.219f, 0.219f, 1));

								if (!UI.current.layout)
									gen.guiAdvancedHeight = Cell.current.InternalRect.height;
							}


					#if MM_DEBUG
					if (gen.guiDebug  &&  !isScheme)
						using(ProfilerExt.Profile("Debug"))
							using (Cell.LinePx(0)) 
							{
								//background
								if (!UI.current.layout)
									Draw.Rect(new Color(0.219f, 0.219f, 0.219f, 1));

								DrawDebug(gen);
							}
					#endif


					if (gen is IOutlet<object> &&  gen.guiPreview  &&  !isScheme)
						using(ProfilerExt.Profile("Preview"))
							using (Cell.LinePx(gen.guiSize.x))
								using (Cell.Padded(1,1,0,0))
									Previews.PreviewDraw.DrawPreview((IOutlet<object>)gen);


					if (!isScheme)
						using(ProfilerExt.Profile("Footer"))
							using (Cell.LinePx(0))
							{
								using (Cell.LinePx(18))
								{
									Cell.EmptyRow();

									using (Cell.RowPx(25))
										Draw.CheckButton(ref gen.guiField, 
											iconOn: UI.current.textures.GetTexture("MapMagic/Icons/FieldsIcon_enabled"), 
											iconOff: UI.current.textures.GetTexture("MapMagic/Icons/FieldsIcon_disabled"),
											iconScale:0.5f, visible:false);

									if (gen is IOutlet<object>)
										using (Cell.RowPx(25))
											Draw.CheckButton(ref gen.guiPreview, 
												iconOn: UI.current.textures.GetTexture("MapMagic/Icons/PreviewIcon_enabled"), 
												iconOff: UI.current.textures.GetTexture("MapMagic/Icons/PreviewIcon_disabled"),
												iconScale:0.5f, visible:false);

									using (Cell.RowPx(25))
										Draw.CheckButton(ref gen.guiDebug, 
										iconOn: UI.current.textures.GetTexture("MapMagic/Icons/InfoIcon_enabled"), 
										iconOff: UI.current.textures.GetTexture("MapMagic/Icons/InfoIcon_disabled"), 
										iconScale:0.5f, visible:false);

									Cell.EmptyRow();
								}

								
							}
				}
			#endregion

			//bottom line
			Cell.EmptyLinePx(4); 


			#region Selection Frame - after node but before inletsOutlets

				if (selected)
				{
					Color frameColor = Color.white; //selected ? Color.white : Color.black;
					Texture2D frameTex = UI.current.textures.GetTexture("MapMagic/Node/Frame");
					Draw.Element(frameTex, new Vector4(9, 9, 9, 9), color: frameColor, scale: isScheme ? nodeTextureScale*2 : nodeTextureScale);
					//Draw.PixelPerfectFrame(frameColor, new Color(), cornerRounding:10);
				}

			#endregion


			//Drawing positioned Inlets/Outlets after frame
			if (!UI.current.layout  &&  !superScheme)
				DrawInletsOutletsIcons(gen);


			//saving calculated node height (to add it to groups etc)
			if (!UI.current.layout)
			{
				gen.guiSize.y = Cell.current.finalSize.y;
				//gen.guiSize.x = nodeWidth;
			}
		}}

		public static void DragGenerator (Generator gen, HashSet<Generator> otherSelected=null)
			{using(ProfilerExt.Profile("DragGenerator"))
		/// Returns the position of generator while dragging
		/// Relayout marks generators whose layout has changed to perform re-draw
		{
			if (UI.current.layout) return; //only in repaint - we've got to have rects ready

			if (DragDrop.TryDrag(gen, UI.current.mousePos))
			{
				if (DragDrop.totalDelta.magnitude >= 2) //to prevent 1-pixel offset when clicking on node
					for (int i=0; i<draggedGenerators.Length; i++)
						draggedGenerators[i].guiPosition = RefinePosition(draggedOriginalPositions[i] + DragDrop.totalDelta);
			}

			if (DragDrop.TryRelease(gen))
			{
				//move to original pos, record undo, and move back
				for (int i=0; i<draggedGenerators.Length; i++)
					draggedGenerators[i].guiPosition = draggedOriginalPositions[i]; //already refined

				UnityEditor.Undo.RecordObject(GraphWindow.current.graph, "Drag Generator" + (otherSelected.Count>1 ? "s"  : ""));

				for (int i=0; i<draggedGenerators.Length; i++)
					draggedGenerators[i].guiPosition = RefinePosition(draggedOriginalPositions[i] + DragDrop.totalDelta);

				EditorUtility.SetDirty(GraphWindow.current.graph);

				draggedGenerators = null;
				draggedOriginalPositions = null;
			}

			if (!(Event.current.control && otherSelected.Count > 1)  &&   //when several nodes are selected grid is activated when started to drag
				DragDrop.TryStart(gen, UI.current.mousePos, new Rect(gen.guiPosition, gen.guiSize)))
			{
				draggedGenerators = otherSelected.ToArray();
				if (!otherSelected.Contains(gen))
					ArrayTools.Add(ref draggedGenerators, gen);

				draggedOriginalPositions = new Vector2[draggedGenerators.Length];
				for (int i=0; i<draggedGenerators.Length; i++)
					draggedOriginalPositions[i] = draggedGenerators[i].guiPosition;
			}

			#if MM_DEBUG
			//TODO: this is for wobbling debugging, remove it

				if (Event.current.type == EventType.KeyDown  &&  GraphWindow.current.selected.Contains(gen))
				{
					Vector2 moveDelta = Vector2.zero;
					if (Event.current.keyCode == KeyCode.UpArrow) moveDelta = new Vector2(0, -1);
					if (Event.current.keyCode == KeyCode.DownArrow) moveDelta = new Vector2(0, 1);
					if (Event.current.keyCode == KeyCode.LeftArrow) moveDelta = new Vector2(-1, 0);
					if (Event.current.keyCode == KeyCode.RightArrow) moveDelta = new Vector2(1, 0);

					if (moveDelta != Vector2.zero)
					{
						/*draggedGenerators = otherSelected.ToArray();
						if (!otherSelected.Contains(gen))
							ArrayTools.Add(ref draggedGenerators, gen);

						draggedOriginalPositions = new Vector2[draggedGenerators.Length];
						for (int i=0; i<draggedGenerators.Length; i++)
							draggedOriginalPositions[i] = draggedGenerators[i].guiPosition;

						for (int i = 0; i<draggedGenerators.Length; i++)
							draggedGenerators[i].guiPosition = RefinePosition(draggedGenerators[i].guiPosition + moveDelta);*/
						gen.guiPosition += moveDelta;

						


						//UI.current.scrollZoom.scroll = new Vector2(); // += moveDelta*UnityEngine.Random.value*2;
						//UI.current.scrollZoom.RoundScroll();
					//	Event.current.Use();

					//	UI.current.editorWindow.Repaint();
					//	EditorUtility.SetDirty(GraphWindow.current.graph);
					}
				}
			#endif
		}}

		private static Generator[] draggedGenerators;
		private static Vector2[] draggedOriginalPositions;


		public static void UpdateGeneratorPosition (Generator gen,
			Dictionary<Generator,Cell> genCellsCache, 
			Dictionary<IInlet<object>, Vector2> inletsPosCache, 
			Dictionary<IOutlet<object>, Vector2> outletsPosCache)
		/// Re-layouts cells and links on dragging generator (or all generators in group)
		{
			//re-layouting cells
			Cell cell = genCellsCache[gen];
			cell.worldPosition = UI.current.scrollZoom.RoundToPixels( gen.guiPosition );
			cell.CalculateSubRects();

			//modifing link positions of dragged nodes
			foreach (IInlet<object> inlet in gen.AllInlets())
				if (inletsPosCache.TryGetValue(inlet, out Vector2 pos))
					inletsPosCache[inlet] = pos + DragDrop.currentDelta;

			foreach (IOutlet<object> outlet in gen.AllOutlets())
				if (outletsPosCache.TryGetValue(outlet, out Vector2 pos))
					outletsPosCache[outlet] = pos + DragDrop.currentDelta;
		}


		public static Vector2 RefinePosition (Vector2 moveTo)
		/// Applies grid and pixel-perfect positioning
		/// Internal move function for DragGenerator
		/// Returns where was generator actually moved
		//TODO: only grid is used now, cells are rounded to pixels anyways
		{
			if (Event.current.control)
			{
				moveTo.x = (int)(float)(moveTo.x / 32 + 0.5f) * 32;
				moveTo.y = (int)(float)(moveTo.y / 32 - 0.5f) * 32;
			}

			float dpiFactor = UI.current.dpiScaleFactor;
			float zoom = UI.current.scrollZoom != null ? UI.current.scrollZoom.zoom.x : 1;
			float round = dpiFactor*zoom;

			//moveTo.y = //Mathf.Round(moveTo.y / round) * round;
			//moveTo.x = UI.current.scrollZoom.ToInternal( Mathf.Round( UI.current.scrollZoom.ToScreenX(moveTo.x) ) ); //Mathf.Round(moveTo.x / round) * round;
			//moveTo = UI.current.scrollZoom.RoundToZoom(moveTo);

			/*Vector2 roundVal = new Vector2(  //0.5002 prevents cells un-align for the reason I don't remember
				moveTo.x > 0  ?  0.5002f  :  -0.5002f,
				moveTo.y > 0  ?  0.5002f  :  -0.5002f )*/

			//moveTo.x = (int)(float)(moveTo.x*dpiFactor*zoom + roundVal.x) / (dpiFactor*zoom);  
			//moveTo.y = (int)(float)(moveTo.y*dpiFactor*zoom + roundVal.y) / (dpiFactor*zoom);

			//moveTo.x = (int)(float)(moveTo.x*round) / round;
			//moveTo.y = (int)(float)(moveTo.y*round) / round;

			return moveTo;
		}


		public static Vector2 ResizeGeneraror (Generator gen)
		/// changes the size of the generator
		/// returns new size if changed, Vector2 zero if not
		{
			if (UI.current.layout)
				return new Vector2();

			Rect cellRect = Cell.current.InternalRect;
			if (DragDrop.ResizeRect(gen, UI.current.mousePos, ref cellRect, 
				minSize:new Vector2(0,0), 
				additionalBottomRightMargin:new RectOffset(4,0,4,0),
				rightBottomOnly:true))
			{
				float minWidth = (gen is IReroute<object>) ? minPortalWidth : minNodeWidth;
				if (cellRect.width < minWidth)
					cellRect.width = minWidth;

				gen.guiSize.x = cellRect.width;

				//enabling/disabling features based on node height

				Cell.current.finalSize.x = cellRect.width;  //Cell.current.InternalRect = cellRect; //size x only
				Cell.current.CalculateSubRects();
				UI.current.editorWindow.Repaint();

				return cellRect.size;
			}

			return new Vector2();
		}


		public static void ResizeGeneratorMenus (Generator gen, float resizeHeight)
		/// opens and closes generator menu on resize
		/// this should ne executed after draw since it changes cells
		{
			if (UI.current.layout)
				return;

			if (resizeHeight < 1)
				return;
						
			//expanding
			bool guiChanged = false;
			if (!gen.guiField) //do not expand preview if no field opened
			{
				if (resizeHeight > gen.guiSize.y + gen.guiFieldHeight - 10)
					{ gen.guiField = true; guiChanged = true; }
			}
			else if (!gen.guiPreview)
			{
				if (resizeHeight > gen.guiSize.y + gen.guiSize.x - 10)
					{ gen.guiPreview = true; guiChanged = true; }
			}
			else if (!gen.guiDebug)
			{
				if (resizeHeight > gen.guiSize.y + 180)
					{ gen.guiDebug = true; guiChanged = true; }
			}

			//collapsing
			if (gen.guiDebug)
			{
				if (resizeHeight < gen.guiSize.y - 180)
					{ gen.guiDebug = false; guiChanged = true; }
			}

			else if (gen.guiPreview)  
			{
				if (resizeHeight < gen.guiSize.y - gen.guiSize.x + 10)
					{ gen.guiPreview = false; guiChanged = true; }
			}

			else if (gen.guiField)
			{
				if (resizeHeight < gen.guiSize.y - gen.guiFieldHeight + 10)
					{ gen.guiField = false;  guiChanged = true; }
			}

			//collapsing all
			if (resizeHeight < 60)  //40 header 20 footer
				{ gen.guiField = false; gen.guiPreview = false; gen.guiAdvanced = false; gen.guiDebug = false; guiChanged = true; }
		}


		private static void DrawNodeBackground (Color color)
		{
			if (nodeMat == null)
				nodeMat = new Material(Shader.Find("Hidden/MapMagic/NodeElementQuad"));

			if (nodeTex == null)
				nodeTex = UI.current.textures.GetTexture("MapMagic/Node/Node");

			if (nodeIds == null)
				nodeIds = Draw.GetMatPropertyIds("_MainTex", "_CellRect", "_ClipRect", "_BorderOffset", "_Scale", "_TexSections", "_NodeSections", "_TopColor", "_BottomColor");

			if (nodeMesh == null)
				nodeMesh = Draw.CreateQuadMesh(3, 4);


			//_MainTex
			nodeMat.SetTexture(nodeIds[0], nodeTex);

			//_CellRect and _ClipRect
			Rect rect = Cell.current.GetRect(UI.current.scrollZoom);
			nodeMat.SetVector(nodeIds[1], Cell.current.InternalRect.ToV4());  //TODO: here and in shadows use ints in SetValue
			nodeMat.SetVector(nodeIds[2], Draw.GetClipRect());

			//_BorderOffset
			int borders = 10;
			nodeMat.SetVector(nodeIds[3], new Vector4(borders,borders,borders,borders)); 

			//_Scale
			nodeMat.SetFloat(nodeIds[4],  0.5f);  //0.5 is default scale applied to oversized textures for dpi 200%

			//_TexSections and _NodeSections
			nodeMat.SetVector(nodeIds[5], new Vector4(20, 10,0,0));  //In that order: x-top, y-bottom, z-left, w-right
			nodeMat.SetVector(nodeIds[6], new Vector4(70, 10,0,0));

			//_Color
			nodeMat.SetColor(nodeIds[7], color); 

			//Draw.Element(nodeTex, nodeMat);
			using (ProfilerExt.Profile("Drawing"))
			{
				Matrix4x4 prs = Matrix4x4.TRS(UI.current.scrollZoom.ToScreen(Cell.current.worldPosition), Quaternion.identity, UI.current.scrollZoom.zoom);
				nodeMat.SetPass(0);
				Graphics.DrawMeshNow(nodeMesh, prs);
			}
		}


		private static void DrawShadow (bool selected)
		/// Unified way to draw shadow since we call it from lots of places
		{
			using(ProfilerExt.Profile("Shadow"))
			{
				if (!selected)
					Draw.Shadow(overflow:70, casterOffset:tempShadowOffset, fill:tempShadowFill, gamma:tempShadowGamma, opacity:tempShadowOpacity, moveDown:10);
				else //white smaller shadow for selected
					Draw.Shadow(overflow:tempShadowSize*0.66f, casterOffset:tempShadowOffset, fill:tempShadowFill, gamma:tempShadowGamma, opacity:tempShadowOpacity*2, color:Color.white);
			
				//additional light shadow for mouse hover
//				if (!selected  &&  (GraphWindow.highlightedGen==gen)) 
//					Draw.Shadow(overflow:tempShadowSize*0.33f, casterOffset:5, fill:tempShadowFill, gamma:tempShadowGamma, opacity:tempShadowOpacity*2, color:Color.white);
			}
		}
			


		public static bool DrawField (Generator gen, bool advancedOptions=false)
		///Re-sed by generator draw and generator inspector tab
		///Returns true if something was drawn
		{
			bool fieldDrawn = false;
			Action<FieldInfo,Cell> fieldCellAction = null;

			if (gen.fieldInlets.HasFieldInlets || UI.current.mouseButton==1)
			{
				void FieldCellAction (FieldInfo field, Cell cell) => DrawInletField(cell, gen, new FieldChArrInfo(field));
				fieldCellAction = FieldCellAction;
			}

			if (!advancedOptions)
				fieldDrawn = Draw.Class(gen, additionalAction:fieldCellAction);

			else //to draw in different cells
			{
				fieldDrawn = true;

				using (Cell.LinePx(0))
					Draw.Class(gen, additionalAction:fieldCellAction);

				Cell.EmptyLinePx(2);
					using (Cell.LinePx(0))
						using (new Draw.FoldoutGroup(ref gen.guiAdvanced, "Advanced", padding:0))
							if (gen.guiAdvanced)
								Draw.Class(gen, additionalAction:fieldCellAction, category:"Advanced");
			}

			return fieldDrawn;
		}


		#if MM_DEBUG
		private static void DrawDebug (Generator gen)
		{
			using (Cell.LinePx(0))
				using (new Draw.FoldoutGroup(ref gen.guiDebug, "Debug"))
					if (gen.guiDebug)
					{
						using (Cell.LineStd) Draw.Label("Id: " + Id.ToString(gen.id), tooltipLabel:true);
						//using (Cell.LineStd) Draw.Label(gen.id.ToString());

						foreach (IInlet<object> inlet in gen.AllInlets())
							using (Cell.LineStd) Draw.Label("Inlet Id: " + Id.ToString(inlet.Id), tooltipLabel:true);

						foreach (IOutlet<object> outlet in gen.AllOutlets())
							using (Cell.LineStd) Draw.Label("Outlet Id: " + Id.ToString(outlet.Id), tooltipLabel:true);

						Cell.EmptyLinePx(5);
						using (Cell.LineStd) Draw.DualLabel("Version GUI:", gen.version.ToString());

						TileData lastData = null;
						if (GraphWindow.current.mapMagic is MapMagicObject mapMagicObject) //trying to use preview data for consistency
							lastData = mapMagicObject?.PreviewData;

						if (lastData != null) 
						{
							Graph rootGraph = GraphWindow.current.mapMagic.Graph;

							foreach ((Graph subGraph, TileData subData) in Graph.AllGraphsDatas(rootGraph,lastData,includeSelf:true))
							{
								if (!subGraph.ContainsGenerator(gen))
									continue;

								ulong? dataVersion = subData.Version(gen);
								using (Cell.LineStd) Draw.DualLabel("Version Data:", dataVersion!=null ? dataVersion.Value.ToString() : "N/A"); 
								using (Cell.LineStd) Draw.Toggle(subData.IsReady(gen), "Ready");

								Cell.EmptyLinePx(5);
								if (gen is ICustomComplexity)
									using (Cell.LineStd) Draw.DualLabel("Progress:", subData.GetProgress((ICustomComplexity)gen).ToString());
								if (gen is IOutlet<object> outlet)
								{
									object product = subData.ReadOutletProduct(outlet);
									string hashString = "null";
									if (product != null)
									{
										int hashCode = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(product);
										hashString = Convert.ToBase64String( BitConverter.GetBytes(hashCode) );
									}
									using (Cell.LineStd) Draw.DualLabel("Product:", hashString);
								}
							}
						}
						else
							using (Cell.LineStd) Draw.Label("No preview data");


						using (Cell.LineStd) Draw.DualLabel("Time Draft:", gen.draftTime.ToString("0.00") + "ms");
						using (Cell.LineStd) Draw.DualLabel("Time Main:", gen.mainTime.ToString("0.00") + "ms");
					}
		}

		#endif


		public static bool SelectGenerators (List<Generator> generators, HashSet<Generator> selection, Dictionary<Generator,Cell> genCellsCache)
		/// Using cell lut as a list of all generators
		/// shiftForSingleSelect selects sinhgle gens only when shift is pressed. Otherwise selected by click
		/// Return true if selection changed
			{ using(ProfilerExt.Profile("SelectGenerators"))
		{
			if (UI.current.layout)
				return false; //got to have positions prepared

			bool mouseDown = Event.current.type == EventType.MouseDown  &&  Event.current.button == 0;

			if (!mouseDown  &&  !DragDrop.IsDragging())
				return false;

			bool selectionChanged = false;

			//if we clicked on generator?
			//and if we clicked several - picking the one with top priority
			Generator clickedGen = null;
			int maxPriority = -1;
			Cell maxPriorityCell = null;
			foreach (var kvp in genCellsCache)
				if (kvp.Value.Contains(UI.current.mousePos))
				{
					int priority = generators.IndexOf(kvp.Key);
					if (priority > maxPriority && !UI.current.layout)
					{
						//hack - if there are two nodes at the same place - avoid touching fields
						//if (maxPriorityCell != null)
						//	Event.current.Use();
						//	maxPriorityCell.InternalRect = new Rect(maxPriorityCell.InternalRect.position, Vector2.zero); //if it's top priority - disabling prev top cell

						maxPriority = priority;
						maxPriorityCell = kvp.Value;
						clickedGen = kvp.Key;
					}
					//else if (!UI.current.layout)
					//	kvp.Value.InternalRect = new Rect(kvp.Value.InternalRect.position, Vector2.zero); //if it's not top priority - disabling it's cell
				}

			bool wasClickedGenSelected = selection.Contains(clickedGen);

			//single selection
			if (mouseDown  &&  !DragDrop.IsDragging<Generator>()  &&  !wasClickedGenSelected)
			{
				if (clickedGen != null)
				{
					if (!Event.current.control)
						selection.Clear();

					selection.Add(clickedGen);
					selectionChanged = true;
					UI.RemoveFocusOnControl();
					UI.current.editorWindow.Repaint();
				}
			}

			//selection frame
			if (clickedGen == null  ||  DragDrop.IsDragging())
			{
				if (DragDrop.TryDrag("NodeSelectionFrame"))
				{
					GUIStyle frameStyle = UI.current.textures.GetElementStyle("DPUI/Backgrounds/SelectionFrame");

					Rect rect = new Rect(DragDrop.initialMousePos, UI.current.mousePos-DragDrop.initialMousePos);
					rect = rect.TurnNonNegative();

					Draw.Element(rect, frameStyle);

					//selecting nodes already (to visualize)
					//however this might happen only once on TryRelease
					if (!Event.current.control  &&  !Event.current.alt)
					{
						selection.Clear();
						selectionChanged = true;
					}

					Rect frameRect = new Rect(DragDrop.initialMousePos, UI.current.mousePos-DragDrop.initialMousePos);
					frameRect = frameRect.TurnNonNegative();

					foreach (var kvp in genCellsCache)
					{
						Cell cell = kvp.Value;
						Generator gen = kvp.Key;

						Rect genRect = new Rect(gen.guiPosition, cell.finalSize);
						//if (frameRect.Contains(cell.InternalRect))
						if (frameRect.Intersects(cell.InternalRect))
						{ 
							if (!Event.current.alt  &&  !selection.Contains(gen)) 
							{
								selection.Add(gen); 
								selectionChanged = true;
							}
							if (Event.current.alt  &&  selection.Contains(gen))
							{
								selection.Remove(gen);
								selectionChanged = true;
							}
						}
					}
				}

				if (DragDrop.TryRelease("NodeSelectionFrame")  &&  !UI.current.layout)
					UI.RemoveFocusOnControl();

				if (clickedGen == null  &&  Event.current.type == EventType.MouseDown   &&  Event.current.button == 0)
					DragDrop.ForceStart("NodeSelectionFrame", UI.current.mousePos, new Rect(0,0,0,0));
			}

			//deselecting (after drag since we could start frame)
			if (clickedGen == null  &&  
				selection.Count != 0  &&
				mouseDown  &&
				!DragDrop.IsDragging<Generator>())
				{
					selection.Clear();
					selectionChanged = true;
					UI.current.editorWindow.Repaint();
				}
				
			return selectionChanged;
		}}


		public static void DrawInlet (IInlet<object> inlet, bool addCellObj=true, Vector2 position=new Vector2())
		/// Draws Inlet in cell and adds it's position to inletsPosCache
		/// If position is 0 Draws at the center of current cell
		/// TODO: don't need addCellObj
			{ using(ProfilerExt.Profile("DrawInlet"))
		{
			Graph graph = GraphWindow.current.graph;
			Color color = GetLinkColor(inlet);
			float scale = UI.current.scrollZoom!=null ? UI.current.scrollZoom.SoftZoom : 1; //we can draw it in properties window or test
			//Vector2 pos = new Vector2(); //assigned and used only in repaint

			if (!UI.current.layout  &&  position.sqrMagnitude < 0.01f)
				position = Cell.current.InternalCenter;

			//drawing
			if (UI.current.Repaint  ||   //we don't know position before repaint, and position is wrong on mouse events (raised 20 pixels)
				(UI.current.scrollZoom!=null && UI.current.scrollZoom.zoomChanged))  //HACK to re-layout nodes on zoom changed
					GraphWindow.current.inletsPosCache.ForceAdd(inlet,position);

			#if MM_DEBUG
			if (inlet.Id == 0)
				Draw.Rect(Cell.current.GetRect().Extended(10), Color.red);
			#endif

			//dragging
			if (!UI.current.layout) 
			{
				IOutlet<object> outlet = null;
				Vector2 outletPos = new Vector2();
				(string,ulong) id = ("Inlet",inlet.Id); //can't use inlet itself or it's id - because it can be generator or outlet

				if (DragDrop.TryDrag(id, UI.current.mousePos))
				//do not use inlet as id obj - it can be generator
				{
					if (GraphWindow.current.outletsPosCache != null)
					{
						int dist = (int)(inletOutletDragArea/2 * scale);
						
						foreach (var kvp in GraphWindow.current.outletsPosCache)
							if ((kvp.Value-UI.current.mousePos).magnitude < dist)
								{ outlet = kvp.Key; outletPos = kvp.Value; }
					}

					color.a = 0.5f;	

					if (outlet != null) 
					{
						if (!graph.CheckLinkValidity(outlet, inlet)) color = Color.red;
						LinkDraw.DrawLink(outletPos, position, color);
					}
					else 
						LinkDraw.DrawLink(UI.current.mousePos, position, color);
				}

				if (DragDrop.TryRelease(id, UI.current.mousePos))
				{
					GraphWindow.RecordCompleteUndo();

					if (outlet == null)
					{
						graph.UnlinkInlet(inlet);
						GraphEditorActions.RemoveLink(graph, inlet);
					}
					else if (graph.CheckLinkValidity(outlet,inlet))
						graph.Link(outlet, inlet);

					GraphWindow.current?.RefreshMapMagic();
				}

				Rect inletRect = new Rect (position, Vector2.zero);
				inletRect = inletRect.Extended(inletOutletDragArea/2);

				DragDrop.TryStart(id, UI.current.mousePos, inletRect);
			}
		}}


		public static void DrawOutlet (IOutlet<object> outlet)
		/// Requires 0-width cell, draws outlet in the center
			{ using(ProfilerExt.Profile("DrawOutlet"))
		{
			//if (UI.current.layout) return;
			//no optimize!

			Graph graph = GraphWindow.current.graph;
			Color color = GetLinkColor(outlet);
			float scale = UI.current.scrollZoom!=null ? UI.current.scrollZoom.SoftZoom : 1; //we can draw it in properties window or test
			Vector2 pos = new Vector2(); //assigned and used only in repaint

			//drawing
			if (UI.current.Repaint  ||   //we don't know position before repaint, and position is wrong on mouse events (raised 20 pixels)
				(UI.current.scrollZoom!=null && UI.current.scrollZoom.zoomChanged))  //HACK to re-layout nodes on zoom changed
				{
					pos = Cell.current.InternalCenter;// + new Vector2(-2,0);
					GraphWindow.current.outletsPosCache.ForceAdd(outlet, pos);
				}

			#if MM_DEBUG
			if (outlet.Id == 0)
				Draw.Rect(Cell.current.GetRect().Extended(10), Color.red);
			#endif

			//dragging
			if (!UI.current.layout) 
			{
				IInlet<object> inlet = null;
				Vector2 inletPos = new Vector2();
				(string,ulong) id = ("Outlet",outlet.Id); //can't use inlet itself or it's id - because it can be generator or outlet

				if (DragDrop.TryDrag(id, UI.current.mousePos))
				{
					if (GraphWindow.current.inletsPosCache != null)
					{
						int dist = (int)(inletOutletDragArea/2 * scale);
						
						foreach (var kvp in GraphWindow.current.inletsPosCache)
							if ((kvp.Value-UI.current.mousePos).magnitude < dist)
								{ inlet = kvp.Key; inletPos = kvp.Value; }
					}

					color.a = 0.5f;	

					if (inlet != null) 
					{
						if (!graph.CheckLinkValidity(outlet,inlet)) color = Color.red;
						LinkDraw.DrawLink(pos, inletPos, color);
					}
					else LinkDraw.DrawLink(pos, UI.current.mousePos, color);
				}

				if (DragDrop.TryRelease(id, UI.current.mousePos))
				{
					GraphWindow.RecordCompleteUndo();

					if (inlet != null  &&  graph.CheckLinkValidity(outlet,inlet))
						graph.Link(outlet, inlet);

					GraphWindow.current?.RefreshMapMagic();
				}

				Rect outletRect = new Rect (Cell.current.InternalCenter, Vector2.zero);
				outletRect = outletRect.Extended(inletOutletDragArea/2);

				DragDrop.TryStart(id, UI.current.mousePos, outletRect);
			}
		}}




		public static void DrawPortal (Generator gen, Graph graph, bool selected=false, bool isMini=false, string tooltip=null)
			{ using(ProfilerExt.Profile("DrawPortal"))
		/// Starts similar to DrawStandard, but then absolutely different
		{
			bool isScheme = UI.current.scrollZoom.zoom.x < 0.4f;
			bool superScheme = UI.current.scrollZoom.zoom.x < 0.2f;

			bool portalEnter = gen is IPortalEnter<object>;
			bool portalExit = gen is IPortalExit<object>;
			IPortalExit<object> portalExitGen = gen as IPortalExit<object>;  //no, portalExit = is IPortalExit<object> portalExitGen won't work

			Color color = GeneratorDraw.GetGeneratorColor(gen as IOutlet<object>);
			color.a = 1;

			//shadow
			if (!UI.current.layout)
				DrawShadow(selected);

			//background
			if (!UI.current.layout)
				{
					string texName;
					if (portalEnter)
						texName = "MapMagic/Node/PortalIn";
					else if (portalExit)
						texName = "MapMagic/Node/PortalOut";
					else
						texName = "MapMagic/Node/Reroute";

					Texture2D collapsedTex = UI.current.textures.GetTexture(texName);
					Draw.Element(collapsedTex, new Vector4(10, 10, 10, 10), color:color, scale:isScheme ? nodeTextureScale*2 : nodeTextureScale);
				}


			#region headerfield
			using (Cell.LinePx(32))
			{
				float iconSize = 20;


				//inlet
				if (gen is IInlet<object> inlet)
					using (Cell.RowPx(0)) DrawInlet(inlet);

				//common case
				if (!portalExit)
				{
					//icon
					Cell.EmptyRowPx(8);
					if (portalEnter)
					{
						Texture2D genIcon = UI.current.textures.GetTexture("GeneratorIcons/PortalIn");
						using (Cell.RowPx(iconSize)) Draw.Icon(genIcon, scale:0.5f);
					}

					//label
					if (!isMini)
						using (Cell.Row) 
							using (Cell.Padded(0,0,-7,7))
					{
						Color prevSelectionColor = UnityEngine.GUI.skin.settings.selectionColor;
						UnityEngine.GUI.skin.settings.selectionColor = new Color(0, 0.3555f, 0.78125f);
						gen.guiName = Draw.EditableLabelText(gen.guiName, style:UI.current.styles.bigLabel); 
						UnityEngine.GUI.skin.settings.selectionColor = prevSelectionColor;
					}

					Cell.EmptyRowPx(5);
				}

				//exit with selector
				else
				{
					Cell.EmptyRowPx(10);

					//icon
					Texture2D genIcon = UI.current.textures.GetTexture("GeneratorIcons/PortalOut");
					using (Cell.RowPx(iconSize)) Draw.Icon(genIcon, scale:0.5f);
					//UI.Empty(Size.RowPixels(5));

					IPortalEnter<object> linkedEnter = portalExitGen.GetEnter(graph);

					//label
					using (Cell.Row) //cell or empty cell if mini
						using (Cell.Padded(0,0,-7,7))
							if (!isMini)
							{
								string label = "(Empty)";
								if (linkedEnter != null)
									label = linkedEnter.GuiName;
								Draw.Label(label, style:UI.current.styles.bigLabel);
							}

					//going to enter
					if (linkedEnter != null  &&  Event.current.alt  &&  UI.current.mouseButton==0  &&   Cell.current.Contains(UI.current.mousePos)  &&  !UI.current.layout)
						GraphWindow.current.graphUI.scrollZoom.FocusWindowOn(linkedEnter.Gen.guiPosition, GraphWindow.current.position.size);

					Texture2D chevronDown = UI.current.textures.GetTexture("DPUI/Chevrons/Down");
					using (Cell.RowPx(iconSize))
						if (Draw.Button(null, icon:chevronDown, iconScale:0.5f, visible:false))
							PortalSelectorPopup.DrawPortalSelector(graph, portalExitGen);

					Cell.EmptyRowPx(10);
				}

				//outlet - this will only calculate outlet position, drawing at the end
				if (gen is IOutlet<object> outlet)
					using (Cell.RowPx(0)) DrawOutlet(outlet);
			}
			#endregion

			
			#region Frame - before inletsOutlets
			Color frameColor = selected ? Color.white : Color.black;

			string frameTexName;
			if (portalEnter)
				frameTexName = "MapMagic/Node/PortalIn_Frame";
			else if (portalExit)
				frameTexName = "MapMagic/Node/PortalOut_Frame";
			else
				frameTexName = "MapMagic/Node/Frame";

			Texture2D frameTex = UI.current.textures.GetTexture(frameTexName);
			Draw.Element(frameTex, new Vector4(10, 10, 10, 10), color:frameColor, scale:isScheme ? 1 : 0.5f);
			//Draw.PixelPerfectFrame(frameColor, new Color(), cornerRounding:10);
			#endregion


			if (!UI.current.layout  &&  !superScheme)
				DrawInletsOutletsIcons(gen);


			if (!UI.current.layout)
			{
				gen.guiSize.y = Cell.current.finalSize.y;
				//gen.guiSize.x = nodeWidth;
			}
		}}


		public static bool TryRenderGenToTexture (Generator gen, Cell cell, Dictionary<Generator,(Texture2D,Rect)> genTextures)
		/// Renders generator to texture if possible
		/// Appends texture cache
		/// Returns true if generator was rendered
		{
			Rect rect = UI.current.scrollZoom.ToScreen(cell.InternalRect);

			//EditorGUI.DrawRect(rect,Color.green);

			Rect winRect = GraphWindow.current.rootVisualElement.worldBound;
			winRect.position = new Vector2(0, -18);

			//if not fully within window - can't render to texture without artifacts
			Rect graphWinRect = winRect.Extended(0,0, -GraphWindow.toolbarSize, 0);
			if (!(graphWinRect.Contains(rect.position)  &&  graphWinRect.Contains(rect.max))) 
				return false;


			bool texExisted = genTextures.TryGetValue(gen, out (Texture2D t, Rect r) texRect);
			Texture2D tex = texRect.t;
			Rect writeRect = texRect.r;

			Vector2Int texSize = new Vector2Int(
				(int)((rect.size.x+2.5f) * UI.current.dpiScaleFactor), 
				(int)((rect.size.y+2.5f) * UI.current.dpiScaleFactor)); //rounding +1 additional pixels around (we need those for some reason)

			if (tex == null)
				tex = new Texture2D(texSize.x, texSize.y, TextureFormat.RGBA32, mipChain:false, linear:false);

			if (tex.width != texSize.x  ||  tex.height != texSize.y)
				tex.Reinitialize(texSize.x, texSize.y);

			Rect readRect = new Rect(
				(int) (rect.position.x * UI.current.dpiScaleFactor + 0.5f),
				(int)((winRect.size.y - rect.position.y - 18 + UI.current.dpiScaleFactor) * UI.current.dpiScaleFactor - texSize.y),  //idk where 18 comes from. Others is winHeight-pos-height
				tex.width, 
				tex.height);

			tex.ReadPixels(readRect, 0, 0, recalculateMipMaps:false);
			tex.Apply();

			if (texExisted)
				genTextures[gen] = (tex,readRect);
			else
				genTextures.Add(gen, (tex,readRect));

			return true;
		}


		public static Rect GetGenTextureRect (Cell cell, Texture2D tex)
		{
			Rect winRect = GraphWindow.current.rootVisualElement.worldBound;
			winRect.position = new Vector2(0, -18);

			Rect rect = UI.current.scrollZoom.ToScreen(cell.InternalRect);

			Rect readRect = new Rect(
				(int) (rect.position.x * UI.current.dpiScaleFactor),
				(int)((winRect.size.y - rect.position.y - 18 + UI.current.dpiScaleFactor) * UI.current.dpiScaleFactor - tex.height),  //idk where 18 comes from. Others is winHeight-pos-height
				tex.width, 
				tex.height);

			Rect writeRect = new Rect(
			   rect.x, //(readRect.x - 0.5f) / UI.current.dpiScaleFactor,
			   winRect.size.y - ((readRect.position.y + tex.height) / UI.current.dpiScaleFactor) - 18,
			   readRect.width / UI.current.dpiScaleFactor,
			   readRect.height / UI.current.dpiScaleFactor);

			//fixes one-pixel offset for popular DPIs. I give up to comprehend how Unity handles dpi scale
			writeRect.position = writeRect.position + new Vector2(-0.5f,0); //seem to be needed for all dpi, including 1

			if (UI.current.dpiScaleFactor > 1.49f)
				writeRect.position = writeRect.position + new Vector2(-0.5f, 0.75f); //additional 0.5 to make it -1

			//if (Mathf.Approximately(UI.current.dpiScaleFactor, 1.25f))
			//	writeRect.position = writeRect.position + new Vector2(0,-0.5f); //height change seem  to happen only on 1.25, not even 1.2

			return writeRect;
		}


		public static void DrawAsTexture (Generator gen, Cell cell, bool selected, Texture2D tex)
		{
			Rect tRect = GeneratorDraw.GetGenTextureRect(cell, tex);

			//shadow
			using (cell.Activate())
				DrawShadow(selected);

			//texture
			EditorGUI.DrawPreviewTexture(tRect, tex);

			//inlets/outlets
			bool superScheme = UI.current.scrollZoom.zoom.x < 0.2f;
			if (!UI.current.layout  &&  !superScheme)
				DrawInletsOutletsIcons(gen);
		}



		public static T DrawGlobalVar<T> (T val, string label, string tooltip=null)
		{
			using (Cell.RowRel(1-Cell.current.fieldWidth)) 
			{
				Cell.EmptyRowPx(3);
				using (Cell.RowPx(9)) Draw.Icon(UI.current.textures.GetTexture("DPUI/Icons/Linked"));
				using (Cell.Row) Draw.Label(label, tooltip:tooltip);

				if (val != null  &&  val is float)
					val = (T)(object)Draw.DragValue((float)(object)val);
				if (val != null  &&  val is int)
					val = (T)(object)Draw.DragValue((int)(object)val);
			}
			
			using (Cell.RowRel(Cell.current.fieldWidth))
				val = (T)Draw.UniversalField(val, typeof(T)); 

			return val;
		}
		public static void DrawGlobalVar<T> (ref T val, string label) => val = DrawGlobalVar<T>(val, label);


		public static void SubGraph (IBiome biome, ref Graph subGraph, bool refreshOnGraphChange=true)
		{
			using (Cell.Row)
			{
				Graph prevSubGraph = subGraph;
				Draw.ObjectField(ref subGraph); 

				if (Cell.current.valChanged)
				{
					if (SubGraphDependenceError(biome, subGraph))
					{
						Debug.Log($"Graph {subGraph} contains this biome (directly or in sub-graphs). Could not be assigned since it will create dependency loop.");
						subGraph = prevSubGraph;
						return;
					}

					//if (biome is Biomes.BaseFunctionGenerator fn)
					//	if (biome.SubGraph != null)
					//		fn.ovd = new Expose.Override(subGraph.defaults);
					//requires an access to module. Moved to fn editor

					if (refreshOnGraphChange)
						GraphWindow.current?.RefreshMapMagic();	
				}
			}

			Texture2D openIcon = UI.current.textures.GetTexture("DPUI/Icons/FolderOpen");
				using (Cell.RowPx(22))
					if (Draw.Button(icon:openIcon, iconScale:0.5f, visible:false) && subGraph!=null)
					{
						Graph subGraphClosure = subGraph;
						UI.current.DrawAfter( ()=> GraphWindow.current.OpenBiome(subGraphClosure) );
					}
		}

		private static bool SubGraphDependenceError (IBiome biome, Graph subGraph)
		/// True if subGraph contains biome (even in sub-sub grpahs)
		{
			if (subGraph == null)
				return false;

			Generator gen = ((IUnit)biome).Gen;

			if (subGraph.ContainsGenerator(gen))
				return true;

			foreach (Graph subSubGraph in subGraph.SubGraphs(recursively:true))
				if (subSubGraph.ContainsGenerator(gen))
					return true;

			return false;
		}


		private static void DrawInletsOutletsIcons (Generator gen) {
		/// Draws inlets outlets circles atop of the node
			using(ProfilerExt.Profile("InletsOutlets"))
		{
			Texture2D icon = UI.current.textures.GetTexture("MapMagic/Node/InletOutlet");
			float scale = UI.current.scrollZoom.SoftZoom;

			if (!(gen is IPortalExit<object>)  ||  GraphWindow.current.graph.debugDrawPortalLinks) //don't draw inlet for enter
				foreach (IInlet<object> inlet in gen.AllInlets())
				{
					Color inletColor = GetLinkColor(inlet);
					Vector2 pos = GraphWindow.current.inletsPosCache[inlet];
					Draw.Icon(icon, pos, color:inletColor, scale:0.5f*scale);
				}

			if (!(gen is IPortalEnter<object>)  ||  GraphWindow.current.graph.debugDrawPortalLinks) //don't draw outlet for exit
				foreach (IOutlet<object> outlet in gen.AllOutlets())
				{
					Color outletColor = GetLinkColor(outlet);
					Vector2 pos = GraphWindow.current.outletsPosCache[outlet];
					Draw.Icon(icon, pos, color:outletColor, scale:0.5f*scale);
				}
		}}


		public static void DrawInletField (Cell cell, Generator gen, Type type, string fieldName)
		/// Adds right-click menu to cell and draw exposed field over standard one if cell is exposed
		{
			FieldInfo field = Draw.FindCachedField(type,fieldName);
			DrawInletField(cell, gen, new FieldChArrInfo(field));
		}


		public static void DrawInletField (Cell cell, Generator gen, FieldChArrInfo fieldInfo)
		/// Adds right-click menu to cell and draw exposed field over standard one if cell is exposed
		{
			IInlet<object> inlet = gen.fieldInlets.FindInlet(fieldInfo);

			//disabling cell
			if (inlet != null)
			{
				foreach (Cell subCell in Cell.current.SubCellsRecursively(false))
					if (!subCell.special.HasFlag(Cell.Special.Label)) //TODO: disable only fields, labels just inactiuve
						subCell.disabled = true;
			}

			//drawing
			if (!UI.current.layout)
			{
				if (inlet != null) //if field is exposed as inlet
				{
					Vector2 position = Cell.current.InternalCenter;
					position.x = gen.guiPosition.x; //placing at the edge of the node, not field (it has 2 pixel offset)
					position.x += inletOutletCellWidth/2 + 1; //idk why +1

					DrawInlet(inlet, position:position);

					Cell.current.disabled = true;

					foreach (Cell subCell in cell.SubCellsRecursively(includeSelf:true))
					{
						subCell.inactive = true; //turning off controls like field drag
						//making all cells disable recursively since they won't make it AFTER deactivating them
						//could not be made inside special.HasFlag because flag is set only on repaint - when it's too late to make inactive

						if (subCell.special.HasFlag(Cell.Special.Field))
						{

							subCell.Activate();

							

							subCell.Dispose();
						}



						//TODO NEXT
						//clean up


						if (!UI.current.layout) //will need field width, and what's the point to do it in layout?
						{
							float fieldWidth = Cell.current.finalSize.x; //we are not in layout
			
							/*string label = graph.exposed.GetExpression(genId, fieldName, channel:chNum, arrIndex:arrIndex);
							float labelWidth = UI.current.styles.label.CalcSize( new GUIContent(label) ).x;
							if (labelWidth > fieldWidth-20)
								label = "...";
			
							Draw.Label(label, UI.current.styles.field);

							Vector2 center = Cell.current.InternalCenter;
							Vector2 iconPos = new Vector2(center.x + fieldWidth/2 - 10, center.y);
							Draw.Icon(UI.current.textures.GetTexture("DPUI/Icons/Expose"), iconPos, scale:0.5f);

							//if (Draw.Button(visible:false))  //cell inactive
							if (UI.current.mouseButton==0 && Cell.current.Contains(UI.current.mousePos))
								ExposeWindow.ShowWindow(graph, genId, fieldName, fieldType, chNum, arrIndex);*/
						}

						
					}
				}
			}

			//adding to right-click menu
			if (UI.current.mouseButton==1)
			{
				GraphWindow.current.fieldRectsCache.Add((cell.InternalRect, fieldInfo));
			}

			
			//if vector - calling per-channel expose cell first
			if (cell.special.HasFlag(Cell.Special.Vector) && fieldInfo.channel < 0) 
			{
				foreach (Cell subCell in cell.SubCellsRecursively())
				{
					if (subCell.special.HasFlag(Cell.Special.VectorX))
						DrawInletField(subCell, gen, new FieldChArrInfo(fieldInfo.field, 0));

					if (subCell.special.HasFlag(Cell.Special.VectorY))
						DrawInletField(subCell, gen, new FieldChArrInfo(fieldInfo.field, 1));

					if (subCell.special.HasFlag(Cell.Special.VectorZ))
						DrawInletField(subCell, gen, new FieldChArrInfo(fieldInfo.field, 2));

					if (subCell.special.HasFlag(Cell.Special.VectorW))
						DrawInletField(subCell, gen, new FieldChArrInfo(fieldInfo.field, 3));
				}
			}
		}


		#region Helpers

		private static readonly Dictionary<Type,GeneratorMenuAttribute> cachedAttributes = new Dictionary<Type, GeneratorMenuAttribute>();

			public static GeneratorMenuAttribute GetMenuAttribute (Type genType)
			{
				if (cachedAttributes.TryGetValue(genType, out GeneratorMenuAttribute att)) return att;

				Attribute[] atts = Attribute.GetCustomAttributes(genType); 
			
				GeneratorMenuAttribute menuAtt = null;
				for (int i=0; i<atts.Length; i++)
					if (atts[i] is GeneratorMenuAttribute) menuAtt = (GeneratorMenuAttribute)atts[i];

				if (menuAtt != null)
				{
					menuAtt.nameUpper = menuAtt.name.ToUpper();
					menuAtt.nameWidth = UI.current.styles.bigLabel.CalcSize( new GUIContent(menuAtt.nameUpper) ).x;
					menuAtt.icon = UI.current.textures.GetTexture(menuAtt.iconName ?? "GeneratorIcons/Generator");
					menuAtt.type = genType;
					menuAtt.color = menuAtt.colorType != null ?
						GetGeneratorColor( menuAtt.colorType) :
						GetGeneratorColor( Generator.GetGenericType(genType) );
				}
				//else
				//	throw new Exception("Could not find attribute for " + genTypeName.ToString());
				// there are still some generators without attribute. Like "MyGenerator"

				cachedAttributes.Add(genType, menuAtt);
				return menuAtt;
			}


			public static readonly Dictionary<Type, Color> generatorColors = new Dictionary<Type, Color>
			{
				{ typeof(MatrixWorld), new Color(0.4f, 0.666f, 1f, 1f) },
				{ typeof(TransitionsList), new Color(0.444f, 0.871f, 0.382f, 1f) },
				{ typeof(Den.Tools.Splines.SplineSys), new Color(1f, 0.6f, 0f, 1f) },
				{ typeof(Den.Tools.Lines.LineSys), new Color(1f, 0.6f, 0f, 1f) },
				{ typeof(IBiome), new Color(0.45f, 0.55f, 0.56f, 1f) },
				{ typeof(MatrixSet), new Color(0.3f, 0.444f, 0.75f, 1f) }
			};

			public static readonly Dictionary<Type, Color> generatorColorsPro = new Dictionary<Type, Color>
			{
				{ typeof(MatrixWorld), new Color32(66, 94, 130, 255) },
				{ typeof(TransitionsList), new Color32(74, 134, 64, 255) },
				{ typeof(Den.Tools.Splines.SplineSys), new Color32(126, 94, 63, 255) },
				{ typeof(Den.Tools.Lines.LineSys), new Color(0.325f, 0.25f, 0.16f, 1f) },
				{ typeof(IBiome), new Color(0.45f, 0.55f, 0.56f, 1f) },
				{ typeof(MatrixSet), new Color(0, 0.2f, 0.5f, 1f) },
				{ typeof(Calculator.Vector), new Color(0.7f, 0.7f, 0.7f, 1f) }
			};

			public static readonly Dictionary<Type, Color> linkColors = new Dictionary<Type, Color>
			{
				{ typeof(MatrixWorld), new Color(0, 0.333f, 0.666f, 1f) },
				{ typeof(TransitionsList), new Color(0, 0.465f, 0, 1f) },
				{ typeof(Den.Tools.Splines.SplineSys), new Color(0.666f, 0.333f, 0, 1f) },
				{ typeof(Den.Tools.Lines.LineSys), new Color(0.666f, 0.333f, 0, 1f) },
				{ typeof(MatrixSet), new Color(0, 0.16f, 0.333f, 1f) }
			};

			public static readonly Dictionary<Type, Color> linkColorsPro = new Dictionary<Type, Color>
			{
				{ typeof(MatrixWorld), new Color(0, 0.5f, 1f, 1f) },
				{ typeof(TransitionsList), new Color(0.2f, 1f, 0, 1f) },
				{ typeof(Den.Tools.Splines.SplineSys), new Color(1f, 0.5f, 0f, 1f) },
				{ typeof(Den.Tools.Lines.LineSys), new Color(1f, 0.5f, 0f, 1f) },
				{ typeof(MatrixSet), new Color(0, 0.25f, 0.5f, 1f) },
				{ typeof(Calculator.Vector), new Color(0.666f, 0.666f, 0.666f, 1f) }
			};

			public static Color GetGeneratorColor (IOutlet<object> gen) => GetGeneratorColor( Generator.GetGenericType(gen) );
			public static Color GetGeneratorColor (IInlet<object> gen) => GetGeneratorColor( Generator.GetGenericType(gen) );

			public static Color GetGeneratorColor (Generator gen, GeneratorMenuAttribute menuAtt)
			{
				Color color;
				if (menuAtt.colorType != null)
					color = GetGeneratorColor(menuAtt.colorType);
				else if (gen is IOutlet<object>  ||  gen is IInlet<object>)
					color = GetGeneratorColor( Generator.GetGenericType(gen) );
				else
					color = new Color(0.65f, 0.65f, 0.65f, 1);

				if (!gen.enabled) 
					color = DisabledGeneratorColor(color);

				return color;
			}


			public static Color GetGeneratorColor (Type genericType)
			{
				if (genericType == null) //generic color might not be determined for some nodes
					return defaultGeneratorColor;

				if (UI.current.styles.isPro)
					return generatorColorsPro.TryGetValue(genericType, out var color) ? color : defaultGeneratorColor;
				else
					return generatorColors.TryGetValue(genericType, out var color) ? color : defaultGeneratorColor;
			}


			public static Color GetLinkColor (IInlet<object> inlet) => GetLinkColor(Generator.GetGenericType(inlet));
			public static Color GetLinkColor (IOutlet<object> outlet) => GetLinkColor(Generator.GetGenericType(outlet));
			public static Color GetLinkColor (Type genericType)
			{
				bool isPro = UI.current.styles.isPro;
				if (isPro)
					return linkColorsPro.TryGetValue(genericType, out var color) ? color : Color.gray;
				else
					return linkColors.TryGetValue(genericType, out var color) ? color : Color.gray;
			}

			private static Color DisabledGeneratorColor (Color color)
			//returns the disabled color from original generator's color
			{
				return Color.Lerp(color, new Color(0.8f, 0.8f, 0.8f, 1), 0.75f);
			}

			private static Color defaultGeneratorColor = new Color(0.65f, 0.65f, 0.65f, 1);

		#endregion


		#region AutoPositioning

			public static bool FindPlace (ref Vector2 original, Vector2 range, Graph graph, int step=10, int margins=5)
			/// Tries to find the position that is not intersecting with other nodes. Return true if found (and changes ref)
			{
				//creating a list of generators within range
				List<Rect> gensInRange = new List<Rect>();
				Rect rangeRect = new Rect(
					original.x-range.x-nodeWidth/2-margins, 
					original.y-range.y-nodeWidth/4-margins, 
					range.x*2+nodeWidth+margins*2, 
					range.y*2+nodeWidth/2+margins*2);
				for (int g=0; g<graph.generators.Length; g++) 
				{
					if (!rangeRect.Intersects(graph.generators[g].guiPosition, graph.generators[g].guiSize)) continue;
					gensInRange.Add(new Rect(graph.generators[g].guiPosition, graph.generators[g].guiSize));
				}
				int gensInRangeCount = gensInRange.Count;

				Vector2 defSize = new Vector2(nodeWidth + margins*2, nodeWidth/2 + margins*2);

				//finding number of steps
				int stepsX = (int)(range.x/step) + 1;
				int stepsY = (int)(range.y/step) + 1;
				int stepsMax = Mathf.Max(stepsX, stepsY);

				//find the closest possible place (starting from center)
				Coord center = new Coord(0,0);
				foreach (Coord coord in center.DistanceArea(stepsMax))
				{
					int x = coord.x;
					int y = coord.z;

					if (x>stepsX || x<-stepsX || y>stepsY || y<-stepsY) continue;

					Vector2 newPos = new Vector2(original.x + x*step - margins, original.y + y*step - margins);
					//Vector2 pos = new Vector2(original.x + x*step, original.y + y*step);

					bool intersects = false;
					for (int g=0; g<gensInRangeCount; g++)
					{
						if (gensInRange[g].Intersects(newPos, defSize))
							{ intersects = true; break; }
					}

					if (!intersects)
					{
						//using (Cell.Custom( new Rect(pos, defSize)))
						//	Draw.Rect(Color.red);

						original = newPos + new Vector2(margins,margins);
						return true;
					}
				}

				return false;
			}


			public static void RelaxIteration (Generator gen, List<Generator> nearGens, List<IOutlet<object>> inletGens, List<Generator> outletGens)
			{
				
			}


			internal static void TestRelax (Graph graph)
			{
				Generator gen = null;
				for (int g=0; g<graph.generators.Length; g++)
					if ( new Rect(graph.generators[g].guiPosition, graph.generators[g].guiSize).Contains(UI.current.mousePos) )
						{ gen = graph.generators[g]; break; }

				if (gen != null)
				{
					using (Cell.Custom(  new Rect(gen.guiPosition, gen.guiSize) ))
						Draw.Rect(Color.red);
				}
			}


		#endregion
	}
}