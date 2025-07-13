Shader "Hidden/MapMagic/NodeElementQuad"
/// Copy of DPUI/Element shader with some enchancements - it supports header
/// taks b&w image (red) and colors it according to 2 masks - green and blie
/// That Draws UI texture the same way DrawElement does - 1x1 at the corners, and stretches in-between
{
    Properties
    {
		_MainTex ("Texture", 2D) = "white" {}
		_CellRect("Cell Rect", Vector) = (0,0,10,10)
		_ClipRect("Clip Rect", Vector) = (0,0,10,10) //window size, assigned automatically
		_BorderOffset("Border Offset", Vector) = (20,20,20,135)  //In that order: x-top, y-bottom, z-left, w-right
        _Scale("Scale", Range(0, 1)) = 0.5
		_TexSections("Tex Sections", Vector) = (10,10,0,0)  //Each NodeElement is a 3*3 table. This one defines offsets of top,bottom,left,right cells. Like header and footer. On texture.
		_NodeSections("Node Sections", Vector) = (10,10,0,0)  //And this defines the size of top/bottom/left/right cells on screen
        _TopColor ("Top Color", Color) = (1, 1, 1, 1)
        _BottomColor ("Bottom Color", Color) = (1, 1, 1, 1)
	}

    SubShader
    {
        Tags
		{
			"Queue" = "Transparent"
			"IgnoreProjector" = "True"
			"RenderType" = "Transparent"
			"PreviewType" = "Plane"
			"CanUseSpriteAtlas" = "True"
		}

		Stencil
		{
			Ref[_Stencil]
			Comp[_StencilComp]
			Pass[_StencilOp]
			ReadMask[_StencilReadMask]
			WriteMask[_StencilWriteMask]
		}

		Cull Off
		Lighting Off
		ZWrite Off
		ZTest[unity_GUIZTestMode]
		Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

			#include "UnityCG.cginc"
			#include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
				float2 screenPos : TEXCOORD1; //for window clipping
				float2 stretchedUv : TEXCOORD2;
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize; // Unity-provided: (1/texWidth, 1/texHeight, texWidth, texHeight)
			float4 _CellRect;
			float4 _BorderOffset;
			float _Scale;
			float4 _ClipRect;
			float4 _TexSections;
			float4 _NodeSections;
			float4 _TopColor;
			float4 _BottomColor; 


            v2f vert (appdata_t v)
            {
                v2f o;
				float2 numSegments = float2(3,4);
				_BorderOffset = float4(10,10,10,10);
				float2 texSize = _MainTex_TexelSize.zw;

				float2 modVertex = v.vertex.xy;

				o.uv = 0;

				//first row/line of verts - don't move

				//second - offset in pixels
				if (v.vertex.x > 0.99)
				{
					modVertex.x = _BorderOffset.z * _Scale; //left
					o.uv.x = _BorderOffset.z / texSize.x;
				}
				if (v.vertex.y > 0.99)
				{
					modVertex.y = _BorderOffset.x * _Scale; //top
					o.uv.y = _BorderOffset.x / texSize.y;
				}

				//third - sections offset
				if (v.vertex.x > 1.99)
				{
					modVertex.x = _NodeSections.z * _Scale; //left
					o.uv.x = _TexSections.z / texSize.x;
				}
				if (v.vertex.y > 1.99)
				{
					modVertex.y = _NodeSections.x * _Scale; //top
					o.uv.y = _TexSections.x / texSize.y;
				}

				//pre-last
				if (v.vertex.x > numSegments.x-1-0.001)
				{
					modVertex.x = _CellRect.z - _BorderOffset.w*_Scale; //right
					o.uv.x = 1 - _BorderOffset.w / texSize.x;
				}
				if (v.vertex.y > numSegments.y-1-0.001)
				{
					modVertex.y = _CellRect.w - _BorderOffset.y*_Scale; //bottom
					o.uv.y = 1 - _BorderOffset.y / texSize.y;
				}

				//last
				if (v.vertex.x > numSegments.x-0.001)
				{
					modVertex.x = _CellRect.z;
					o.uv.x = 1;
				}
				if (v.vertex.y > numSegments.y-0.001)
				{
					modVertex.y = _CellRect.w;
					o.uv.y = 1;
				}

				o.uv.y = 1 - o.uv.y;

                o.pos = UnityObjectToClipPos( float4(modVertex,0,0) );
				o.screenPos = ComputeScreenPos(o.pos); 

                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
				// Early clipping based on screen position. 
				float2 inside = step(_ClipRect.xy, i.pos.xy) * step(i.pos.xy, _ClipRect.zw);
				float alpha = inside.x * inside.y;
				clip(alpha - 0.001);

				float4 color = tex2D(_MainTex, i.uv);
				
				float mask = color.b;
				color.rgb = (color.r + color.g) / 2;
				color.rgb = lerp(color.rgb, color.rgb*_TopColor, mask);
				//color.rgb *= _BottomColor * masks.y;

				color.a *= alpha;
				return color;
            }
            ENDCG
        }
    }
}
