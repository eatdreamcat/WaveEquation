#ifndef _WAVE_EUQATION_WATER_INCLUDED_
#define _WAVE_EUQATION_WATER_INCLUDED_
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#if defined(LOD_FADE_CROSSFADE)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

struct WaterAttributes
{
    float4 positionOS   : POSITION;
    float3 normalOS     : NORMAL;
    float4 tangentOS    : TANGENT;
    float2 texcoord     : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct WaterVaryings
{
    float2 uv                       : TEXCOORD0;
    float4 positionWS               : TEXCOORD1;
    float4 normalWS                 : TEXCOORD2;
    float4 tangentWS                : TEXCOORD3;
    float4 positionCS               : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

CBUFFER_START(UnityPerMaterial)
half4 _BaseColor;
half4 _AbsorbColor;
float _BumpScale;
float _BumpTilling;
float _Turbidity;
CBUFFER_END

/// zyx: coefficient, w: invResolution
float4 _WaveCoefficient;

TEXTURE2D(_CurrentDisplacement); SAMPLER(sampler_CurrentDisplacement);

float4 _CurrentDisplacement_TexelSize;


float4 _WaterUVScaleAndBias;
WaterVaryings WaterPassVertex(WaterAttributes input)
{
    WaterVaryings output = (WaterVaryings)0;
    VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);

    // normalWS and tangentWS already normalize.
    // this is required to avoid skewing the direction during interpolation
    // also required for per-vertex lighting and SH evaluation
    VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);
    
    output.positionWS.xyz = vertexInput.positionWS;

    output.normalWS.xyz = normalInput.normalWS;
    real sign = input.tangentOS.w * GetOddNegativeScale();
    output.tangentWS = half4(normalInput.tangentWS.xyz, sign);
    
    output.uv = input.texcoord * _WaterUVScaleAndBias.xy + _WaterUVScaleAndBias.zw;
    output.positionCS = TransformObjectToHClip(input.positionOS);
    
    return output;
}




half4 WaterPassFragment(WaterVaryings input):SV_Target
{
    half4 finalColor = 0;
    half4 n1 = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, (input.uv + sin(_Time.y * 0.01)) * _BumpTilling);
    half4 n2 = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, (input.uv + cos(_Time.y * 0.01) + PI) * _BumpTilling);
    half3 normalTS = UnpackNormalScale(n1, _BumpScale) + UnpackNormalScale(n2, _BumpScale);
    float sgn = input.tangentWS.w;      // should be either +1 or -1
    float3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
    half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangent.xyz, input.normalWS.xyz);
    
    half3 originNormalWS = TransformTangentToWorld(normalTS, tangentToWorld, true);
    half3 normalWS = 0;
    
    normalWS.x = SAMPLE_TEXTURE2D(_CurrentDisplacement, sampler_CurrentDisplacement, input.uv + float2(_CurrentDisplacement_TexelSize.x, 0))
       - SAMPLE_TEXTURE2D(_CurrentDisplacement, sampler_CurrentDisplacement, input.uv + float2(-_CurrentDisplacement_TexelSize.x, 0));
    normalWS.y =  SAMPLE_TEXTURE2D(_CurrentDisplacement, sampler_CurrentDisplacement, input.uv + float2(0, _CurrentDisplacement_TexelSize.y))
       - SAMPLE_TEXTURE2D(_CurrentDisplacement, sampler_CurrentDisplacement, input.uv + float2(0, -_CurrentDisplacement_TexelSize.y));
    normalWS.z = 2 * _WaveCoefficient.w;

    // 需不需要做呢？
    // normalWS = TransformTangentToWorldDir(normalWS, tangentToWorld);
    normalWS = normalize(normalWS * _BumpScale + originNormalWS);
    
    // return half4(normalWS, 1.0);
    
    Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
    half NdotL = saturate(dot(normalWS, mainLight.direction));
    
    half3 directDiffuse = NdotL * _BaseColor.rgb * INV_PI * mainLight.color;
    
    half3 viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    
    half3 sss = pow(saturate(dot(-normalize(viewDirectionWS + mainLight.direction), normalWS)), 3) * _AbsorbColor.rgb * INV_PI * mainLight.color * pow(1-_Turbidity, 3);
   
    half3 SH = SampleSH(normalWS) * _BaseColor.rgb;
    half3 GI = SH;

    
    half3 finalScattering = directDiffuse * (1 - _Turbidity) + _Turbidity * sss;
     
    BRDFData brdfData = (BRDFData)0;
    half alpha = _Turbidity;
    
    InitializeBRDFDataDirect(
        _BaseColor.rgb,
        finalScattering,
        0.9,
        0.89,
        0.1,
        0.95,
        alpha,
        brdfData);
    half3 directSpecular = clamp(DirectBRDFSpecular(brdfData, normalWS, mainLight.direction, viewDirectionWS), 0 ,2);

    finalColor.a = alpha;
 
    finalColor.rgb = GI + finalScattering + directSpecular;
    // finalColor.rgb *= (mainLight.shadowAttenuation);
    
    return finalColor;
}


TEXTURE2D(_PreviousDisplacement); SAMPLER(sampler_PreviousDisplacement);


/// x: force , y: viscidity
float4 _ForceParams;

float4 WaterDisplacementFragment(Varyings input) : SV_Target
{
    float current = SAMPLE_TEXTURE2D(_CurrentDisplacement, sampler_CurrentDisplacement, input.texcoord);
    float previous = SAMPLE_TEXTURE2D(_PreviousDisplacement, sampler_PreviousDisplacement, input.texcoord);
    float currentAdjacent =
         SAMPLE_TEXTURE2D(_CurrentDisplacement, sampler_CurrentDisplacement, input.texcoord + float2(_CurrentDisplacement_TexelSize.x, 0))
       + SAMPLE_TEXTURE2D(_CurrentDisplacement, sampler_CurrentDisplacement, input.texcoord + float2(-_CurrentDisplacement_TexelSize.x, 0))
       + SAMPLE_TEXTURE2D(_CurrentDisplacement, sampler_CurrentDisplacement, input.texcoord + float2(0, _CurrentDisplacement_TexelSize.y))
       + SAMPLE_TEXTURE2D(_CurrentDisplacement, sampler_CurrentDisplacement, input.texcoord + float2(0, -_CurrentDisplacement_TexelSize.y));
    
    return float4(clamp(0.01f, 0.96f, 1.0f - _ForceParams.y) * clamp(_WaveCoefficient.x * current + _WaveCoefficient.y * previous + _WaveCoefficient.z * currentAdjacent, -1000, 1000), 0, 0, 0);
}

TEXTURE2D(_NextDisplacement); SAMPLER(sampler_NextDisplacement);

Varyings WaterVert(Attributes input)
{
    Varyings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    #if SHADER_API_GLES
    float4 pos = input.positionOS;
    float2 uv  = input.uv;
    #else
    float4 pos = GetFullScreenTriangleVertexPosition(input.vertexID);
    float2 uv  = GetFullScreenTriangleTexCoord(input.vertexID);
    #endif

    output.positionCS = pos;
    output.texcoord   = uv * _WaterUVScaleAndBias.xy + _WaterUVScaleAndBias.zw;
    return output;
}

float4 WaterFragBilinear(Varyings input) : SV_Target
{
    return SAMPLE_TEXTURE2D_X_LOD(_NextDisplacement, sampler_NextDisplacement,
        input.texcoord.xy, 0);
}


#endif