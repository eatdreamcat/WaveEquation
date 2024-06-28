using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;


public enum Resolution
{
    _256 = 256,
    _512 = 512,
    _1024 = 1024,
    _2048 = 2048,
    _4096 = 4096
}

[Serializable]
public class InteractiveWaterSettings
{
    public LayerMask forceLayer;
    public LayerMask staticLayer;
    [Range(0.001f, 1)] public float viscidity = 0.01f;
    [Range(0.001f, 1.0f)]
    public float velocity = 0.1f;
    [Range(0.001f, 1.0f)]
    public float deltaTime = 0.1f;

    [Range(0.01f, 10f)] public float fixedDeltaTime = 0.16f;
    
    public Mesh fluidMesh = null;
    public float force = 1f;
    public int resolution = 4096;
   
    public float viewSize;
    public float zFarPlane = 1f;
    public FilterMode filterMode = FilterMode.Bilinear;
    
}


public class InteractiveWaterRenderFeature : ScriptableRendererFeature
{
    [SerializeField]
    private InteractiveWaterSettings m_Settings = new InteractiveWaterSettings();
    
    private InteractiveWaterRenderPass m_RenderPass;
    
    public override void Create()
    {
        if (m_RenderPass == null)
        {
            m_RenderPass = new InteractiveWaterRenderPass(m_Settings);
            m_RenderPass.ConfigureInput(ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
        }
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (WaterPlane.s_Water == null)
        {
            return;
        }
        
        if (m_Settings.fluidMesh == null)
        {
            return;
        }

        if (m_RenderPass.Setup() == false)
        {
            return;
        }
        
        renderer.EnqueuePass(m_RenderPass);
    }
}

public class InteractiveWaterRenderPass : ScriptableRenderPass
{
    private static readonly int _WaveCoefficient = Shader.PropertyToID("_WaveCoefficient");
    private static readonly int _CurrentDisplacement = Shader.PropertyToID("_CurrentDisplacement");
    private static readonly int _PreviousDisplacement = Shader.PropertyToID("_PreviousDisplacement");
    private static readonly int _NextDisplacement = Shader.PropertyToID("_NextDisplacement");
    
    private static readonly int _ForceParams = Shader.PropertyToID("_ForceParams");
    private static readonly int _WaterUVScaleAndBias = Shader.PropertyToID("_WaterUVScaleAndBias");
    
    private static readonly string k_ComputeShaderName = "";
    private static readonly string k_ForceApply = "ForceApply";
    private static readonly string k_ClearDisplacement = "ClearDisplacement";

    private static readonly ShaderTagId k_ForceApplyShaderTag = new (k_ForceApply);
    private static readonly ShaderTagId k_ClearDisplacementhaderTag = new (k_ClearDisplacement);

    private static Vector3 s_InvalidPosition = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    private Vector3 m_PlayerPosition = s_InvalidPosition;
    private InteractiveWaterSettings m_Settings;

    private ComputeShader m_ComputeShader;

    private RTHandle m_DisplacementTextureA;
    private RTHandle m_DisplacementTextureB;
    private RTHandle m_DisplacementNext;
    
    private Material m_WaveEquationMat;
    private RenderTextureDescriptor m_Desc;
    private bool m_AIsCurrent = true;

    private void Swap()
    {
        m_AIsCurrent = !m_AIsCurrent;
    }
    
    private RTHandle m_CurrentDisplacement
    {
        get
        {
            return m_AIsCurrent ? m_DisplacementTextureA : m_DisplacementTextureB;
        }
    }

    private RTHandle m_PreviousDisplacement
    {
        get
        {
            return m_AIsCurrent ? m_DisplacementTextureB : m_DisplacementTextureA;
        }
    }

    private Matrix4x4 m_ViewMatrix;
    private Matrix4x4 m_ProjectMatrix;
    

    public InteractiveWaterRenderPass(InteractiveWaterSettings settings)
    {
        m_Settings = settings;
        renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
        m_WaveEquationMat = CoreUtils.CreateEngineMaterial(Shader.Find("WaveEquation/Water"));
    }

    public bool Setup()
    {
        m_Settings.resolution = Math.Clamp(m_Settings.resolution, 1, SystemInfo.maxTextureSize);
        m_Desc = new RenderTextureDescriptor((int)m_Settings.resolution, (int)m_Settings.resolution);
        // reference https://docs.nvidia.com/holoscan/sdk-user-guide/api/cpp/enum_image__format_8hpp_1ac1df8331638c94daefdc3b27385e0433.html
        m_Desc.graphicsFormat = GraphicsFormat.R32_SFloat;
        RenderingUtils.ReAllocateIfNeeded(ref m_DisplacementTextureA, m_Desc, m_Settings.filterMode, TextureWrapMode.Repeat, name:"_DisplacementMapA");
        RenderingUtils.ReAllocateIfNeeded(ref m_DisplacementTextureB, m_Desc, m_Settings.filterMode, TextureWrapMode.Repeat, name:"_DisplacementMapB");
        RenderingUtils.ReAllocateIfNeeded(ref m_DisplacementNext, m_Desc, m_Settings.filterMode, TextureWrapMode.Repeat, name:"_DisplacementNext");
        return true;
    }

