using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Den.Tools;
using Den.Tools.Lines;
using Den.Tools.Matrices;
using Den.Tools.GUI;
using MapMagic.Core;
using MapMagic.Products;

namespace MapMagic.Nodes.SegsGenerators
{
	[System.Serializable]
	[GeneratorMenu (
		menu="Segs/Standard",  
		name ="Interlink", 
		iconName="GeneratorIcons/Constant",
		colorType = typeof(LineSys), 
		disengageable = true, 
		helpLink ="https://gitlab.com/denispahunov/mapmagic/-/wikis/SplinesGenerators/constant")]
	public class Interlink200 : Generator, IInlet<TransitionsList>, IOutlet<LineSys>
	{
		public override (string, int) GetCodeFileLine () => GetCodeFileLineBase();  //to get here with right-click on generator

		[Val("Input", "Inlet")]		public TransitionsInlet input = new TransitionsInlet();

		[Val("Iterations")]		public int iterations = 8;
		[Val("Max Links")]		public int maxLinks = 4;
		[Val("Within Tile")]	public bool withinTile = true;

		public enum Clamp { Off, Full, Active }
		[Val("Clamp")]	public Clamp clamp;


		public IEnumerable<IInlet<object>> Inlets () { yield return input; }

		public override void Generate (TileData data, StopToken stop)
		{
			TransitionsList objs = data.ReadInletProduct(this);
			if (objs == null || !enabled) return; 

			if (stop != null && stop.stop) return;
			Vector3D[] poses = new Vector3D[objs.count]; //Array.ConvertAll(objs.arr, t=>t.pos); real length is bigger than count
			for (int i=0; i<poses.Length; i++)
				poses[i] = objs.arr[i].pos;

			if (stop != null && stop.stop) return;
			LineSys splineSys = new LineSys();
			splineSys.lines = Line.GabrielGraph(poses, maxLinks:maxLinks, triesPerObj:iterations);

			if (stop != null && stop.stop) return;
			Vector2D cutPos = clamp==Clamp.Full ? data.area.full.worldPos : data.area.active.worldPos;
			Vector2D cutSize = clamp==Clamp.Full ? data.area.full.worldSize : data.area.active.worldSize;

			if (clamp==Clamp.Full || clamp==Clamp.Active)
				for (int s=0; s<splineSys.lines.Length; s++)
				{
					splineSys.lines[s].CutAABB(cutPos, cutSize);
					splineSys.lines[s].RemoveOuterAABB(cutPos, cutSize);
				}

			if (stop != null && stop.stop) return;
			data.StoreProduct(this, splineSys);
		}
	}


	[System.Serializable]
	[GeneratorMenu (menu="Segs/Standard", name ="Stroke", iconName="GeneratorIcons/Constant", disengageable = true, 
		colorType = typeof(LineSys),
		helpLink ="https://gitlab.com/denispahunov/mapmagic/wikis/map_generators/constant")]
	public class Stroke200 : Generator, IInlet<LineSys>, IOutlet<MatrixWorld>
	{
		public override (string, int) GetCodeFileLine () => GetCodeFileLineBase();  //to get here with right-click on generator

		[Val("Width")]	  public float width = 10;
		[Val("Hardness")] public float hardness = 0.0f;

		public override void Generate (TileData data, StopToken stop)
		{
			LineSys splineSys = data.ReadInletProduct(this);
			if (splineSys == null || !enabled) return; 

			//stroking
			if (stop!=null && stop.stop) return;
			MatrixWorld strokeMatrix = new MatrixWorld(data.area.full.rect, data.area.full.worldPos, data.area.full.worldSize, data.globals.height);
			foreach (Line spline in splineSys.lines)
				LineMatrixOps.Stroke(spline, strokeMatrix, white:true, antialiased:true);

			//spreading
			if (stop!=null && stop.stop) return;
			MatrixWorld spreadMatrix = Spread(strokeMatrix, width);

			//hardness
			if (hardness > 0.0001f)
			{
				float h = 1f/(1f-hardness);
				if (h > 9999) h=9999; //infinity if hardness is 1

				spreadMatrix.Multiply(h);
				spreadMatrix.Clamp01();
			}

			if (stop!=null && stop.stop) return;
			data.StoreProduct(this, spreadMatrix);
		}


