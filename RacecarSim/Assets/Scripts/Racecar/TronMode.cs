using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

/// <summary>
/// Toggleable Tron visual mode — transforms the scene into a dark, nighttime-style
/// environment with emissive surfaces, a glowing car with dual adaptive headlights,
/// and anti-aliased edge-detected wireframe overlay with built-in bloom.
/// </summary>
public class TronMode : MonoBehaviour
{
    [Header("Regular Ambient")]
    [SerializeField] private Color regularAmbientColor = new Color(0.5f, 0.5f, 0.55f, 1f);
    [SerializeField] private float regularAmbientIntensity = 1.0f;

    [Header("Skybox")]
    [SerializeField] private Color skyboxColor = new Color(10f / 255f, 22f / 255f, 46f / 255f, 1f);
    [SerializeField] private float tronAmbientIntensity = 0.07f;

    [Header("Sun")]
    [SerializeField] private float tronSunIntensity = 0.3f;
    [SerializeField] private Color tronSunColor = new Color(111f / 255f, 116f / 255f, 253f / 255f, 1f);

    [Header("Post Processing")]
    [SerializeField] private float tronBloomIntensity = 5f;
    [SerializeField] private float tronBloomThreshold = 1.1f;
    [SerializeField] private float tronBloomSoftKnee = 0.325f;
    [SerializeField] private float tronContrast = 20f;

    [Header("Environment Surfaces")]
    [SerializeField] private float envEmission = 0.4f;
    [SerializeField] private float envBaseDarken = 0.3f;

    [Header("Car")]
    [SerializeField] private float carEmission = 2.0f;
    [SerializeField] private float carBaseDarken = 0.05f;

    [Header("Car Ambient Glow")]
    [SerializeField] private float glowRange = 15f;
    [SerializeField] private float glowIntensity = 2f;
    [SerializeField] private Color glowColor = Color.cyan;

    [Header("Car Headlights")]
    [SerializeField] private float headlightRange = 25f;
    [SerializeField] private float headlightIntensity = 4f;
    [SerializeField] private Color headlightColor = new Color(0.75f, 0.88f, 1f, 1f);
    [SerializeField] private float headlightSpacing = 0.6f;
    [SerializeField] private float headlightAngle = 50f;
    [SerializeField] private float headlightToeIn = 3f;
    [SerializeField] private float headlightDownTilt = 15f;
    [SerializeField] private float headlightSteerFollow = 0.5f;

    [Header("Wireframe Overlay")]
    [SerializeField] private float wireOverlayIntensity = 3.0f;
    [SerializeField] private float wireOverlayThreshold = 0.1f;
    [SerializeField] private float wireOverlayAlpha = 0.5f;
    [SerializeField] private float wireBloomIntensity = 1.5f;
    [SerializeField] private float wireBloomSize = 2.0f;
    [SerializeField] private int wireBloomPasses = 2;

    private bool tronActive = false;

    // Stored original state — skybox / ambient
    private Material savedSkybox;
    private Color savedAmbientColor;
    private float savedAmbientIntensity;
    private float savedReflectionIntensity;

    // Stored original state — sun
    private Light sunLight;
    private float savedSunIntensity;
    private Color savedSunColor;

    // Stored original state — quality
    private int savedPixelLightCount;

    // Stored original state — post-processing
    private PostProcessVolume postProcessVolume;
    private bool ppSaved = false;
    private float savedBloomIntensity;
    private float savedBloomThreshold;
    private float savedBloomSoftKnee;
    private float savedContrast;

    // Stored original state — car materials (all body parts)
    private Dictionary<Renderer, Material[]> savedCarMaterials = new Dictionary<Renderer, Material[]>();

    // Stored original state — environment materials
    private Dictionary<Renderer, Material[]> savedMaterials = new Dictionary<Renderer, Material[]>();

    // Camera state
    private Dictionary<Camera, CameraClearFlags> savedClearFlags = new Dictionary<Camera, CameraClearFlags>();
    private Dictionary<Camera, Color> savedBackgroundColors = new Dictionary<Camera, Color>();

    // Dynamic car lights
    private GameObject carLightObj;

