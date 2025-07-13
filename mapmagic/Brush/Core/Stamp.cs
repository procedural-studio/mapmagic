
using UnityEngine;

using Den.Tools;

using MapMagic.Terrains;


namespace MapMagic.Brush
{
	public class Stamp 
	{
		public Vector2D pos;

		public float radius;
		public float hardness;
		public int margins; //TODO: make it world since it always affected by pixelSize. Or radius-related

		public Vector2D Min => pos-radius; //-margins
		public Vector2D Max => pos+radius; //+margins
		public Vector2D Size => new Vector2D(radius*2);




		public Area GetArea (Terrain closestTerrain)
		{
			CoordRect pixelRect = closestTerrain.PixelRect(pos-radius, radius.Vector2D()*2, TerrainControlType.Height); // CoordRect.WorldToPixel(stamp.pos-stamp.radius, stamp.radius.Vector2D()*2, pixelSize);
			if (pixelRect.size.x < pixelRect.size.z) pixelRect.size.x = pixelRect.size.z; //ensuring pixelrect is square (in most cases not)
			if (pixelRect.size.z < pixelRect.size.x) pixelRect.size.z = pixelRect.size.x;

			Vector2D pixelSize = closestTerrain.PixelSize(TerrainControlType.Height);
			Vector2D worldPos = (pixelRect.offset.Vector2D()+0.5f) * pixelSize;  //+0.5 since world grid start from half-pixel
			Vector2D worldSize = (pixelRect.size.Vector2D()-1f) * pixelSize;		//and ends with half-pixel, so there is 1 pixel less

			return new Area(worldPos, worldSize, pixelRect, margins);
		}



	}
}