		public static MatrixWorld Spread (MatrixWorld matrix, float range)
		{
			MatrixWorld spreadMatrix;
			float pixelRange = range / matrix.PixelSize.x;

			if (pixelRange < 1) //if less than a pixel making line less noticable
			{
				spreadMatrix = matrix;
				spreadMatrix.Multiply(pixelRange);
			}

			else //spreading the usual way
			{
				spreadMatrix = new MatrixWorld(matrix);
				MatrixOps.SpreadLinear(matrix, spreadMatrix, subtract:1f/pixelRange);
				
			}

			return spreadMatrix;
		}
	}

	
	/*[System.Serializable]
	[GeneratorMenu (menu="Line/Standard", name ="Pathfinding", iconName=null, disengageable = true, helpLink ="https://gitlab.com/denispahunov/mapmagic/wikis/map_generators/constant")]
	public class Pathfinding200 : Generator, IMultiInlet, IOutlet<LineSys>
	{
		[Val("Draft", "Inlet")]		public SegsInlet draftIn = new SplineInlet();
		[Val("Height", "Inlet")]	public MatrixInlet heightIn = new MatrixInlet();
		public IEnumerable<IInlet<object>> Inlets () { yield return draftIn; yield return heightIn; }

		[Val("Resolution")]			public int resolution = 32;
		[Val("Distance Factor")]		public float distanceFactor = 1f;
		[Val("Elevation Factor")]		public float elevationFactor = 1f;
		[Val("Straighten Factor")]		public float straightenFactor = 1f;
		[Val("Max Elevation")]		public float maxElevation = 0.1f;
		[Val("Max Iterations")]		public int maxIterations = 1000000;

		public Den.Tools.Lines.PolylineMatrixOps.SerpentineFactors factors = new Den.Tools.Lines.PolylineMatrixOps.SerpentineFactors();

		public override void Generate (TileData data, StopToken stop)
		{
			LineSys src = data.ReadInletProduct(draftIn);
			MatrixWorld heights = data.ReadInletProduct(heightIn);
			if (src == null) return; 
			if (heights == null) { data.StoreProduct(this, src; return; }

			if (stop!=null && stop.stop) return;
			MatrixWorld downsampledHeights = new MatrixWorld(new CoordRect(0,0,resolution,resolution), heights.worldPos, heights.worldSize);
			MatrixOps.Resize(heights, downsampledHeights);
			
			if (stop!=null && stop.stop) return;
			LineSys clamped = new LineSys(src); 
			clamped.Clamp(heights.worldPos, heights.worldSize);
			LineSys dst = FindPaths(clamped, downsampledHeights);

			dst.Update();
			if (stop!=null && stop.stop) return;
			data.StoreProduct(this, dst;
		}


		public LineSys FindPaths (LineSys src, MatrixWorld downsampledHeights)
		{
			List<Line> dstLines = new List<Line>();

			Matrix weights = new Matrix(downsampledHeights.rect);
			Matrix2D<Coord> dirs = new Matrix2D<Coord>(downsampledHeights.rect);

			FixedListPathfinding pathfind = new FixedListPathfinding() {
				distanceFactor = distanceFactor,
				elevationFactor = elevationFactor,
				straightenFactor = straightenFactor,
				maxElevation = maxElevation };

			for (int l=0; l<src.lines.Length; l++)
			{
				Line line = src.lines[l];
				List<Vector3> newPath = null;

				for (int s=0; s<line.segments.Length; s++)
				{
					Vector3 fromWorld = line.segments[s].start.pos;
					Vector3 toWorld = line.segments[s].end.pos;

					//checking if this line lays within heights matrix, and clamping all of the line segments out of matrix
					Rect worldRect2D = new Rect(downsampledHeights.worldPos.x, downsampledHeights.worldPos.z, downsampledHeights.worldSize.x, downsampledHeights.worldSize.z);

					Vector2 fromWorld2D = fromWorld.V2(); 
					Vector2 toWorld2D = toWorld.V2();

					if (!worldRect2D.IntersectsLine(fromWorld2D, toWorld2D)) continue;
					worldRect2D.ClampLine(ref fromWorld2D, ref toWorld2D);

					fromWorld = fromWorld2D.V3(); 
					toWorld = toWorld2D.V3();

					//world to coordinates
					Coord fromCoord = downsampledHeights.WorldToPixel(fromWorld.x, fromWorld.z);
					Coord toCoord = downsampledHeights.WorldToPixel(toWorld.x, toWorld.z);

					fromCoord.ClampByRect(downsampledHeights.rect);
					toCoord.ClampByRect(downsampledHeights.rect);

					//pathfinding
					Coord[] pathCoord = pathfind.FindPathDijkstra(fromCoord, toCoord, downsampledHeights, weights, dirs); //Pathfinding.FindPathDijkstraList(fromCoord, toCoord, downsampledHeights, weights, dirs);
					if (pathCoord == null) break;

					//coords to world
					Vector3[] pathWorld = new Vector3[pathCoord.Length];
					for (int i=0; i<pathCoord.Length; i++)
						pathWorld[i] = downsampledHeights.PixelToWorld(pathCoord[i].x, pathCoord[i].z);

					//slightly moving all nodes to make start and end match the src nodes
					//Vector3 startDelta = fromWorld - pathWorld[0];
					//Vector3 endDelta = toWorld - pathWorld[pathWorld.Length-1];
					//for (int i=0; i<pathCoord.Length; i++)
					//{
					//	float percent = 1f * i / (pathWorld.Length-1);
					//	pathWorld[i] += startDelta*(1-percent) + endDelta*percent;
					//}

					//flooring nodes
					//DebugGizmos.Clear("Path");
					pathWorld[0].y = downsampledHeights.GetWorldInterpolatedValue(pathWorld[0].x, pathWorld[1].z) * downsampledHeights.worldSize.y;
					for (int i=0; i<pathWorld.Length-1; i++)
					{
						pathWorld[i+1].y = downsampledHeights.GetWorldInterpolatedValue(pathWorld[i+1].x, pathWorld[i+1].z) * downsampledHeights.worldSize.y;
						//DebugGizmos.AddLine("Path", pathWorld[i], pathWorld[i+1]);
					}

					if (newPath == null) newPath = new List<Vector3>();
					newPath.AddRange(pathWorld);
				}

				if (newPath != null)
				{
					Line newLine = new Line();
					newLine.SetNodes(newPath.ToArray());

					dstLines.Add(newLine);
				}
			}
			
			LineSys dst = new LineSys();
			dst.lines = dstLines.ToArray();

			return dst;
		}
	}*/






