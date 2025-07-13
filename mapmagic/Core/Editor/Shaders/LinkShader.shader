Shader "MapMagic/LinkShader"
/// Bends tri-strip mesh to draw connection between nodes
{
    Properties
    {
		_MainTex ("Texture", 2D) = "white" {}
		_ClipRect("Clip Rect", Vector) = (0,0,10,10) //window size with toolbar offset. Min-max, not pos-size

		_Color ("Color", Color) = (1, 1, 1, 1)
		_StartArc("Start Arc", Vector) = (0,0,10,10)  //x,y,and z is radius, w is percent filled
		_EndArc("End Arc", Vector) = (0,0,10,10)  //x,y,and z is radius, w is percent filled
		_Width("Width", Range(0, 20)) = 4
		_Mirror("Mirror", Range(-1, 1)) = 1//changes direction for mirrored
		_AAWidth("AA Width", Range(0, 4)) = 1 //width of the antialisaed area. Increase for zooming out and multiply with DPI scale
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
			float4 _ClipRect;
			
			float4 _Color;
			float4 _StartArc;
			float4 _EndArc;
			float _Width;
			float _Mirror;
			float _AAWidth;


            v2f vert (appdata_t v)
            {
                v2f o;

				float4 arc = v.uv.x > 0.5 ? _EndArc : _StartArc;

				float radius = abs(arc.z);
				float3 center = float3(arc.xy,0);
				float maxPercent = arc.w;
				//if (v.uv.x > 0.5 && maxPercent < 0)
				//	maxPercent = maxPercent+1;


				float uvPercent = v.uv.x;
				if (v.uv.x > 0.5)
					uvPercent = uvPercent-1;
				uvPercent *= 2;
				if (v.uv.x > 0.5)
					maxPercent = maxPercent-0.5;
				if (v.uv.x > 0.5)
					_Width =-_Width;
				//if (_StartArc.z < 0)
				//	percent = percent+0.5;
				//percent = -percent;
				//if (maxPercent < 0)
				//	maxPercent = maxPercent+1;

				if (_StartArc.z < 0)
				{
					//maxPercent = 0.5+maxPercent;
				}

                float radian = uvPercent * maxPercent * 2 * UNITY_PI;
               
				float3 direction = float3(-sin(radian), -cos(radian), 0);

				if (v.uv.x > 0.5)
					direction.y = -direction.y;

				direction *= _Mirror;

					
				float dist = radius + _Width*v.uv.y - _Width/2;
				float3 pos = center + direction * dist;

                o.pos = UnityObjectToClipPos(pos);
                o.uv = v.uv;
				o.screenPos = ComputeScreenPos(float4(pos, 0.0));

                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
				// Early clipping based on screen position. 
				float2 inside = step(_ClipRect.xy, i.pos.xy) * step(i.pos.xy, _ClipRect.zw);
				float alpha = inside.x * inside.y;
				clip(alpha - 0.001);

				alpha = min(i.uv.y, 1-i.uv.y) * _Width * _AAWidth;
				//return float4(step(alpha,1),0,0,1);

				return float4(_Color.rgb, _Color.a*alpha);
            }
            ENDCG
        }
    }
}
