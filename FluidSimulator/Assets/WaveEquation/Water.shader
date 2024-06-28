Shader "WaveEquation/Water"
{
    Properties
    {
       _BaseColor("本体色", Color) = (1,1,1,1)
       _AbsorbColor("衰减", Color) = (1,1,1,1)
       _Turbidity("浑浊度", Range(0.01, 0.99)) = 0.1 
       _BumpMap("Normal Map", 2D) = "bump" {}
       _BumpTilling("Bump Tilling", Float) = 1.0 
       _BumpScale("Bump Scale", Range(0, 100)) = 1.0
    }
    SubShader
    {
        HLSLINCLUDE

        #pragma target 2.0
        #pragma editor_sync_compilation
        #pragma multi_compile _ DISABLE_TEXTURE2D_X_ARRAY
        #pragma multi_compile _ BLIT_SINGLE_SLICE
        // Core.hlsl for XR dependencies
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        
    ENDHLSL


        Tags { "RenderType"="Transparent"  "Queue" = "Transparent"}
        LOD 100

        // UniversalForward
        Pass
        {
            Name "WaterLighting"
            Tags
            {
                "LightMode" = "UniversalForward"
            }
            
            Blend SrcAlpha OneMinusSrcAlpha
            BlendOp Add
            
            HLSLPROGRAM
            // -------------------------------------
            // Shader Stages
            #pragma vertex WaterPassVertex
            #pragma fragment WaterPassFragment
            #include "./WaterBase.hlsl"

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            
            ENDHLSL
        }

        // Displacement
        Pass
        {
            Name "WaterDisplacement"
            Tags
            {
                "LightMode" = "WaterDisplacement"
            }
            
           ZWrite Off ZTest Always Blend Off Cull Off

            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment WaterDisplacementFragment
                #include "./WaterBase.hlsl"
                
            ENDHLSL
        }

        // Copy
        Pass
        {
            Name "Copy"
            Tags
            {
                "LightMode" = "Copy"
            }
            
           ZWrite Off ZTest Always Blend Off Cull Off

            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment WaterFragBilinear
                #include "./WaterBase.hlsl"
                
            ENDHLSL
        }
    }
}
