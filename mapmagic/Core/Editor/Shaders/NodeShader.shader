Shader "MapMagic/NodeShader"
/// Copy of ElementShader, but taks b&w image (red) and colors it according to 2 masks - green and blie
/// Draws UI texture the same way DrawElement does - 1x1 at the corners, and stretches in-between
{
    Properties
    {
		_MainTex ("Texture", 2D) = "white" {}
		_CellRect("Cell Rect", Vector) = (0,0,10,10)
		_ClipRect("Clip Rect", Vector) = (0,0,10,10) //window size, assigned automatically
		_BorderOffset("Border Offset", Vector) = (20,20,20,135)
        _Scale("Scale", Range(0, 1)) = 0.5 //0.5 is default scale applied to oversized textures for dpi 200%
		_Opacity("Opacity", Range(0, 1)) = 1
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
			float _Opacity;
			float4 _TopColor;
			float4 _BottomColor;

            v2f vert (appdata_t v)
            {
                v2f o;

                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
				o.screenPos = v.vertex.xy;

				_CellRect /= _Scale;

				float2 pixel = o.uv * _CellRect.zw;

				//stretched center coords
				float2 scaledFragmentSize = _CellRect.zw/_Scale - _BorderOffset.zy - _BorderOffset.wx;  //rectSize - left(top)Border - right(bot)Border
				float2 origFragmentSize = _MainTex_TexelSize.zw - _BorderOffset.zy - _BorderOffset.wx;  //textureSize - leftBorder - rightBorder
				float2 ratio = scaledFragmentSize / origFragmentSize;
				float2 stretchedPixel = (pixel/_Scale - _BorderOffset.zy) / ratio + _BorderOffset.zy;  // (pixel - borderLeft) / ratio + (returning borderLeft back)
				o.stretchedUv = stretchedPixel;

                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
				//early clipping
				float alpha = UnityGet2DClipping(i.screenPos.xy, _ClipRect);
				alpha *= step(0, i.screenPos.y);
				clip(alpha - 0.001); 

				//dpi scale affects only the size of _CellRect (and all depending parameters)
				_CellRect /= _Scale;

				float2 pixel = i.uv * _CellRect.zw;

				//stretched center coords
				//now in vertex
				//float2 scaledFragmentSize = _CellRect.zw - _BorderOffset.zx - _BorderOffset.wy;  //rectSize - leftBorder - rightBorder
				//float2 origFragmentSize = _MainTex_TexelSize.zw - _BorderOffset.zx - _BorderOffset.wy;  //textureSize - leftBorder - rightBorder
				//float2 ratio = scaledFragmentSize / origFragmentSize;
				//float2 stretchedPixel = (pixel - _BorderOffset.zx) / ratio + _BorderOffset.zx;  // (pixel - borderLeft) / ratio + (returning borderLeft back)
				float2 stretchedPixel = i.stretchedUv;

				//removing stretching on edges
				if (pixel.x < _BorderOffset.z)  //pixel.x < bordersLeft
					stretchedPixel.x = pixel.x;
				else if (pixel.x >  _CellRect.z - _BorderOffset.w)  //pixel.x > rectWidth - bordersRight
					stretchedPixel.x = pixel.x - (_CellRect.z - _MainTex_TexelSize.z);  //pixel.x -= delta aka (rectWidth - textureWidth)

				if (pixel.y < _BorderOffset.y) //pixel.y < bordersTop
					stretchedPixel.y = pixel.y;
				else if (pixel.y >  _CellRect.w - _BorderOffset.x) //pixel.y > rectHeight - bordersBottom
					stretchedPixel.y = pixel.y - (_CellRect.w - _MainTex_TexelSize.w); //pixel.y -= rectHeight - textureHeight

				float2 uv = stretchedPixel * _MainTex_TexelSize.xy; // pixel / texture size

				float4 color = tex2D(_MainTex, uv);
				
				float2 masks = color.gb;
				color.rgb = color.r;
				color.rgb = lerp(color.rgb, color.rgb * _TopColor, masks.x);
				//color.rgb *= _BottomColor * masks.y;

				color.a *= alpha;
				color.a *= _Opacity;

				return color;
            }
            ENDCG
        }
    }
}