	/*[System.Serializable]
	[GeneratorMenu (
		menu="Line/Standard", 
		name ="Align", 
		iconName="GeneratorIcons/Constant",
		colorType = typeof(LineSys), 
		disengageable = true, 
		helpLink ="https://gitlab.com/denispahunov/mapmagic/wikis/map_generators/constant")]
	public class Align200 : Generator, IMultiInlet, IOutlet<MatrixWorld>
	/// Flattens the land along the line
	/// Outdated version of Stamp
	{
		[Val("Line", "Inlet")]	public SegsInlet splineIn = new SplineInlet();
		[Val("Height", "Inlet")]	public MatrixInlet heightIn = new MatrixInlet();
		public IEnumerable<IInlet<object>> Inlets () { yield return splineIn; yield return heightIn; }

		[Val("Range")] public float range = 30;
		[Val("Flat")] public float flat = 0.25f;
		[Val("Detail")] public float detail = 0f;


		public override void Generate (TileData data, StopToken stop)
		{
			LineSys splineSys = data.ReadInletProduct(splineIn);
			MatrixWorld heightMatrix = data.ReadInletProduct(heightIn);
			if (splineSys == null) return;
			if (!enabled || heightMatrix == null) { data.StoreProduct(this, splineSys; return; }

			if (stop!=null && stop.stop) return;
			MatrixWorld splineMatrix = Stamp(heightMatrix, splineSys, stop);

			if (stop!=null && stop.stop) return;
			data.StoreProduct(this, splineMatrix;
		}


		private MatrixWorld Stamp (MatrixWorld srcHeights, LineSys splineSys, StopToken stop)
		{
			//contours matrix
			if (stop!=null && stop.stop) return null;
			MatrixWorld lineContours = new MatrixWorld(srcHeights.rect, srcHeights.worldPos, srcHeights.worldSize);
			foreach (Line spline in splineSys.lines)
				PolylineMatrixOps.Stroke(spline, lineContours, white:true, antialiased:true);


			//line heights matrix
			if (stop!=null && stop.stop) return null;
			MatrixWorld lineHeightsSrc = new MatrixWorld(srcHeights.rect, srcHeights.worldPos, srcHeights.worldSize);
			foreach (Line spline in splineSys.lines)
				PolylineMatrixOps.Stroke(spline, lineHeightsSrc, padOnePixel:true);
			MatrixWorld lineHeights = new MatrixWorld(lineHeightsSrc); //TODO: use same src/dst matrix in padding
			MatrixOps.PaddingMipped(lineHeightsSrc, lineContours, lineHeights);


			//distances matrix
			if (stop!=null && stop.stop) return null;
			MatrixWorld lineDistances = new MatrixWorld(lineContours);

			float pixelRange = range / srcHeights.PixelSize.x;

			if (pixelRange < 1) //if less than a pixel making line less noticable
				lineDistances.Multiply(pixelRange);
			else
				MatrixOps.Spread(lineContours, lineDistances, subtract:1f/pixelRange);
			

			//saving detail matrix if detail is used (and then operating on lower-detail)
			if (stop!=null && stop.stop) return null;
			MatrixWorld detailMatrix = null;
			if (detail > 0.00001f)
			{
				int downsample = (int)(detail+1);
				float blur = (detail+1) - downsample;

				MatrixWorld originalHeights = srcHeights;
				srcHeights = new MatrixWorld(srcHeights); //further operating on blurred matrix
				MatrixOps.DownsampleBlur(srcHeights, downsample, blur);

				detailMatrix = new MatrixWorld(srcHeights); //taking blurred matrix
				detailMatrix.InvSubtract(originalHeights); //and subtracting src (non-blurred) from it
			}


			//blending line heights with terrain heights
			if (stop!=null && stop.stop) return null;
			for (int i=0; i<srcHeights.arr.Length; i++)  //TODO: replace with matrix mix
			{
				float dist = lineDistances.arr[i];
				if (dist == 0) { lineHeights.arr[i] = srcHeights.arr[i]; continue; }
				if (1-dist < flat) continue;

				float percent = dist / (1-flat);
				percent = 3*percent*percent - 2*percent*percent*percent;

				lineHeights.arr[i] = lineHeights.arr[i]*percent + srcHeights.arr[i]*(1-percent);
			}

			//applying detail
			if (detailMatrix != null)
				lineHeights.Add(detailMatrix);
			
			return lineHeights;
		}
	}*/


