using UnityEngine;
using UnityEngine.Rendering;
namespace SurvivalFP
{
    // Presentation for one map: ambient light, distance fog, grime and the post-processing profile.
    // Applied locally on every peer when a round starts; never touches gameplay or networking.
    [CreateAssetMenu(menuName = "Survival FP/Map Atmosphere")]
    public sealed class MapAtmosphere : ScriptableObject
    {
        [Header("Ambient")]
        [Tooltip("Flat fill light. Keep it very low: lanterns and the flashlight should do the work.")]
        public Color ambient = new(.04f, .04f, .048f);
        [Header("Fog")]
        public bool fog = true;
        public Color fogColour = new(.02f, .018f, .018f);
        [Range(0f, .2f)] public float fogDensity = .055f;
        [Header("Surfaces")]
        [Tooltip("Colour that grime darkens surfaces towards (PSX Lit materials).")]
        public Color grimeTint = new(.42f, .36f, .3f);
        [Range(0f, 2f)] public float grimeScale = 1f;
        [Tooltip("Height of the dirty band along the base of walls.")]
        [Range(0f, 1f)] public float grimeHeight = .35f;
        [Tooltip("Storey spacing, so the wall-base grime repeats on every floor.")]
        [Min(1f)] public float storeyHeight = 4f;
        [Header("Post processing")]
        public VolumeProfile postProfile;
        public Color cameraBackground = Color.black;

        static readonly int GrimeTint = Shader.PropertyToID("_PsxGrimeTint"), GrimeParams = Shader.PropertyToID("_PsxGrimeParams");
        static Volume volume;

        // The profile most recently applied; cameras created later (player cameras) adopt its background.
        public static MapAtmosphere Current { get; private set; }
        public static void ApplyToCamera(Camera cam)
        {
            if (!Current || !cam || cam.clearFlags != CameraClearFlags.Skybox) return;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Current.cameraBackground;
        }

        public void Apply()
        {
            Current = this;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ambient;
            RenderSettings.fog = fog; RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = fogColour; RenderSettings.fogDensity = fogDensity;
            RenderSettings.skybox = null;
            ApplySurfaces();
            if (!volume)
            {
                var go = new GameObject("Map Atmosphere Volume");
                volume = go.AddComponent<Volume>(); volume.isGlobal = true; volume.priority = 10;
            }
            volume.sharedProfile = postProfile;
            foreach (var cam in Camera.allCameras) if (cam.cameraType == CameraType.Game) ApplyToCamera(cam);
        }

        public void ApplySurfaces()
        {
            Shader.SetGlobalVector(GrimeTint, grimeTint);
            Shader.SetGlobalVector(GrimeParams, new Vector4(storeyHeight, grimeHeight, grimeScale, 0));
        }
    }
}
