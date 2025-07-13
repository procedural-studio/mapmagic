using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

using Den.Tools;
using Den.Tools.GUI;
using Den.Tools.SceneEdit;
using UnityEditor;
using MapMagic.Previews;
using static UnityEngine.Mesh;
using static Den.Tools.SemVer;
using UnityEngine.Rendering;
using UnityEngine.UIElements;


namespace MapMagic.Nodes.GUI
{
	public static class LinkDraw
	{
		private static Mesh schemeMesh;
		private static Mesh loMesh;
		private static Mesh midMesh;
		private static Mesh hiMesh;

		private static Material mat;
		private static int[] matPropertyIds = null;
		private static MaterialPropertyBlock matProps = null;


		public static void DrawLink (Vector2 start, Vector2 end, Color color, bool scheme=false) =>
			LinkDraw.DrawLink(start, end, color, 
					radius:100,
					width:4f*UI.current.scrollZoom.SoftZoom,
					scheme:scheme);
		

		public static void DrawLink (Vector2 start, Vector2 end, Color color, float radius, float width, bool scheme=false)
			{ using (ProfilerExt.Profile("Calculating Links"))
		{
			if (Event.current.type != EventType.Repaint  &&  UI.current.layout)	
				return;

			if (UI.current.optimizeElements)
			{
				Rect rect = new Rect(start, Vector2.zero);
				rect = rect.Encapsulate(end);
				rect.Extended(radius);

				if (!UI.current.IsInWindow(rect.position, rect.size)) 
					return;
			}


			//usual params
			Vector2 startCircle = start;
			Vector2 endCircle = end;
			float startToPercent = 0; //percent of the arc filled
			float endToPercent = 0;

			bool mirrored = start.y > end.y; //mirror vertically if it's going UP from left to right
			float dist = (end-start).magnitude;
			Vector2 dir = (end-start) / dist;
			float horDelta = end.x-start.x;
			float verDelta = Mathf.Abs(start.y-end.y);


			//modifying arcs and start/end 
			if (!scheme  &&
				verDelta > 1) //sometimes creates unncessary circles when in one line. Replacing with straight.
			{
				//reducing radius on facing too backwards
				float dirAngle = Mathf.Atan2(dir.y, dir.x); //y  and x are swapped to point it right. 0 is "straight" link, pi is backwards
				float backFactor = 1 - Mathf.Abs(dirAngle) / Mathf.PI;
				radius *= backFactor; //main smoothness factor

				//reducing radius if start and end are too close
				float distFactor = 1 - 1 / ((dist+radius) / radius);
				radius *= distFactor;

				//offseting up and down
				float circleOffset = mirrored ? -1 : 1;
				startCircle.y = start.y + circleOffset*radius; 
				endCircle.y = end.y - circleOffset*radius;

				//reducing again to prevent intersection (this will also give additional distance, that is even better)
				for (int i=0; i<2; i++)
				{
					if ((startCircle-endCircle).magnitude < radius*2.2f) //adding additional 0.2 threshold
					{
						radius = (startCircle-endCircle).magnitude / 2.2f;
						startCircle.y = start.y + circleOffset*radius; 
						endCircle.y = end.y - circleOffset*radius;
					}
				}

				//finding straight line
				//adjusting start/end so that it's always closest to straight line's final positions
				if (start.x > end.x) //backward-going s-curve
				{
					if (mirrored)
						{ start.y -= radius*2; end.y += radius*2; }
					else
						{ start.y += radius*2; end.y -= radius*2; }
				}
				else if (mirrored && (startCircle.y > endCircle.y))
					{ start.x += radius; end.x -= radius; }
				else if (!mirrored && (startCircle.y < endCircle.y))
					{ start.x += radius; end.x -= radius; }

				//Debugging final start/end positions
				//Handles.color = Color.red;
				//Handles.DrawLineObject(UI.current.scrollZoom.ToScreen(start), UI.current.scrollZoom.ToScreen(end));

				//placing start and end on their circles
				PlaceDotsOnCircle (ref start, ref end, startCircle, endCircle, radius);
				PlaceDotsOnCircle (ref start, ref end, startCircle, endCircle, radius); //two iterations: after end modified line changed and start will be inside circle
				PlaceDotsOnCircle (ref start, ref end, startCircle, endCircle, radius); //okay, third iteration to remove issues where arcs are too close

				//"from" angle is 0, finding "to" angle in each circle
				Vector2 startToDir = (startCircle-start).normalized;
				float startToRadians = Mathf.Atan2(startToDir.x, startToDir.y);
				startToPercent = startToRadians/(Mathf.PI*2);
				if (!mirrored  &&  startCircle.x > endCircle.x  &&  start.y > end.y)
					startToPercent -= 1;

				Vector2 endToDir = (endCircle-end).normalized;
				float endToRadians = Mathf.Atan2(endToDir.x, endToDir.y);
				endToPercent = endToRadians/(Mathf.PI*2);
				if (mirrored  &&  startCircle.x > endCircle.x  &&  start.y < end.y)
					endToPercent += 1; 

				if (mirrored)
				{
					startToPercent = 0.5f + startToPercent;
					endToPercent = 0.5f + startToPercent;
				}
			}

			else //if scheme - doing same for the straight line
			{
				Vector2 perpDir = new Vector2(dir.y, -dir.x);

				float radians = Mathf.Atan2(perpDir.x, perpDir.y);
				endToPercent =radians/(Mathf.PI*2);
				startToPercent = endToPercent + 0.5f;

				radius = 0;
			}

			//Debugging circles
			//Handles.color = Color.gray;
			//Handles.DrawWireArc(UI.current.scrollZoom.ToScreen(startCircle), new Vector3(0,0,1), new Vector2(0,1), 360, radius*UI.current.scrollZoom.zoom.x);
			//Handles.DrawWireArc(UI.current.scrollZoom.ToScreen(endCircle), new Vector3(0,0,1), new Vector2(0,1), 360, radius*UI.current.scrollZoom.zoom.x);
			//Handles.DrawWireArc(UI.current.scrollZoom.ToScreen(startCircle), new Vector3(0,0,1), new Vector2(0,1), 360, 1);
			//Handles.DrawWireArc(UI.current.scrollZoom.ToScreen(endCircle), new Vector3(0,0,1), new Vector2(0,1), 360, 1);

			//Handles.color = Color.green;
			//Handles.DrawLineObject(UI.current.scrollZoom.ToScreen(start), UI.current.scrollZoom.ToScreen(end));


			//material block
			using (ProfilerExt.Profile("Links Material"))
			{
				if (mat == null)
					mat = new Material( Shader.Find("MapMagic/LinkShader") ); 

				if (matPropertyIds == null)
					matPropertyIds = Draw.GetMatPropertyIds("_ClipRect", "_Color", "_StartArc", "_EndArc", "_Width", "_Mirror", "_AAWidth");

				mat.SetVector(matPropertyIds[0], Draw.GetClipRect());  //_ClipRect
				mat.SetColor(matPropertyIds[1], color); //_Color
				mat.SetVector(matPropertyIds[2], new Vector4(startCircle.x, startCircle.y, mirrored ? -radius : radius, startToPercent)); //_StartArc
				mat.SetVector(matPropertyIds[3], new Vector4(endCircle.x, endCircle.y, mirrored ? -radius : radius, endToPercent)); //_EndArc
				mat.SetFloat(matPropertyIds[4], Mathf.Max(1.01f,width)); //_Width //crisp edges occur instead of transparency when size is < 1
				mat.SetFloat(matPropertyIds[5], mirrored ? -1 : 1); //_Mirror
				mat.SetFloat(matPropertyIds[6], UI.current.scrollZoom.zoom.x * UI.current.dpiScaleFactor); //_AAWidth

				/*if (matProps == null)
					matProps = new MaterialPropertyBlock();

				matProps.SetVector(matPropertyIds[0], Draw.GetClipRect());  //_ClipRect
				matProps.SetColor(matPropertyIds[1], color); //_Color
				matProps.SetVector(matPropertyIds[2], new Vector4(startCircle.x, startCircle.y, mirrored ? -radius : radius, startToPercent)); //_StartArc
				matProps.SetVector(matPropertyIds[3], new Vector4(endCircle.x, endCircle.y, mirrored ? -radius : radius, endToPercent)); //_EndArc
				matProps.SetFloat(matPropertyIds[4], Mathf.Max(1.01f, width)); //_Width
				matProps.SetFloat(matPropertyIds[5], mirrored ? -1 : 1); //_Mirror
				matProps.SetFloat(matPropertyIds[6], UI.current.scrollZoom.zoom.x * UI.current.dpiScaleFactor); //_AAWidth*/


				//all coordinates are ui-space, and transforming to screen with mesh's transform
			}

			if (loMesh == null)
				loMesh = CreateMesh(11);
			if (midMesh == null)
				midMesh = CreateMesh(21);
			if (hiMesh == null)
				hiMesh = CreateMesh(51);

			//deciding whether to use hipoly mesh
			Mesh mesh;

			if (horDelta < 0)
				mesh = hiMesh;
			else if (verDelta > horDelta*0.5f)
				mesh = midMesh;
			else
				mesh = loMesh;

			//Draw.Mesh(mesh, meshMat, clip:false);
			Matrix4x4 prs = Matrix4x4.TRS(UI.current.scrollZoom.ToScreen(Vector3.zero), Quaternion.identity, UI.current.scrollZoom.zoom);

			using (ProfilerExt.Profile("Links Drawing"))
			{
				mat.SetPass(0);
				Graphics.DrawMeshNow(mesh, prs);
			}
		}}