	[System.Serializable]
	[GeneratorMenu (
		menu="Segs/Standard", 
		name ="Stamp", 
		iconName="GeneratorIcons/Constant", 
		colorType = typeof(LineSys), 
		disengageable = true, 
		helpLink ="https://gitlab.com/denispahunov/mapmagic/wikis/map_generators/constant")]
	public class Stamp200 : Generator, IMultiInlet, IOutlet<MatrixWorld>
	{
		public override (string, int) GetCodeFileLine () => GetCodeFileLineBase();  //to get here with right-click on generator

		[Val("Spline", "Inlet")]	public SegsInlet splineIn = new SegsInlet();
		[Val("Height", "Inlet")]	public MatrixInlet heightIn = new MatrixInlet();
		public IEnumerable<IInlet<object>> Inlets () { yield return splineIn; yield return heightIn; }

		public enum Algorithm { Flatten, Detail, Both };
		public Algorithm algorithm;
		public float flatRange = 2;
		public float blendRange = 16;
		public float detailRange = 32;
		public int detail = 1;


		public override void Generate (TileData data, StopToken stop)
		{
			LineSys splineSys = data.ReadInletProduct(splineIn);
			MatrixWorld heightMatrix = data.ReadInletProduct(heightIn);
			if (splineSys == null ||  heightMatrix == null) return;
			if (!enabled) { data.StoreProduct(this, heightMatrix); return; }

			if (stop!=null && stop.stop) return;
			MatrixWorld splineMatrix = Stamp(heightMatrix, splineSys, stop);

			if (stop!=null && stop.stop) return;
			data.StoreProduct(this, splineMatrix);

			if (splineMatrix.ContainsNaN()) Debug.Log("NaN");
		}