    void Start()
    {
        // Set regular ambient lighting on scene load
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = regularAmbientColor;
        RenderSettings.ambientIntensity = regularAmbientIntensity;

        // Find the sun (first directional light)
        Light[] lights = FindObjectsOfType<Light>();
        foreach (Light light in lights)
        {
            if (light.type == LightType.Directional)
            {
                sunLight = light;
                break;
            }
        }

        // Find post-process volume
        postProcessVolume = FindPlayerPostProcessVolume();

        Debug.Log($"[TronMode] Sun: {sunLight != null}, " +
                  $"PostProcess: {postProcessVolume != null}");
    }

    /// <summary>
    /// Toggle Tron mode on/off.
    /// </summary>
    public void Toggle()
    {
        if (tronActive)
            DeactivateTron();
        else
            ActivateTron();

        tronActive = !tronActive;
        Debug.Log($"[TronMode] {(tronActive ? "ON" : "OFF")}, " +
                  $"car parts: {savedCarMaterials.Count}, " +
                  $"environment: {savedMaterials.Count}");
    }

    /// <summary>
    /// Whether Tron mode is currently active.
    /// </summary>
    public bool IsActive => tronActive;

    private void ActivateTron()
    {
        // --- Quality: increase pixel light count for all our dynamic lights ---
        savedPixelLightCount = QualitySettings.pixelLightCount;
        QualitySettings.pixelLightCount = Mathf.Max(savedPixelLightCount, 8);

        // --- Skybox / Ambient ---
        savedSkybox = RenderSettings.skybox;
        savedAmbientColor = RenderSettings.ambientLight;
        savedAmbientIntensity = RenderSettings.ambientIntensity;
        savedReflectionIntensity = RenderSettings.reflectionIntensity;

        RenderSettings.skybox = null;
        RenderSettings.ambientLight = skyboxColor;
        RenderSettings.ambientIntensity = tronAmbientIntensity;
        RenderSettings.reflectionIntensity = 0.3f;

        // --- Sun: tinted, dimmed ---
        if (sunLight != null)
        {
            savedSunIntensity = sunLight.intensity;
            savedSunColor = sunLight.color;
            sunLight.intensity = tronSunIntensity;
            sunLight.color = tronSunColor;
        }

        // --- Post-processing: boost bloom and contrast ---
        postProcessVolume = FindPlayerPostProcessVolume();
        if (postProcessVolume != null && postProcessVolume.profile != null)
        {
            if (postProcessVolume.profile.TryGetSettings(out Bloom bloom))
            {
                if (!ppSaved)
                {
                    savedBloomIntensity = bloom.intensity.value;
                    savedBloomThreshold = bloom.threshold.value;
                    savedBloomSoftKnee = bloom.softKnee.value;
                }
                bloom.intensity.value = tronBloomIntensity;
                bloom.threshold.value = tronBloomThreshold;
                bloom.softKnee.value = tronBloomSoftKnee;
            }

            if (postProcessVolume.profile.TryGetSettings(out ColorGrading grading))
            {
                if (!ppSaved)
                {
                    savedContrast = grading.contrast.value;
                }
                grading.contrast.value = tronContrast;
            }

            ppSaved = true;
        }

        // --- Cameras to solid dark background ---
        Camera[] allCams = FindObjectsOfType<Camera>();
        foreach (Camera cam in allCams)
        {
            savedClearFlags[cam] = cam.clearFlags;
            savedBackgroundColors[cam] = cam.backgroundColor;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = skyboxColor;
        }

        // --- Car materials: strong emissive glow ---
        Renderer[] carRenderers = GetComponentsInChildren<Renderer>();
        foreach (Renderer renderer in carRenderers)
        {
            savedCarMaterials[renderer] = renderer.materials;
            ApplyCarTronEmissive(renderer);
        }

        // --- Environment materials: subtle emissive ---
        Renderer[] allRenderers = FindObjectsOfType<Renderer>();
        foreach (Renderer renderer in allRenderers)
        {
            if (savedCarMaterials.ContainsKey(renderer)) continue;
            if (renderer.gameObject.layer == LayerMask.NameToLayer("UI")) continue;
            if (renderer is ParticleSystemRenderer) continue;
            if (renderer is SpriteRenderer) continue;
            if (renderer is LineRenderer) continue;
            if (renderer is TrailRenderer) continue;
            if (savedMaterials.ContainsKey(renderer)) continue;

            savedMaterials[renderer] = renderer.materials;
            ApplyEnvironmentTron(renderer);
        }

        // --- Wireframe overlay on player cameras ---
        foreach (Camera cam in FindObjectsOfType<Camera>())
        {
            if (cam.gameObject.name == "MainCamera" ||
                cam.gameObject.name == "OverheadCamera" ||
                cam.gameObject.name == "RearViewCamera")
            {
                WireframeOverlay overlay = cam.gameObject.AddComponent<WireframeOverlay>();
                overlay.wireIntensity = wireOverlayIntensity;
                overlay.wireThreshold = wireOverlayThreshold;
                overlay.wireAlpha = wireOverlayAlpha;
                overlay.wireBloomIntensity = wireBloomIntensity;
                overlay.wireBloomSize = wireBloomSize;
                overlay.wireBloomPasses = wireBloomPasses;
            }
        }

        // --- Dynamic car lights ---
        carLightObj = new GameObject("TronCarLights");
        carLightObj.transform.SetParent(transform);
        carLightObj.transform.localPosition = Vector3.zero;
        carLightObj.transform.localRotation = Quaternion.identity;

        // Underglow — three spots aimed down from car body center
        // Angled so top of beam is horizontal, all light goes below car level
        GameObject underglowParent = new GameObject("Underglow");
        underglowParent.transform.SetParent(carLightObj.transform);
        underglowParent.transform.localPosition = new Vector3(0f, 0.4f, 0f); // car body center height
        underglowParent.transform.localRotation = Quaternion.identity;

        int playerLayer = LayerMask.NameToLayer("Player");
        int glowCullingMask = playerLayer >= 0 ? ~(1 << playerLayer) : -1;

        string[] dirs = { "Left", "Right", "Rear" };
        // Aim 45° down — with 90° cone, top edge is horizontal, bottom edge hits ground close
        Vector3[] rotations = {
            new Vector3(45f, -90f, 0f),
            new Vector3(45f, 90f, 0f),
            new Vector3(45f, 180f, 0f)
        };

        for (int i = 0; i < dirs.Length; i++)
        {
            GameObject glowSpot = new GameObject("Glow_" + dirs[i]);
            glowSpot.transform.SetParent(underglowParent.transform);
            glowSpot.transform.localPosition = Vector3.zero;
            glowSpot.transform.localRotation = Quaternion.Euler(rotations[i]);

            Light light = glowSpot.AddComponent<Light>();
            light.type = LightType.Spot;
            light.range = glowRange;
            light.spotAngle = 90f;        // 45° half-angle — top edge exactly horizontal
            light.intensity = glowIntensity;
            light.color = glowColor;
            light.bounceIntensity = 0f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.5f;
            light.shadowNearPlane = 0.1f;
            light.renderMode = LightRenderMode.ForcePixel;
            light.cullingMask = glowCullingMask;
        }
        // Headlight assembly — parent that steers adaptively
        GameObject headlightAssembly = new GameObject("HeadlightAssembly");
        headlightAssembly.transform.SetParent(carLightObj.transform);
        headlightAssembly.transform.localPosition = new Vector3(0f, 0.4f, 1.5f);
        headlightAssembly.transform.localRotation = Quaternion.Euler(headlightDownTilt, 0f, 0f);

        AdaptiveHeadlight adaptive = headlightAssembly.AddComponent<AdaptiveHeadlight>();
        adaptive.steerFollowAmount = headlightSteerFollow;
        adaptive.downTilt = headlightDownTilt;

        // Left headlight
        CreateHeadlight(headlightAssembly.transform, "LeftHeadlight",
            new Vector3(-headlightSpacing, 0f, 0f), -headlightToeIn);

        // Right headlight
        CreateHeadlight(headlightAssembly.transform, "RightHeadlight",
            new Vector3(headlightSpacing, 0f, 0f), headlightToeIn);

        // Force transform sync and align one frame later
        Physics.SyncTransforms();
        StartCoroutine(AlignLightsNextFrame());
    }