    public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
    {
        ConfigureTarget(m_CurrentDisplacement);
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {

        var force = 0f;

        if (WaterPlane.s_Player != null)
        {
            if (m_PlayerPosition == s_InvalidPosition)
            {
                m_PlayerPosition = WaterPlane.s_Player.position;
            }

            var distance = Vector3.Distance(WaterPlane.s_Player.position, m_PlayerPosition);
            force = m_Settings.force * distance;
            m_PlayerPosition = WaterPlane.s_Player.position;
        }
        
        var cmd = CommandBufferPool.Get();
        
        var camera = renderingData.cameraData.camera;
        var originViewMatrix = camera.worldToCameraMatrix;
        var originProjMatrix = camera.projectionMatrix;
        var halfViewSize = Mathf.Max(0.001f, m_Settings.viewSize / 2);
        
        using (new ProfilingScope(cmd, new ProfilingSampler("ForceApply")))
        {
           
            m_ViewMatrix.SetRow(0, new Vector4(1,0,0,0));
            m_ViewMatrix.SetRow(1, new Vector4(0,0,1,0));
            m_ViewMatrix.SetRow(2, new Vector4(0,1,0,0));
            m_ViewMatrix.SetRow(3, new Vector4(0,0,0,1));
            var waterPosition = WaterPlane.s_Water.position;
         
            m_ViewMatrix.SetColumn(3, new Vector4(-waterPosition.x, -waterPosition.z, -waterPosition.y,1.0f));
            m_ProjectMatrix = Matrix4x4.Ortho(-halfViewSize, halfViewSize, -halfViewSize, halfViewSize, 0.01f, m_Settings.zFarPlane);
            m_ProjectMatrix = GL.GetGPUProjectionMatrix(m_ProjectMatrix, true);
            cmd.SetViewProjectionMatrices(m_ViewMatrix, m_ProjectMatrix);
            context.ExecuteCommandBuffer(cmd);
            
            cmd.Clear();

            {
                var drawSettings = RenderingUtils.CreateDrawingSettings(k_ForceApplyShaderTag, ref renderingData,
                    SortingCriteria.CommonOpaque);
                var filterSettings = new FilteringSettings();
                filterSettings.renderQueueRange = RenderQueueRange.all;
                filterSettings.layerMask = m_Settings.forceLayer;
                filterSettings.renderingLayerMask = 0xFFFFFFFF;
                filterSettings.sortingLayerRange = SortingLayerRange.all;

                cmd.SetGlobalVector(_ForceParams, new Vector4(force, m_Settings.viscidity, 0, 0));
                context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref filterSettings);
                
            }
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }

        using (new ProfilingScope(cmd, new ProfilingSampler("Displacement")))
        {
            
            cmd.SetGlobalVector(_WaterUVScaleAndBias, new Vector4(-1, 1, 1, 0));
            
            // render displacement texture
            var density = 1.0f / (int)m_Settings.resolution;
            var maxVelocity = density / (2.0f * m_Settings.fixedDeltaTime) *
                              Mathf.Sqrt(m_Settings.viscidity * m_Settings.fixedDeltaTime + 2f);
            var velocity = m_Settings.velocity * maxVelocity;
            var velocity2 = velocity * velocity;
            var d2 = density * density;
            var maxDeltaTime = (m_Settings.viscidity
                                + Mathf.Sqrt(m_Settings.viscidity * m_Settings.viscidity + 32f * velocity2 / d2))/
                               (8f * velocity2 / d2);
            
            var dt = m_Settings.deltaTime * maxDeltaTime;
            var dt2 = dt * dt;
            
            var utadd2 = m_Settings.viscidity * dt + 2f;
            var _2c2t2_d2 = 2f * velocity2 * dt2 / d2;
            var coefficient0 = (4f - 4f * _2c2t2_d2) / utadd2;
            var coefficient1 = (utadd2 - 4f) / utadd2;
            var coefficient2 = _2c2t2_d2 / utadd2;
            cmd.SetGlobalVector(_WaveCoefficient, new Vector4(coefficient0, coefficient1, coefficient2, density));
            
            m_WaveEquationMat.SetTexture(_CurrentDisplacement, m_CurrentDisplacement);
            m_WaveEquationMat.SetTexture(_PreviousDisplacement, m_PreviousDisplacement);
            Blitter.BlitTexture(cmd,m_PreviousDisplacement, m_DisplacementNext, m_WaveEquationMat, 1);
            m_WaveEquationMat.SetTexture(_NextDisplacement, m_DisplacementNext);
            Blitter.BlitTexture(cmd,m_DisplacementNext, m_PreviousDisplacement, m_WaveEquationMat, 2);
            
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            
            Swap();
            
            cmd.SetGlobalTexture(_CurrentDisplacement, m_CurrentDisplacement);
        }
        
        using (new ProfilingScope(cmd, new ProfilingSampler("StaticDisplacement")))
        {
            var drawSettings = RenderingUtils.CreateDrawingSettings(k_ClearDisplacementhaderTag, ref renderingData,
                SortingCriteria.CommonOpaque);
            var filterSettings = new FilteringSettings();
            filterSettings.renderQueueRange = RenderQueueRange.all;
            filterSettings.layerMask = m_Settings.staticLayer;
            filterSettings.renderingLayerMask = 0xFFFFFFFF;
            filterSettings.sortingLayerRange = SortingLayerRange.all;
            
            m_ProjectMatrix = Matrix4x4.Ortho(-halfViewSize, halfViewSize, -halfViewSize, halfViewSize, 0.01f, 10000f);
            m_ProjectMatrix = GL.GetGPUProjectionMatrix(m_ProjectMatrix, true);
            cmd.SetViewProjectionMatrices(m_ViewMatrix, m_ProjectMatrix);
            cmd.SetRenderTarget(m_CurrentDisplacement);
            context.ExecuteCommandBuffer(cmd);

            cmd.Clear();
            
            context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref filterSettings);
        }
        
        cmd.SetViewProjectionMatrices(originViewMatrix, originProjMatrix);
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();
        CommandBufferPool.Release(cmd);
    }

    public override void OnCameraCleanup(CommandBuffer cmd)
    {
        if (cmd == null)
            throw new ArgumentNullException("cmd");

        
    }
}