// Development: each variant adds one kind of parameter (see ShaderProbe).
Shader "CastleStoryPlus/ProbeKinds"
{
	Properties
	{
		_TexK ("TexK", 2D) = "white" {}
	}
	SubShader
	{
		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile _ K_FLOAT K_FLOAT2 K_ARRAY K_MATRIX K_TEX K_TWO K_TEXONLY
			float4 _Base;
			float _F;
			float2 _F2;
			float4 _Arr[2];
			float4x4 _M2;
			sampler2D _TexK;
			float4 vert(float4 v : POSITION) : SV_POSITION
			{
				float4 p = v + _Base;
				#ifdef K_MATRIX
				p = mul(_M2, p);
				#endif
				return p;
			}
			fixed4 frag() : SV_Target
			{
				#ifdef K_TEXONLY
				return tex2D(_TexK, float2(0.5, 0.5));
				#endif
				float4 c = _Base;
				#ifdef K_FLOAT
				c *= _F;
				#endif
				#ifdef K_FLOAT2
				c.xy *= _F2;
				#endif
				#ifdef K_ARRAY
				c += _Arr[1];
				#endif
				#ifdef K_TEX
				c *= tex2D(_TexK, c.xy);
				#endif
				#ifdef K_TWO
				c *= _F;
				c.xy *= _F2;
				#endif
				return c;
			}
			ENDCG
		}
	}
}
