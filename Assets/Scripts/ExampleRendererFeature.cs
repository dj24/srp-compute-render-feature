using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class ExampleRendererFeature : ScriptableRendererFeature
{
    private ExampleRendererPass _exampleRendererPass;
    public Color skyColor;
    [Range(0, 500)]
    public float fogDistance;
    class ExampleRendererPass : ScriptableRenderPass
    {
        private const string ProfilerTag = "Example Pass";
        private readonly ComputeShader _exampleComputeShader;
        private readonly Color _skyColor;
        private readonly float _fogDistance;
        private int KernelIndex => _exampleComputeShader.FindKernel("Main");
        public ExampleRendererPass(Color skyColor, float fogDistance)
        {
            renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
            _exampleComputeShader = (ComputeShader)Resources.Load("ExampleComputeShader");
            _skyColor = skyColor;
            _fogDistance = fogDistance;
        }
        
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            renderingData.cameraData.camera.depthTextureMode = DepthTextureMode.Depth;
        }
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData;
            if (camera.cameraType == CameraType.Preview)
            {
                // Do not apply this render pass to preview windows
                return;
            }
            var colorTarget = camera.renderer.cameraColorTargetHandle;
            var depthTarget = camera.renderer.cameraDepthTargetHandle;
            var cmd = CommandBufferPool.Get(ProfilerTag);
            
            // Get temporary copy of the scene texture
            var tempColorTarget = RenderTexture.GetTemporary(camera.cameraTargetDescriptor);
            tempColorTarget.enableRandomWrite = true;
            cmd.Blit(colorTarget.rt,tempColorTarget);
            
            // Setup compute params
            cmd.SetComputeTextureParam(_exampleComputeShader, KernelIndex, "Scene", tempColorTarget);
            cmd.SetComputeTextureParam(_exampleComputeShader, KernelIndex, "Depth", depthTarget.rt);
            cmd.SetComputeVectorParam(_exampleComputeShader, "SkyColor", _skyColor);
            cmd.SetComputeFloatParam(_exampleComputeShader, "FogDistance", _fogDistance);
            
            // Dispatch according to thread count in shader
            _exampleComputeShader.GetKernelThreadGroupSizes(KernelIndex,out uint groupSizeX, out uint groupSizeY, out _);
            int threadGroupsX = (int) Mathf.Ceil(tempColorTarget.width / (float)groupSizeX); 
            int threadGroupsY = (int) Mathf.Ceil(tempColorTarget.height / (float)groupSizeY);
            cmd.DispatchCompute(_exampleComputeShader, KernelIndex, threadGroupsX, threadGroupsY, 1);
            
            // Sync compute with frame
            AsyncGPUReadback.Request(tempColorTarget).WaitForCompletion();
            
            // Copy temporary texture into colour buffer
            cmd.Blit(tempColorTarget, colorTarget);
            context.ExecuteCommandBuffer(cmd);
            
            // Clean up
            cmd.Clear();
            RenderTexture.ReleaseTemporary(tempColorTarget);
            CommandBufferPool.Release(cmd);
        }
    }

    public override void Create()
    {
        _exampleRendererPass = new ExampleRendererPass(skyColor, fogDistance);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(_exampleRendererPass);
    }
}