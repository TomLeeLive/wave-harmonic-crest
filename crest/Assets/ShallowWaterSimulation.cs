using Crest;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public partial class ShallowWaterSimulation : MonoBehaviour
{
    [SerializeField, UnityEngine.Range(16, 1024)] int _resolution = 512;
    [SerializeField, UnityEngine.Range(8, 128)] float _domainWidth = 32f;

    RenderTexture _rtH0, _rtH1;
    RenderTexture _rtVx0, _rtVx1;
    RenderTexture _rtVy0, _rtVy1;

    PropertyWrapperCompute _csUpdateHProps;
    CommandBuffer _buf;

    ComputeShader _csUpdateH;
    int _krnlInitH;
    int _krnlUpdateH;

    void InitData()
    {
        if (_rtH0 == null) _rtH0 = CreateSWSRT();
        if (_rtH1 == null) _rtH1 = CreateSWSRT();
        if (_rtVx0 == null) _rtVx0 = CreateSWSRT();
        if (_rtVx1 == null) _rtVx1 = CreateSWSRT();
        if (_rtVy0 == null) _rtVy0 = CreateSWSRT();
        if (_rtVy1 == null) _rtVy1 = CreateSWSRT();

        if (_buf == null)
        {
            _buf = new CommandBuffer();
            _buf.name = "UpdateShallowWaterSim";
        }

        _matInjectSWSAnimWaves.SetFloat("_DomainWidth", _domainWidth);
    }

    void Swap<T>(ref T a, ref T b)
    {
        T temp = a;
        a = b;
        b = temp;
    }

    void Update()
    {
        InitData();

        _buf.Clear();

        // Each stage block should leave latest state in '1' buffer (H1, Vx1, Vy1)

        {
            Swap(ref _rtH0, ref _rtH1);

            _csUpdateHProps.Initialise(_buf, _csUpdateH, _krnlUpdateH);

            _csUpdateHProps.SetTexture(Shader.PropertyToID("_H0"), _rtH0);
            _csUpdateHProps.SetTexture(Shader.PropertyToID("_H1"), _rtH1);
            _csUpdateHProps.SetTexture(Shader.PropertyToID("_Vx0"), _rtVx0);
            _csUpdateHProps.SetTexture(Shader.PropertyToID("_Vx1"), _rtVx1);
            _csUpdateHProps.SetTexture(Shader.PropertyToID("_Vy0"), _rtVy0);
            _csUpdateHProps.SetTexture(Shader.PropertyToID("_Vy1"), _rtVy1);

            _csUpdateHProps.SetFloat(Shader.PropertyToID("_Time"), Time.time);
            _csUpdateHProps.SetFloat(Shader.PropertyToID("_DomainWidth"), _domainWidth);

            _buf.DispatchCompute(_csUpdateH, _krnlUpdateH, (_rtH1.width + 7) / 8, (_rtH1.height + 7) / 8, 1);
        }

        Graphics.ExecuteCommandBuffer(_buf);

        Shader.SetGlobalTexture("_swsH", _rtH1);
    }
}

// Separate helpers/glue/initialisation/etc
public partial class ShallowWaterSimulation : MonoBehaviour, ILodDataInput
{
    [Space, Header("Debug")]
    [SerializeField] bool _updateInEditMode = false;

    Material _matInjectSWSAnimWaves;

    // Draw to all LODs
    public float Wavelength => 0f;
    public bool Enabled => true;

    void OnEnable()
    {
        if (_csUpdateH == null)
        {
            _csUpdateH = ComputeShaderHelpers.LoadShader("SWEUpdateH");
            _csUpdateHProps = new PropertyWrapperCompute();

            _krnlInitH = _csUpdateH.FindKernel("InitH");
            _krnlUpdateH = _csUpdateH.FindKernel("UpdateH");
        }

        {
            _matInjectSWSAnimWaves = new Material(Shader.Find("Hidden/Crest/Inputs/Animated Waves/Inject SWS"));
            _matInjectSWSAnimWaves.hideFlags = HideFlags.HideAndDontSave;
            _matInjectSWSAnimWaves.SetFloat(RegisterLodDataInputBase.sp_Weight, 1f);
        }

#if UNITY_EDITOR
        EditorApplication.update -= EditorUpdate;
        EditorApplication.update += EditorUpdate;
#endif

        {
            var registrar = RegisterLodDataInputBase.GetRegistrar(typeof(LodDataMgrAnimWaves));
            registrar.Remove(this);
            registrar.Add(0, this);
        }

        Reset();
    }

    RenderTexture CreateSWSRT()
    {
        var result = new RenderTexture(_resolution, _resolution, 0, RenderTextureFormat.RFloat);
        result.enableRandomWrite = true;
        result.Create();
        return result;
    }

    public void Reset()
    {
        _rtH0 = _rtH1 = _rtVx0 = _rtVx1 = _rtVy0 = _rtVy1 = null;

        InitData();

        _buf.Clear();

        {
            _csUpdateHProps.Initialise(_buf, _csUpdateH, _krnlInitH);

            _csUpdateHProps.SetTexture(Shader.PropertyToID("_H0"), _rtH0);
            _csUpdateHProps.SetTexture(Shader.PropertyToID("_H1"), _rtH1);
            _csUpdateHProps.SetTexture(Shader.PropertyToID("_Vx0"), _rtVx0);
            _csUpdateHProps.SetTexture(Shader.PropertyToID("_Vx1"), _rtVx1);
            _csUpdateHProps.SetTexture(Shader.PropertyToID("_Vy0"), _rtVy0);
            _csUpdateHProps.SetTexture(Shader.PropertyToID("_Vy1"), _rtVy1);

            _csUpdateHProps.SetFloat(Shader.PropertyToID("_Time"), Time.time);
            _csUpdateHProps.SetFloat(Shader.PropertyToID("_DomainWidth"), _domainWidth);

            _buf.DispatchCompute(_csUpdateH, _krnlInitH, (_rtH1.width + 7) / 8, (_rtH1.height + 7) / 8, 1);
        }

        Graphics.ExecuteCommandBuffer(_buf);
    }

    public void Draw(LodDataMgr lodData, CommandBuffer buf, float weight, int isTransition, int lodIdx)
    {
        buf.SetGlobalInt(LodDataMgr.sp_LD_SliceIndex, lodIdx);

        buf.DrawProcedural(Matrix4x4.identity, _matInjectSWSAnimWaves, 0, MeshTopology.Triangles, 3);
    }

#if UNITY_EDITOR
    void EditorUpdate()
    {
        if (_updateInEditMode && !EditorApplication.isPlaying)
        {
            Update();
        }
    }
#endif
}

#if UNITY_EDITOR
[CustomEditor(typeof(ShallowWaterSimulation))]
class ShallowWaterSimulationEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        if (GUILayout.Button("Reset"))
        {
            (target as ShallowWaterSimulation).Reset();
        }
    }
}
#endif
