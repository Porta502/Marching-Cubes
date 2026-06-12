using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PixelateFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Header("Pixelate")]
        public int pixelSize = 4;

        [Header("Outline")]
        public bool enableOutline = true;
        public Color outlineColor = Color.black;
        [Range(0.0001f, 0.05f)] public float depthThreshold = 0.001f;
        [Range(0f, 1f)] public float normalThreshold = 0.3f;

        [Header("Shader References")]
        public Shader pixelateShader;
        public Shader outlineShader;
    }

    public Settings settings = new Settings();
    PixelatePass pass;

    public override void Create()
    {
        if (settings.pixelateShader == null || settings.outlineShader == null) return;
        pass = new PixelatePass(settings);
        pass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (pass == null) return;
        if (renderingData.cameraData.cameraType == CameraType.SceneView) return;
        if (renderingData.cameraData.cameraType == CameraType.Preview) return;

        pass.Setup(renderer);
        renderer.EnqueuePass(pass);
    }
}

public class PixelatePass : ScriptableRenderPass
{
    PixelateFeature.Settings settings;
    Material pixelateMat;
    Material outlineMat;
    ScriptableRenderer renderer;
    RTHandle tempRT;

    static readonly int _PixelSize = Shader.PropertyToID("_PixelSize");
    static readonly int _OutlineColor = Shader.PropertyToID("_OutlineColor");
    static readonly int _DepthThreshold = Shader.PropertyToID("_DepthThreshold");
    static readonly int _NormalThreshold = Shader.PropertyToID("_NormalThreshold");

    public PixelatePass(PixelateFeature.Settings settings)
    {
        this.settings = settings;
        pixelateMat = new Material(settings.pixelateShader);
        outlineMat = new Material(settings.outlineShader);

        ConfigureInput(ScriptableRenderPassInput.Color |
                       ScriptableRenderPassInput.Depth |
                       ScriptableRenderPassInput.Normal);
    }

    public void Setup(ScriptableRenderer renderer) { this.renderer = renderer; }

    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        var desc = renderingData.cameraData.cameraTargetDescriptor;
        desc.depthBufferBits = 0;
        RenderingUtils.ReAllocateIfNeeded(ref tempRT, desc, FilterMode.Point, name: "_PixelateTempRT");
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (pixelateMat == null) return;

        RTHandle source = renderer.cameraColorTargetHandle;
        CommandBuffer cmd = CommandBufferPool.Get("PixelateEffect");

        pixelateMat.SetInt(_PixelSize, settings.pixelSize);
        Blitter.BlitCameraTexture(cmd, source, tempRT, pixelateMat, 0);
        Blitter.BlitCameraTexture(cmd, tempRT, source);

        if (settings.enableOutline && outlineMat != null)
        {
            outlineMat.SetColor(_OutlineColor, settings.outlineColor);
            outlineMat.SetFloat(_DepthThreshold, settings.depthThreshold);
            outlineMat.SetFloat(_NormalThreshold, settings.normalThreshold);
            Blitter.BlitCameraTexture(cmd, source, tempRT, outlineMat, 0);
            Blitter.BlitCameraTexture(cmd, tempRT, source);
        }

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    public override void OnCameraCleanup(CommandBuffer cmd) { renderer = null; }
}