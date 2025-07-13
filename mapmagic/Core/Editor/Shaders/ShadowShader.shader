Shader "MapMagic/ShadowShader"
/// Copy of ElementShader, but taks b&w image (red) and colors it according to 2 masks - green and blie
/// Draws UI texture the same way DrawElement does - 1x1 at the corners, and stretches in-between
{
    Properties
    {
		_MainTex ("Texture", 2D) = "white" {}
		_ClipRect("Clip Rect", Vector) = (0,0,10,10) //window size, assigned automatically
		_CellRect("Cell Rect", Vector) = (0,0,10,10)
		_Scale ("Scale",  Range(0, 1)) = 1

		_NodeRect("Node Rect", Vector) = (0,0,10,10) //object that casts shadow
		_Opacity("Opacity", Range(0, 1)) = 1
		_Distance("Distance", Range(0, 100)) = 20
		_Fill("Fill", Range(0, 1)) = 0 //part of the distance it will be 100% opacity
		_Gamma("Gamma", Range(0, 10)) = 1
		_Color ("Color", Color) = (0, 0, 0, 1)
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
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize; // Unity-provided: (1/texWidth, 1/texHeight, texWidth, texHeight)
			float4 _CellRect;
			float4 _ClipRect;

			float _Scale;
			float4 _NodeRect;
			float _Opacity;
			float _Distance;
			float _Fill;
			float _Gamma;
			float4 _Color;


            v2f vert (appdata_t v)
            {
                v2f o;

                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
				o.screenPos = v.vertex.xy;

				float2 pixel = o.uv * _CellRect.zw;

                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
				//early clipping
				float alpha = UnityGet2DClipping(i.screenPos.xy, _ClipRect);
				alpha *= step(0, i.screenPos.y);
				clip(alpha - 0.001); 


				float2 pixel = i.uv * _CellRect.zw + _CellRect.xy;
				//pixel *= _Scale;

				//corners
				float cornerDist = 100000; //_Distance+1;

				cornerDist = min(cornerDist, distance(pixel,_NodeRect.xy));
				cornerDist = min(cornerDist, distance(pixel,_NodeRect.xy + _NodeRect.zw));
				cornerDist = min(cornerDist, distance(pixel,_NodeRect.xy + float2(_NodeRect.z,0)));
				cornerDist = min(cornerDist, distance(pixel,_NodeRect.xy + float2(0,_NodeRect.w)));

				//rect
				float horDist = max(_NodeRect.x-pixel.x, pixel.x - (_NodeRect.x+_NodeRect.z));
				float vertDist = max(_NodeRect.y-pixel.y, pixel.y - (_NodeRect.y+_NodeRect.w));
				float rectDist = max(horDist,vertDist);

				//corner/rect preference
				float cornerFactor = step(0, min(horDist, vertDist));
				float dist = cornerDist*cornerFactor + rectDist*(1-cornerFactor); //in pixels

				float val = 1 - (dist / _Distance); //in 0-1
				val = saturate(val/ max(0.0001,1-_Fill));
				val = pow(val,_Gamma);
				val *= _Opacity;

				//testing
				//val = step(0.01, val);

				_Color.a = val;
				return _Color;
            }
            ENDCG
        }
    }
}