		private MatrixWorld Stamp (MatrixWorld srcHeights, LineSys splineSys, StopToken stop)
		{
			MatrixWorld dstHeights = new MatrixWorld(srcHeights);

			//transforming ranges to relative (0-1)
			float maxRange = Mathf.Max(detailRange, blendRange);
			float relDetailRange = 1 - detailRange/maxRange;
			float relBlendRange = 1 - blendRange/maxRange;
			float relFlatRange = 1 - flatRange/maxRange;

			//contours matrix
			if (stop!=null && stop.stop) return null;
			MatrixWorld lineContours = new MatrixWorld(srcHeights.rect, srcHeights.worldPos, srcHeights.worldSize);
			foreach (Line spline in splineSys.lines)
				LineMatrixOps.Stroke(spline, lineContours, white:true, antialiased:true);


			//line heights matrix
			if (stop!=null && stop.stop) return null;
			MatrixWorld lineHeightsSrc = new MatrixWorld(srcHeights.rect, srcHeights.worldPos, srcHeights.worldSize);
			foreach (Line spline in splineSys.lines)
				LineMatrixOps.Stroke(spline, lineHeightsSrc, padOnePixel:true);
			MatrixWorld lineHeights = new MatrixWorld(lineHeightsSrc); //TODO: use same src/dst matrix in padding
			MatrixOps.PaddingMipped(lineHeightsSrc, lineContours, lineHeights);


			//distances matrix
			if (stop!=null && stop.stop) return null;
			MatrixWorld lineDistances = new MatrixWorld(lineContours);

			float pixelRange = maxRange / srcHeights.PixelSize.x;

			if (pixelRange < 1) //if less than a pixel making line less noticeable
				lineDistances.Multiply(pixelRange);
			else
				MatrixOps.SpreadLinear(lineContours, lineDistances, subtract:1f/pixelRange);
			

			//applying detail
			if (stop!=null && stop.stop) return null;
			if (algorithm == Algorithm.Detail || algorithm == Algorithm.Both)
			{
				int downsample = (int)(detail+1);
				float blur = (detail+1) - downsample;
				MatrixOps.DownsampleBlur(dstHeights, detail, 1);
				//MatrixOps.GaussianBlur(dstHeights, detail);

				MatrixWorld detailMatrix = new MatrixWorld(dstHeights);  //taking blurred matrix
				detailMatrix.InvSubtract(srcHeights);	//and subtracting non-blurred from it

				dstHeights.Mix(lineHeights, lineDistances, 0, relDetailRange, maskInvert:false, fallof:false, opacity:1);  //stamping on blurred

				dstHeights.Add(detailMatrix); //and returning details back
			}


			//applying fallof
			if (stop!=null && stop.stop) return null;
			if (algorithm == Algorithm.Flatten || algorithm == Algorithm.Both)
			{
				dstHeights.Mix(lineHeights, lineDistances, relBlendRange, relFlatRange, maskInvert:false, fallof:false, opacity:1);
			}

			return dstHeights;
		}
	}



	[System.Serializable]
	[GeneratorMenu (menu="Segs/Standard", name ="Optimize", iconName=null, disengageable = true, helpLink ="https://gitlab.com/denispahunov/mapmagic/wikis/map_generators/constant")]
	public class Optimize200 : Generator, IInlet<LineSys>, IOutlet<LineSys>
	{
		public override (string, int) GetCodeFileLine () => GetCodeFileLineBase();  //to get here with right-click on generator

