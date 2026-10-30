// Development: a Shader Model 5 program reading a structured buffer, like the game's eye adaptation (see ShaderProbe).
Shader "CastleStoryPlus/ProbeBuffer"
{
	SubShader
	{
		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 5.0
			#pragma multi_compile _ B_BUF
			StructuredBuffer<uint> _Histogram;
			float4 _Base;
			float4 vert(float4 v : POSITION) : SV_POSITION
			{
				return v + _Base;
			}
			fixed4 frag() : SV_Target
			{
				#ifdef B_BUF
				return _Histogram[3] * _Base;
				#endif
				return _Base;
			}
			ENDCG
		}
	}
}
