// Development: every vertex channel, and parameter kinds the game's Direct3D 11 programs use (see ShaderProbe).
Shader "CastleStoryPlus/ProbeChannels"
{
	Properties
	{
		_TexA ("TexA", 2D) = "white" {}
		_TexB ("TexB", 2D) = "white" {}
		_Cube ("Cube", Cube) = "" {}
		_Vol ("Vol", 3D) = "" {}
	}
	SubShader
	{
		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile _ P_INT P_BOOL P_CUBE P_LOAD P_TWO P_3D
			int _I;
			bool _B;
			float4 _Base;
			sampler2D _TexA;
			sampler2D _TexB;
			samplerCUBE _Cube;
			sampler3D _Vol;
			Texture2D _Tl;
			struct appdata
			{
				float4 pos : POSITION;
				float3 normal : NORMAL;
				float4 tangent : TANGENT;
				float4 color : COLOR;
				float4 uv0 : TEXCOORD0;
				float4 uv1 : TEXCOORD1;
				float4 uv2 : TEXCOORD2;
				float4 uv3 : TEXCOORD3;
			};
			float4 vert(appdata v) : SV_POSITION
			{
				return v.pos + float4(v.normal, 0) + v.tangent + v.color + v.uv0 + v.uv1 + v.uv2 + v.uv3;
			}
			fixed4 frag() : SV_Target
			{
				float4 c = _Base;
				#ifdef P_INT
				c *= _I;
				#endif
				#ifdef P_BOOL
				if (_B) { c *= 2; }
				#endif
				#ifdef P_CUBE
				c *= texCUBE(_Cube, c.xyz);
				#endif
				#ifdef P_LOAD
				c *= _Tl.Load(int3(1, 1, 0));
				#endif
				#ifdef P_TWO
				c *= tex2D(_TexA, c.xy) * tex2D(_TexB, c.zw);
				#endif
				#ifdef P_3D
				c *= tex3D(_Vol, c.xyz);
				#endif
				return c;
			}
			ENDCG
		}
	}
}