    /// <summary>
    /// Create a single headlight with both wide spread and focused beam.
    /// </summary>
    private void CreateHeadlight(Transform parent, string name, Vector3 offset, float toeIn)
    {
        GameObject headlightObj = new GameObject(name);
        headlightObj.transform.SetParent(parent);
        headlightObj.transform.localPosition = offset;
        headlightObj.transform.localRotation = Quaternion.Euler(0f, toeIn, 0f);

        // Wide beam — soft horizontal spread
        GameObject wideObj = new GameObject("WideBeam");
        wideObj.transform.SetParent(headlightObj.transform);
        wideObj.transform.localPosition = Vector3.zero;
        wideObj.transform.localRotation = Quaternion.identity;
        Light wide = wideObj.AddComponent<Light>();
        wide.type = LightType.Spot;
        wide.range = headlightRange * 0.7f;
        wide.spotAngle = headlightAngle * 1.6f;
        wide.intensity = headlightIntensity * 0.3f;
        wide.color = headlightColor;
        wide.bounceIntensity = 0.1f;
        wide.shadows = LightShadows.None;
        wide.renderMode = LightRenderMode.ForcePixel;

        // Focused beam — bright center core
        GameObject focusObj = new GameObject("FocusBeam");
        focusObj.transform.SetParent(headlightObj.transform);
        focusObj.transform.localPosition = Vector3.zero;
        focusObj.transform.localRotation = Quaternion.identity;
        Light focus = focusObj.AddComponent<Light>();
        focus.type = LightType.Spot;
        focus.range = headlightRange;
        focus.spotAngle = headlightAngle;
        focus.intensity = headlightIntensity;
        focus.color = headlightColor;
        focus.bounceIntensity = 0.2f;
        focus.shadows = LightShadows.Soft;
        focus.shadowStrength = 0.6f;
        focus.renderMode = LightRenderMode.ForcePixel;
    }