		private static Mesh CreateMesh (int segments = 11)
		{
			Mesh mesh = new Mesh();
			//mesh.MarkDynamic(); //no need it's shader driven

			int numVerts = (segments+1) * 2;
			float uvStep = 1f/(segments-1); //-1 for central segment
			Vector3[] verts = new Vector3[numVerts];
			Vector2[] uvs = new Vector2[numVerts];

			for (int s=0; s<=segments; s++)
			{
				int v = s*2;	

				verts[v] = new Vector3(v, 0, 0);
				verts[v+1] = new Vector3(v, 1, 0);

				float u = s*uvStep;
				if (s==segments/2) u = 0.4999f;
				if (s==segments/2+1) u = 0.5001f;
				if (s> segments/2+1) u-= uvStep;

				uvs[v] = new Vector2(u, 0);
				uvs[v+1] = new Vector2(u, 1);
			}

			int[] tris = new int[segments*6];
			for (int s=0; s<segments; s++)
			{
				int t = s*6;
				int v = s*2;

				tris[t] = v;  tris[t+1] = v+1;  tris[t+2] = v+3;
				tris[t+3] = v+3;  tris[t+4] = v+2;  tris[t+5] = v;
			}

			//tris = new int[] { 0,1,3, 3,2,0 };

			mesh.vertices = verts;
			mesh.uv = uvs;
			mesh.triangles = tris;

			return mesh;
		}


