﻿using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;

using Den.Tools;
using Den.Tools.GUI;


namespace MapMagic.Nodes.GUI
{
	public static class BrushGraphTemplate
	{
			//empty graph is created viaCreateAssetMenuAttribute,
			//but unfortunately there's only one attribute per class

			[MenuItem("Assets/Create/MapMagic/Brush Graph", priority = 102)]
			static void MenuCreateMapMagicBrushGraph(MenuCommand menuCommand)
			{
				ProjectWindowUtil.StartNameEditingIfProjectWindowExists(0, 
					ScriptableObject.CreateInstance<TmpCallbackRecieverBrush>(), 
					"Brush Graph.asset", 
					TexturesCache.LoadTextureAtPath("MapMagic/Icons/AssetBig"), 
					null);
			}

			class TmpCallbackRecieverBrush : UnityEditor.ProjectWindowCallback.EndNameEditAction
			{
				public override void Action(int instanceId, string pathName, string resourceFile)
				{
					Graph graph = CreateBrushErosionTemplate();
					graph.name = System.IO.Path.GetFileName(pathName);
					AssetDatabase.CreateAsset(graph, pathName);

					ProjectWindowUtil.ShowCreatedAsset(graph);

					GraphInspector.allGraphsGuids = new HashSet<string>(AssetDatabase.FindAssets("t:Graph"));
				} 
			}

			public static Graph CreateBrushErosionTemplate ()
			{
				Graph graph = GraphInspector.CreateInstance<Graph>();

				Brush.BrushReadHeight206 heightIn = (Brush.BrushReadHeight206)Generator.Create(typeof(Brush.BrushReadHeight206));
				graph.Add(heightIn);
				heightIn.guiPosition = new Vector2(-270,-100);

				MatrixGenerators.Spot210 fallof = (MatrixGenerators.Spot210)Generator.Create(typeof(MatrixGenerators.Spot210));
				graph.Add(fallof);
				fallof.guiPosition = new Vector2(-270,-20);

				MatrixGenerators.Erosion200 erosion = (MatrixGenerators.Erosion200)Generator.Create(typeof(MatrixGenerators.Erosion200));
				graph.Add(erosion);
				erosion.iterations = 1;
				erosion.guiPosition = new Vector2(-70,-220);
				graph.Link(heightIn, erosion);

				MatrixGenerators.Mask200 mask = (MatrixGenerators.Mask200)Generator.Create(typeof(MatrixGenerators.Mask200));
				graph.Add(mask);
				mask.guiPosition = new Vector2(130,-100);
				mask.invert = true;
				graph.Link(erosion, mask.aIn);
				graph.Link(heightIn, mask.bIn);
				graph.Link(fallof, mask.maskIn);

				Brush.BrushWriteHeight206 heightOut = (Brush.BrushWriteHeight206)Generator.Create(typeof(Brush.BrushWriteHeight206));
				graph.Add(heightOut);
				heightOut.guiPosition = new Vector2(350,-100);
				graph.Link(mask, heightOut);

				return graph;
			}


			public static Graph CreateBrushAddTemplate ()
			{
				Graph graph = GraphInspector.CreateInstance<Graph>();
				graph.name = "AddTemplate";

				Brush.BrushReadHeight206 heightIn = (Brush.BrushReadHeight206)Generator.Create(typeof(Brush.BrushReadHeight206));
				graph.Add(heightIn);
				heightIn.guiPosition = new Vector2(-400, -250);  

				MatrixGenerators.Spot210 fallof = (MatrixGenerators.Spot210)Generator.Create(typeof(MatrixGenerators.Spot210));
				graph.Add(fallof);
				fallof.guiPosition = new Vector2(-400, -450);

				MatrixGenerators.Blend200 blend = (MatrixGenerators.Blend200)Generator.Create(typeof(MatrixGenerators.Blend200));
				graph.Add(blend);
				blend.guiPosition = new Vector2(-150, -350);
				blend.layers = new MatrixGenerators.Blend200.Layer[2] { new MatrixGenerators.Blend200.Layer(), new MatrixGenerators.Blend200.Layer() };
				blend.layers[0].opacity = 1;

				Brush.BrushWriteHeight206 heightOut = (Brush.BrushWriteHeight206)Generator.Create(typeof(Brush.BrushWriteHeight206));
				graph.Add(heightOut);
				heightOut.guiPosition = new Vector2(50, -350);


				graph.Link(heightIn, blend.layers[0]);
				graph.Link(fallof, blend.layers[1]);
				graph.Link(blend, heightOut);


				Group group = new Group();
				graph.groups = new Group[] {group};
				group.guiPos = new Vector2(-450, -520); 
				group.guiSize = new Vector2(670, 350); 
				group.name = "Template Graph";
				group.comment = "Changing it will not be saved";

				return graph;
			}
	}
}