    private void DeactivateTron()
    {
        // --- Restore quality ---
        QualitySettings.pixelLightCount = savedPixelLightCount;

        // --- Restore skybox / ambient ---
        RenderSettings.skybox = savedSkybox;
        RenderSettings.ambientLight = savedAmbientColor;
        RenderSettings.ambientIntensity = savedAmbientIntensity;
        RenderSettings.reflectionIntensity = savedReflectionIntensity;

        // --- Restore sun ---
        if (sunLight != null)
        {
            sunLight.intensity = savedSunIntensity;
            sunLight.color = savedSunColor;
        }

        // --- Restore post-processing ---
        if (postProcessVolume != null && postProcessVolume.profile != null && ppSaved)
        {
            if (postProcessVolume.profile.TryGetSettings(out Bloom bloom))
            {
                bloom.intensity.value = savedBloomIntensity;
                bloom.threshold.value = savedBloomThreshold;
                bloom.softKnee.value = savedBloomSoftKnee;
            }

            if (postProcessVolume.profile.TryGetSettings(out ColorGrading grading))
            {
                grading.contrast.value = savedContrast;
            }
        }

        // --- Restore cameras ---
        Camera[] allCams = FindObjectsOfType<Camera>();
        foreach (Camera cam in allCams)
        {
            if (savedClearFlags.ContainsKey(cam))
                cam.clearFlags = savedClearFlags[cam];
            if (savedBackgroundColors.ContainsKey(cam))
                cam.backgroundColor = savedBackgroundColors[cam];
        }
        savedClearFlags.Clear();
        savedBackgroundColors.Clear();

        // --- Remove wireframe overlays ---
        WireframeOverlay[] overlays = FindObjectsOfType<WireframeOverlay>();
        foreach (WireframeOverlay overlay in overlays)
        {
            Destroy(overlay);
        }

        // --- Remove car lights ---
        if (carLightObj != null)
        {
            Destroy(carLightObj);
            carLightObj = null;
        }

        // --- Restore car materials ---
        foreach (var kvp in savedCarMaterials)
        {
            if (kvp.Key != null)
                kvp.Key.materials = kvp.Value;
        }
        savedCarMaterials.Clear();

        // --- Restore environment materials ---
        foreach (var kvp in savedMaterials)
        {
            if (kvp.Key != null)
                kvp.Key.materials = kvp.Value;
        }
        savedMaterials.Clear();
    }

