Shader "WaveEquation/Preview"
{
    Properties
    {
        
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };
            

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = TransformObjectToHClip(v.vertex);
                o.uv = v.uv;
                return o;
            }

            TEXTURE2D(_CurrentDisplacement); SAMPLER(sampler_CurrentDisplacement);
            half4 frag (v2f i) : SV_Target
            {
                // sample the texture
                half4 col = SAMPLE_TEXTURE2D(_CurrentDisplacement, sampler_CurrentDisplacement, i.uv);
             
                return col;
            }
            ENDHLSL
        }
    }
}