		[Val("Beizer")] public bool beizer = false;
		[Val("Split")] public int split = 3;
		[Val("Deviation")] public float deviation = 1;

		public override void Generate (TileData data, StopToken stop)
		{
			LineSys src = data.ReadInletProduct(this);
			if (src == null) return;
			if (!enabled) { data.StoreProduct(this, src); return; }
			
			if (stop!=null && stop.stop) return;
			LineSys dst = new LineSys(src);

			if (beizer)
				foreach (Line spline in dst.lines)
			{
				BeizerOps.Subdivide(spline, split);
				BeizerOps.Optimize(spline, deviation);
			}

			else
				foreach (Line spline in dst.lines)
					spline.Optimize(deviation);
			
			if (stop!=null && stop.stop) return;
			data.StoreProduct(this, dst);
		}
	}


	[System.Serializable]
	[GeneratorMenu (menu="Segs/Standard", name ="Relax", iconName=null, disengageable = true, helpLink ="https://gitlab.com/denispahunov/mapmagic/wikis/map_generators/constant")]
	public class Relax200 : Generator, IInlet<LineSys>, IOutlet<LineSys>
	{
		public override (string, int) GetCodeFileLine () => GetCodeFileLineBase();  //to get here with right-click on generator

		[Val("Spline", "Inlet")]	public SegsInlet splineIn = new SegsInlet();

		[Val("Blur")] public float blur = 1;
		[Val("Iterations")] public int iterations = 3;

		public override void Generate (TileData data, StopToken stop)
		{
			LineSys src = data.ReadInletProduct(this);
			if (src == null) return;
			if (!enabled) { data.StoreProduct(this, src); return; }
			
			if (stop!=null && stop.stop) return;
			LineSys dst = new LineSys(src);
			foreach (Line spline in dst.lines)
				spline.Relax(blur, iterations);
			
			if (stop!=null && stop.stop) return;
			data.StoreProduct(this, dst);
		}
	}


	[System.Serializable]
	[GeneratorMenu (menu="Segs/Standard", name ="Weld Close", iconName=null, disengageable = true, helpLink ="https://gitlab.com/denispahunov/mapmagic/wikis/map_generators/constant")]
	public class WeldClose200 : Generator, IInlet<LineSys>, IOutlet<LineSys>
	{
		public override (string, int) GetCodeFileLine () => GetCodeFileLineBase();  //to get here with right-click on generator

		[Val("Spline", "Inlet")]	public SegsInlet splineIn = new SegsInlet();

		[Val("Threshold")] public float threshold = 1;

		public override void Generate (TileData data, StopToken stop)
		{
			LineSys src = data.ReadInletProduct(this);
			if (src == null) return;
			if (!enabled) { data.StoreProduct(this, src); return; }
			
			if (stop!=null && stop.stop) return;
			LineSys dst = new LineSys(src);
			dst.lines = Line.WeldClose(dst.lines, threshold);
			
			if (stop!=null && stop.stop) return;
			data.StoreProduct(this, dst);
		}
	}


	[System.Serializable]
	[GeneratorMenu (menu="Segs/Standard", name ="Floor", iconName=null, disengageable = true, helpLink ="https://gitlab.com/denispahunov/mapmagic/wikis/map_generators/constant")]
	public class Floor200 : Generator, IMultiInlet, IOutlet<LineSys>
	{
		public override (string, int) GetCodeFileLine () => GetCodeFileLineBase();  //to get here with right-click on generator

		[Val("Spline", "Inlet")] public SegsInlet splineIn = new SegsInlet();
		[Val("Spline", "Height")] public MatrixInlet heightIn = new MatrixInlet();
		public IEnumerable<IInlet<object>> Inlets () { yield return splineIn; yield return heightIn; }

