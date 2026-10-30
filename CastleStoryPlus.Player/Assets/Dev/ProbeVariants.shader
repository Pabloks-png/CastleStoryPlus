// Development: variants that use different parameters (see ShaderProbe).
Shader "CastleStoryPlus/ProbeVariants"
{
	Properties
	{
		_A ("A", Color) = (1, 0, 0, 1)
		_B ("B", Float) = 2
		_TexA ("TexA", 2D) = "white" {}
		_TexB ("TexB", Cube) = "" {}
	}
	SubShader
	{
		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile _ USE_B
			#include "UnityCG.cginc"
			fixed4 _A;
			float _B;
			float4 _Arr[3];
			float4x4 _M;
			sampler2D _TexA;
			samplerCUBE _TexB;
			struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; };
			v2f vert(appdata_base v)
			{
				v2f o;
				o.pos = UnityObjectToClipPos(mul(_M, v.vertex));
				o.n = v.normal + _Arr[1].xyz;
				return o;
			}
			fixed4 frag(v2f i) : SV_Target
			{
				fixed4 c = _A * tex2D(_TexA, i.n.xy);
				#ifdef USE_B
				c *= _B * texCUBE(_TexB, i.n);
				#endif
				return c;
			}
			ENDCG
		}
	}
}
