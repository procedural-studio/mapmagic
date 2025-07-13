using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

using Den.Tools;
using Den.Tools.Matrices;
using Den.Tools.Splines;
using Den.Tools.Tasks;
using MapMagic.Core;
using MapMagic.Products;
using MapMagic.Nodes;
using MapMagic.Nodes.Biomes;
using MapMagic.Terrains;

using System.Threading;

namespace MapMagic.Clusters
{
	public partial class Cluster
	/// MapMagic object with no terrains and one tile that outputs results to dictionary
	/// And no Apply, that simplyfies things a bit
	{ 
		private static System.Random random = new System.Random();


		private class ProductSet
		{
			public Dictionary<ulong,object> outletProduct = new Dictionary<ulong, object>();

			public MatrixWorld GetAnyMatrixForTest ()
			{
				foreach (var kvp in outletProduct)
					if (kvp.Value is MatrixWorld mw)
						return mw;
				return null;
			}

			//public ulong startedVersion;
			//public bool isGenerating;
			//public ulong generatedVersion;
			//public List<ThreadManager.Task> tasksToResumeOnGenerated = new List<ThreadManager.Task>();
			public StopToken stop;
		}

		[NonSerialized] private Dictionary<Coord,ProductSet> products = new Dictionary<Coord,ProductSet>();


//		[NonSerialized] private GeneratingList generatingList = new GeneratingList();
//		[NonSerialized] private PauseQueue pauseQueue = new PauseQueue();
//		[NonSerialized] private object lockObj = new object(); //access to generatingList and pauseQueue should be done under lock

		//[NonSerialized] private Dictionary<ThreadManager.Task, List<Coord>> pausedTasks = new Dictionary<ThreadManager.Task, List<Coord>>();
		//paused tasks and list of coords they are waiting to be finished
		



			/*private ProductSet GetAddProductSet (Coord coord)
			///Reads product set - or creates new one if it doesn;t exist
			{
				ProductSet productSet;
				if (!products.TryGetValue(coord, out productSet))
				{
					productSet = new ProductSet();
					products.Add(coord, productSet);
				}

				return productSet;
			}*/

		public Vector2D tileSize = new Vector2D(3000,3000);
		public Vector2D offset = new Vector2D(0,0);
		public MapMagicObject.Resolution resolution = MapMagicObject.Resolution._513;
		public int margins = 16;

		public Globals globals = new Globals();

		private ulong settingsVersion = 1;
		public void ResetUpToDate () => settingsVersion++;

		public static Action<Cluster> OnClusterGenerated;

		

		private void StoreProductsFromData (ProductSet productSet, Graph graph, TileData data)
		///Copies all products from cluster generated data to this
		{
			HashSet<ulong> usedIds = new HashSet<ulong>(); //to remove unused products (do not clear products - it could be read)
			//TODO: remove unused products then!

			//copying products to cluster
			foreach (IFnExit<object> exit in graph.GeneratorsOfType<IFnExit<object>>())
			{
				string name = exit.Name;

				object product = data.ReadInletProduct(exit);
				if (product == null) 
					continue;

				productSet.outletProduct.ForceAdd(exit.Id, product);
			}
		}

		private void CheckTasksPaused ()
		//just in case checking that if we don't have generating coords - all paused ones should be resumed
		{
			//TODO: move to puseQueue and implement!

			/*bool isGeneratingAnything = false;

			foreach (var kvp in products)
			{
				if (kvp.Value.isGenerating)
					isGeneratingAnything = true;
			}

			if (!isGeneratingAnything)
			{
				foreach (ThreadManager.Task pausedTask in pausedTasks.Keys)
				{
					Debug.LogError("There are paused tasks while all generated");
					ThreadManager.Resume(pausedTask);
				}

				pausedTasks.Clear();
			}*/
		}


		public object GetCroppedMergedProduct (ulong outletId, Type outletType, Area area, float height, StopToken stop)
		///Finds products based on area (could be several if area is between tiles),
		///Crops them and merges in one
		{
			CoordRect clusterCoords = ClusterCoordsByArea(area);
			
//			Type productType = GetProductsType(id);

			//Matrix
			if (outletType == typeof(MatrixWorld))
			{
				MatrixWorld dstMatrix = new MatrixWorld(area.full.rect, area.full.worldPos, area.full.worldSize, height);
				MatrixWorld[] clusterMatrices = ProductsInCoordRect<MatrixWorld>(outletId, clusterCoords);

				GetCroppedMergedMatrix(clusterMatrices, margins, dstMatrix, stop);

				return dstMatrix;
			}

			//Objects
			if (outletType == typeof(MatrixWorld))
			{
				TransitionsList dstObjects = new TransitionsList();
				TransitionsList[] srcObjects = ProductsInCoordRect<TransitionsList>(outletId, clusterCoords);

				GetCroppedMergedObjects(srcObjects, dstObjects, area.full.worldPos, area.full.worldSize, stop);

				return dstObjects;  
			}

			return null;
		}


		private static void GetCroppedMergedMatrix (MatrixWorld[] srcs, int srcMargins, MatrixWorld dst, StopToken stop)
		{
			Matrix tmp = null; //temporary matrix used for resize (height src, width dst)
			foreach (MatrixWorld src in srcs)
			//to test use //MatrixWorld src = srcs[0]; you may also want to reduce worldSrcMargins
			//MatrixWorld src = srcs[0];
			{
				if (stop!=null && stop.stop) return;

				#if MM_DEBUG
				Log.AddThreaded("Cluster.ImportMatricesWithEnlarge", ("pos:",dst.worldPos), ("value:",src.arr[100]));
				#endif

				float worldSrcMargins = srcMargins * (src.PixelSize.x);
				Vector2D srcWorldPos = src.worldPos.Vector2D() + worldSrcMargins;
				Vector2D srcWorldSize = src.worldSize.Vector2D() - worldSrcMargins*2;

				MatrixOps.CopyInterpolated(src, dst, srcWorldPos, srcWorldSize, ref tmp);
			}
		}


		private static void GetCroppedMergedObjects (TransitionsList[] srcs, TransitionsList dst, Vector2D dstOffset, Vector2D dstSize, StopToken stop)
		{
			foreach (TransitionsList src in srcs)
			{
				if (stop!=null && stop.stop) return;

				TransitionsList.CopyTransitions(src, dst, dstOffset, dstSize);
			}
		}


		private T[] ProductsInCoordRect<T> (ulong outletId, CoordRect coordRect) where T:class
		{
			//TODO: check if mode is really T 

			T[] res = new T[coordRect.Count];

			Coord min = coordRect.Min;
			Coord max = coordRect.Max;
			int i = 0;
			for (int x = min.x; x < max.x; x++)
				for (int z = min.z; z < max.z; z++)
				{
					Coord coord = new Coord(x,z);
					ProductSet set = products[coord];

					if (set.outletProduct.TryGetValue(outletId, out object product))
						res[i] = product as T;
					else
						throw new Exception("Cluster: Could not find product for coord "+x+", "+z);
					i++;
				}

			return res;
		}





		public void ClearProducts ()
		{
			products.Clear();
		}


		private CoordRect ClusterCoordsByArea (Area area, bool checkFull=false)
		{
			Area.Dimensions dim = checkFull ? area.full : area.active;

			Vector2D min = dim.worldPos;
			Vector2D max = dim.worldPos + dim.worldSize - 0.1f; //max of 5000 will think it's coord 5

			Coord minCoord = Coord.PickCellByPos(min.x, min.z, tileSize);
			Coord maxCoord = Coord.PickCellByPos(max.x, max.z, tileSize);
			Coord size = maxCoord-minCoord;
			size.x++; size.z++; //making rect inclusive

			return new CoordRect(minCoord, size);
		}
	}
}