		public override void Generate (TileData data, StopToken stop)
		{
			LineSys src = data.ReadInletProduct(splineIn);
			MatrixWorld heights = data.ReadInletProduct(heightIn);
			if (src == null) return;
			if (!enabled || heights==null) { data.StoreProduct(this, src); return; }
			
			if (stop!=null && stop.stop) return;
			LineSys dst = new LineSys(src);

			foreach (Line spline in dst.lines)
				LineMatrixOps.Floor(heights, spline, interpolated:true);
			
			if (stop!=null && stop.stop) return;
			data.StoreProduct(this, dst);
		}
	}


	[System.Serializable]
	[GeneratorMenu (menu="Segs/Standard", name ="Avoid", iconName=null, disengageable = true, helpLink ="https://gitlab.com/denispahunov/mapmagic/wikis/map_generators/constant")]
	public class Push200 : Generator, IMultiInlet, IMultiOutlet //IOutlet<LineSys>
	{
		public override (string, int) GetCodeFileLine () => GetCodeFileLineBase();  //to get here with right-click on generator

		[Val("Spline", "Spline")] public SegsInlet splineIn = new SegsInlet();
		[Val("Spline", "Positions")] public TransitionsInlet objsIn = new TransitionsInlet();
		public IEnumerable<IInlet<object>> Inlets () { yield return splineIn; yield return objsIn; }

		[Val("Spline", "Spline")] public SegsOutlet splineOut = new SegsOutlet();
		[Val("Spline", "Positions")] public TransitionsOutlet objsOut = new TransitionsOutlet();
		public IEnumerable<IOutlet<object>> Outlets () { yield return splineOut; yield return objsOut; }

		[Val("Distance")] public float distance = 10;
		[Val("Nodes/Objects")] public float nodesObjectsRatio = 0.5f;
		[Val("Size Factor")] public int sizeFactor = 0;
		[Val("Iterations")] public int iterations = 5;

		public override void Generate (TileData data, StopToken stop)
		{
			LineSys sps = data.ReadInletProduct(splineIn);
			TransitionsList objs = data.ReadInletProduct(objsIn);
			if (sps == null) return;
			if (!enabled || objs==null || sps==null) { data.StoreProduct(splineOut,sps); data.StoreProduct(objsOut,objs); return; }
			
			if (stop!=null && stop.stop) return;
			LineSys dstSps = new LineSys(sps);
			TransitionsList dstObjs = new TransitionsList(objs);

			Vector3D[] points = new Vector3D[objs.count];
			float[] ranges = new float[objs.count];

			for (int o=0; o<objs.count; o++)
			{
				points[o] = objs.arr[o].pos;
				ranges[o] = distance * (1-sizeFactor + objs.arr[o].scale.x*sizeFactor);
			}

			if (stop!=null && stop.stop) return;
			if (nodesObjectsRatio < 0.5f)
				foreach (Line spline in dstSps.lines)
					spline.SplitNearPoints(points, ranges, horizontalOnly:true);

			for (int i=0; i<iterations; i++)
			{
				if (stop!=null && stop.stop) return;
				foreach (Line spline in dstSps.lines)
				{
					float intensity = (float)(i+1) / iterations;
					spline.Push(points, ranges, intensity:intensity, nodesPointsRatio:nodesObjectsRatio, horizontalOnly:true);
				}
			}

			for (int o=0; o<dstObjs.count; o++)
				dstObjs.arr[o].pos = points[o];

			if (stop!=null && stop.stop) return;
			data.StoreProduct(splineOut, dstSps);
			data.StoreProduct(objsOut, dstObjs);
		}
	}


	[System.Serializable]
	[GeneratorMenu (menu="Segs/Standard", name ="Isoline", iconName=null, disengageable = true, 
		colorType = typeof(LineSys),
		helpLink ="https://gitlab.com/denispahunov/mapmagic/wikis/map_generators/constant")]
	public class Isoline200 : Generator, IInlet<MatrixWorld>, IOutlet<LineSys>
	{
		public override (string, int) GetCodeFileLine () => GetCodeFileLineBase();  //to get here with right-click on generator

		public enum LevelType { Relative, Absolute }
		[Val("Level Type")] public LevelType levelType = LevelType.Relative;
		[Val("Level")] public float level = 10;
		[Val("Detail")] public float detail = 0.5f;
		//[Val("Relax Iterations")] public float relaxIterations = 10;
		[Val("Downscale")] public float downscale = 1;

