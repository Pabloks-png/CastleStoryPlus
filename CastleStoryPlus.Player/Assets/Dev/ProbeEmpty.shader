// Development: no parameters at all (see ShaderProbe).
Shader "CastleStoryPlus/ProbeEmpty"
{
	SubShader
	{
		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			float4 vert(float4 v : POSITION) : SV_POSITION { return v; }
			fixed4 frag() : SV_Target { return fixed4(1, 0, 0, 1); }
			ENDCG
		}
	}
}
