// Written into engine-types.assets by PlayerBuild, for Unity 6's shader layout and compiled program format, which the
// installer converts the game's shaders to. Has no parameters, so its parameter entry is the empty one.
Shader "Hidden/CastleStoryPlus/EngineTypes"
{
	SubShader
	{
		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			float4 vert(float4 v : POSITION) : SV_POSITION { return v; }
			fixed4 frag() : SV_Target { return fixed4(1, 0, 1, 1); }
			ENDCG
		}
	}
}