		public override void Generate (TileData data, StopToken stop)
		{
			MatrixWorld matrix = data.ReadInletProduct(this);
			if (matrix == null || !enabled) return; 

			float curLevel = levelType==LevelType.Relative ? level : level/data.globals.height;

			//downscaling
			int maxDownscale = (int)Mathf.Log(data.area.active.rect.size.x-1,2) + 1;
			int limitDownscale = (int)Mathf.Log(32,2) + 1;
			int downscale = (int)((maxDownscale-limitDownscale)*(1-detail));

			MatrixWorld downscaledMatrix = matrix;
			for (int i=0; i<downscale; i++)
			{
				MatrixWorld newDownscaledMatrix = new MatrixWorld(new CoordRect(downscaledMatrix.rect.offset, downscaledMatrix.rect.size/2), matrix.worldPos, matrix.worldSize);
				MatrixOps.DownscaleFast(downscaledMatrix, newDownscaledMatrix);
				downscaledMatrix = newDownscaledMatrix; 
			}

			//isolines
			float relaxIterations = (1-detail*detail) * 10f; //from 0 to 10;
			Line[] splines = LineMatrixOps.Isoline(downscaledMatrix, curLevel);
			LineSys splineSys = new LineSys(splines);

			//optimization
			foreach (Line spline in splineSys.lines)
			{
				spline.Relax(0.5f, (int)relaxIterations);
				spline.Optimize(relaxIterations*0.5f);
			}

			if (stop!=null && stop.stop) return;
			data.StoreProduct(this, splineSys);
		}
	}


	[System.Serializable]
	[GeneratorMenu (menu="Segs/Standard", name ="Silhouette", iconName=null, disengageable = true, 
		colorType = typeof(LineSys),
		helpLink ="https://gitlab.com/denispahunov/mapmagic/wikis/map_generators/constant")]
	public class Silhouette200 : Generator, IInlet<LineSys>, IOutlet<MatrixWorld>
	{
		public override (string, int) GetCodeFileLine () => GetCodeFileLineBase();  //to get here with right-click on generator

		[Val("Width")] public float width = 10;
		//[Val("Size Factor")] public int sizeFactor = 0;
		//[Val("Iterations")] public int iterations = 10;

		public override void Generate (TileData data, StopToken stop)
		{
			LineSys splineSys = data.ReadInletProduct(this);
			if (splineSys == null || !enabled) return; 

			//stroke and spread
			if (stop!=null && stop.stop) return;
			MatrixWorld strokeMatrix = new MatrixWorld(data.area.full.rect, data.area.full.worldPos, data.area.full.worldSize, data.globals.height);
			foreach (Line spline in splineSys.lines)
				LineMatrixOps.Stroke (spline, strokeMatrix, white:true, intensity:0.5f, antialiased:true);

			MatrixWorld spreadMatrix = new MatrixWorld(data.area.full.rect, data.area.full.worldPos, data.area.full.worldSize, data.globals.height);
			MatrixOps.SpreadLinear(strokeMatrix, spreadMatrix, subtract: spreadMatrix.PixelSize.x/width / 2);

			//silhouette
			if (stop!=null && stop.stop) return;
			MatrixWorld silhouetteMatrix = new MatrixWorld(data.area.full.rect, data.area.full.worldPos, data.area.full.worldSize, data.globals.height);
			foreach (Line spline in splineSys.lines)
				LineMatrixOps.Stroke (spline, silhouetteMatrix, white:true, intensity:0.5f, antialiased:false, padOnePixel:false);
			
			if (stop!=null && stop.stop) return;
			LineMatrixOps.Silhouette(splineSys.lines, silhouetteMatrix, silhouetteMatrix);  

			//combining
			if (stop!=null && stop.stop) return;
			LineMatrixOps.CombineSilhouetteSpread(silhouetteMatrix, spreadMatrix, silhouetteMatrix);

			if (stop!=null && stop.stop) return;
			data.StoreProduct(this, silhouetteMatrix); 
		}
	}



}