			private static void PlaceDotsOnCircle (ref Vector2 start, ref Vector2 end, Vector2 startCircle, Vector2 endCircle, float radius)
			/// putting segment start/end on circles: 
			/// find closest point on segment to circle centers
			/// and then offseting segmment start/end to the direction of found points
			{
				Vector2 lineDirection = end - start;
				float lineLengthSq = lineDirection.sqrMagnitude;

				float startDot = Vector2.Dot(startCircle-start, lineDirection) / lineLengthSq;
				Vector2 startClosest = start + startDot*lineDirection; // closest point to startCircle on the start-en
				start = startCircle + (startClosest-startCircle).normalized * radius;

				float endDot = Vector2.Dot(endCircle-end, lineDirection) / lineLengthSq;
				Vector2 endClosest = end + endDot*lineDirection; // closest point to startCircle on the start-en
				end = endCircle + (endClosest-endCircle).normalized * radius;
			}

			private static (Vector2,Vector2) LineCircleIntersections(Vector2 start, Vector2 end, Vector2 circlePos, float circleRadius)
			//not used, but keeping just in case
			{
				circleRadius += 0.0001f; //adding epsilon to ensure we'll always have intersection

				// Compute line direction
				Vector2 d = end - start;
				Vector2 f = start - circlePos;

				float a = Vector2.Dot(d, d);
				float b = 2 * Vector2.Dot(f, d);
				float c = Vector2.Dot(f, f) - (circleRadius * circleRadius);

				// Compute the discriminant
				float discriminant = b * b - 4 * a * c;

				if (discriminant < 0) // No intersection
					return (Vector2.zero, Vector2.zero);

				float sqrtDiscriminant = Mathf.Sqrt(discriminant);
				float inv2a = 1 / (2 * a);

				// Compute the two possible values for t
				float t1 = (-b - sqrtDiscriminant) * inv2a;
				float t2 = (-b + sqrtDiscriminant) * inv2a;

				return (start + t1*d, start + t2*d);

				// Check if intersection points lie within the segment range
				//if (t1 >= 0 && t1 <= 1)
			}
	}
}