    /// <summary>
    /// Ensure light transforms are properly aligned after creation.
    /// </summary>
    private IEnumerator AlignLightsNextFrame()
    {
        yield return null;
        if (carLightObj != null)
        {
            carLightObj.transform.localPosition = Vector3.zero;
            carLightObj.transform.localRotation = Quaternion.identity;
        }
    }

    /// <summary>
    /// Find the correct PostProcessVolume on the player, not a stale scene one.
    /// </summary>
    private PostProcessVolume FindPlayerPostProcessVolume()
    {
        PostProcessVolume[] volumes = FindObjectsOfType<PostProcessVolume>();

        foreach (PostProcessVolume vol in volumes)
        {
            if (vol.transform.IsChildOf(transform))
                return vol;
        }

        foreach (PostProcessVolume vol in volumes)
        {
            if (vol.gameObject.name.Contains("Player") ||
                vol.gameObject.name.Contains("PostProcess") ||
                vol.gameObject.name.Contains("Racecar"))
                return vol;
        }

        foreach (PostProcessVolume vol in volumes)
        {
            if (vol.isGlobal)
                return vol;
        }

        return volumes.Length > 0 ? volumes[0] : null;
    }

    /// <summary>
    /// Apply strong emissive glow to car parts.
    /// </summary>
    private void ApplyCarTronEmissive(Renderer renderer)
    {
        Material[] tronMats = new Material[renderer.materials.Length];

        for (int i = 0; i < renderer.materials.Length; i++)
        {
            Material orig = renderer.materials[i];
            Material mod = new Material(orig);

            Color baseColor = GetMaterialColor(mod);
            if (baseColor == Color.black || baseColor.maxColorComponent < 0.05f)
                baseColor = Color.cyan;

            SetMaterialColor(mod, baseColor * carBaseDarken);

            mod.EnableKeyword("_EMISSION");
            if (mod.HasProperty("_EmissionColor"))
                mod.SetColor("_EmissionColor", baseColor * carEmission);
            else if (mod.HasProperty("_EmissiveColor"))
                mod.SetColor("_EmissiveColor", baseColor * carEmission);

            mod.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            if (mod.HasProperty("_Metallic"))
                mod.SetFloat("_Metallic", 0.9f);
            if (mod.HasProperty("_Glossiness"))
                mod.SetFloat("_Glossiness", 0.95f);

            tronMats[i] = mod;
        }

        renderer.materials = tronMats;
    }

    /// <summary>
    /// Clone environment materials and add emission.
    /// </summary>
    private void ApplyEnvironmentTron(Renderer renderer)
    {
        Material[] tronMats = new Material[renderer.materials.Length];

        for (int i = 0; i < renderer.materials.Length; i++)
        {
            Material orig = renderer.materials[i];
            Material mod = new Material(orig);

            Color baseColor = GetMaterialColor(mod);

            SetMaterialColor(mod, baseColor * envBaseDarken);

            mod.EnableKeyword("_EMISSION");
            Color emissionColor = baseColor * envEmission;

            if (mod.HasProperty("_EmissionColor"))
                mod.SetColor("_EmissionColor", emissionColor);
            else if (mod.HasProperty("_EmissiveColor"))
                mod.SetColor("_EmissiveColor", emissionColor);

            mod.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            tronMats[i] = mod;
        }

        renderer.materials = tronMats;
    }

    /// <summary>
    /// Get the base color from a material, checking common property names.
    /// </summary>
    private Color GetMaterialColor(Material mat)
    {
        if (mat.HasProperty("_Color"))
            return mat.color;
        if (mat.HasProperty("_BaseColor"))
            return mat.GetColor("_BaseColor");
        if (mat.HasProperty("_TintColor"))
            return mat.GetColor("_TintColor");
        return Color.white;
    }

    /// <summary>
    /// Set the base color on a material, checking common property names.
    /// </summary>
    private void SetMaterialColor(Material mat, Color color)
    {
        if (mat.HasProperty("_Color"))
            mat.color = color;
        else if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", color);
        else if (mat.HasProperty("_TintColor"))
            mat.SetColor("_TintColor", color);
    }
}