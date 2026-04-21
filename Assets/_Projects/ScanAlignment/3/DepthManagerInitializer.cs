using System.Collections;
using Meta.XR.EnvironmentDepth;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.XR.OpenXR.Features.Meta;

/// <summary>
/// Monitors EnvironmentDepthManager state for debugging.
/// The EnvironmentDepthManager should be DISABLED in the Inspector —
/// this script enables it once, without any disable/re-enable cycle.
/// </summary>
public class DepthManagerInitializer : MonoBehaviour
{
    [SerializeField] private EnvironmentDepthManager _depthManager;
    [SerializeField] private bool _enableDebugLogging = true;
    [SerializeField] private float _logInterval = 2f;

    private float _nextLogTime;
    private bool _hasStartedSubsystemCheck;

    IEnumerator Start()
    {
        if (_depthManager == null)
            _depthManager = GetComponent<EnvironmentDepthManager>();

        if (_depthManager == null)
        {
            Debug.LogError("[DepthDebug] No EnvironmentDepthManager found.");
            yield break;
        }

        // IMPORTANT: Do NOT disable the depth manager if it's already enabled.
        // Disabling then re-enabling causes the OcclusionSubsystem to Stop() then Start(),
        // which can cause XR_ERROR_CALL_ORDER_INVALID.
        if (_depthManager.enabled)
        {
            Log("DepthManager already enabled — leaving it alone.");
        }
        else
        {
            Log("DepthManager disabled in Inspector. Waiting for session before enabling...");

            Log($"IsSupported: {EnvironmentDepthManager.IsSupported}");

            // Wait for permission
            Log("Waiting for USE_SCENE permission...");
            float permTimeout = 30f;
            while (!Permission.HasUserAuthorizedPermission("com.oculus.permission.USE_SCENE") && permTimeout > 0)
            {
                permTimeout -= 0.5f;
                yield return new WaitForSeconds(0.5f);
            }
            Log($"USE_SCENE permission: {Permission.HasUserAuthorizedPermission("com.oculus.permission.USE_SCENE")}");

            // Wait for HMD + input focus
            while (!OVRManager.isHmdPresent)
                yield return null;
            Log("HMD present.");

            while (!OVRManager.hasInputFocus)
                yield return null;
            Log("Input focus acquired.");

            // Wait for passthrough to be fully active
            yield return new WaitForSeconds(2f);

            // Enable depth manager ONCE (no prior disable cycle)
            Log($"Enabling DepthManager. Mode: {_depthManager.OcclusionShadersMode}, RemoveHands: {_depthManager.RemoveHands}");
            _depthManager.enabled = true;
        }

        // Check subsystem state after enabling
        yield return new WaitForSeconds(0.5f);
        CheckSubsystemState();

        // Monitor if depth becomes available
        float waitForDepth = 10f;
        while (!_depthManager.IsDepthAvailable && waitForDepth > 0)
        {
            waitForDepth -= 0.5f;
            yield return new WaitForSeconds(0.5f);
        }

        if (_depthManager.IsDepthAvailable)
            Log("Depth is now available!");
        else
            Debug.LogError("[DepthDebug] Depth never became available after 10 seconds.");
    }

    private void CheckSubsystemState()
    {
        var xrLoader = UnityEngine.XR.Management.XRGeneralSettings.Instance?.Manager?.activeLoader;
        Log($"XR Loader type: {xrLoader?.GetType().Name ?? "NULL"}");

        if (xrLoader is UnityEngine.XR.OpenXR.OpenXRLoader openXRLoader)
        {
            var occSub = openXRLoader.GetLoadedSubsystem<UnityEngine.XR.ARSubsystems.XROcclusionSubsystem>() as MetaOpenXROcclusionSubsystem;
            if (occSub == null)
            {
                Debug.LogError("[DepthDebug] MetaOpenXROcclusionSubsystem is NULL!");
            }
            else
            {
                Log($"OcclusionSubsystem: Running={occSub.running}");
                Log($"HandRemoval supported: {occSub.isHandRemovalSupported}");
            }

            var displaySub = openXRLoader.GetLoadedSubsystem<UnityEngine.XR.XRDisplaySubsystem>();
            Log($"DisplaySubsystem: {(displaySub != null ? $"running={displaySub.running}" : "NULL")}");
        }
    }

    void Update()
    {
        if (!_enableDebugLogging || _depthManager == null)
            return;

        if (Time.time < _nextLogTime)
            return;
        _nextLogTime = Time.time + _logInterval;

        Log($"State: enabled={_depthManager.enabled}, " +
            $"IsDepthAvailable={_depthManager.IsDepthAvailable}, " +
            $"Mode={_depthManager.OcclusionShadersMode}");

        bool hardOn = Shader.IsKeywordEnabled(EnvironmentDepthManager.HardOcclusionKeyword);
        bool softOn = Shader.IsKeywordEnabled(EnvironmentDepthManager.SoftOcclusionKeyword);
        Log($"Shader keywords: HARD_OCCLUSION={hardOn}, SOFT_OCCLUSION={softOn}");

        var depthTex = Shader.GetGlobalTexture("_EnvironmentDepthTexture");
        Log($"_EnvironmentDepthTexture: {(depthTex != null ? $"{depthTex.width}x{depthTex.height}" : "NULL")}");
    }

    private void Log(string msg)
    {
        if (_enableDebugLogging)
            Debug.Log($"[DepthDebug] {msg}");
    }
}