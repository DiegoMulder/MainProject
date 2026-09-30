#if UNITY_EDITOR
using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SurvivalFP.EditorTools
{
    public static class HeldItemPoseValidation
    {
        const string PlayerPath = "Assets/Game/Prefabs/Player/Network Survivor.prefab";
        const string ClipPath = "Assets/Game/Player/business-man-low-polygon-game-character/Animation/Items/Holding_Medkit.anim";

        [MenuItem("Tools/Validation/Medkit Holding Pose")]
        public static void Validate() => Debug.Log(Run());

        // Direct clip sampling reproduces Animation-window preview. The controller
        // also evaluates locomotion, speech and item switching, as remote players do.
        public static string Run()
        {
            GameObject reference = null, evaluated = null;
            try
            {
                reference = PrefabUtility.LoadPrefabContents(PlayerPath);
                evaluated = PrefabUtility.LoadPrefabContents(PlayerPath);
                var preview = reference.GetComponentInChildren<Animator>(true);
                var animator = evaluated.GetComponentInChildren<Animator>(true);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
                clip.SampleAnimation(preview.gameObject, 0f);

                var expectedBones = preview.GetComponentsInChildren<Transform>(true)
                    .Where(t => AnimationUtility.CalculateTransformPath(t, preview.transform).Contains("/Clavicle_"))
                    .ToArray();
                var actualBones = expectedBones.Select(t => animator.transform.Find(
                    AnimationUtility.CalculateTransformPath(t, preview.transform))).ToArray();
                var expectedAnchor = expectedBones.First(t => t.name == "Right Hand Item Hold");
                var actualAnchor = actualBones.First(t => t.name == "Right Hand Item Hold");
                int itemsLayer = animator.GetLayerIndex("Items");
                int mouthLayer = animator.GetLayerIndex("Mouth Layer");
                if (itemsLayer < 0 || mouthLayer < 0 || actualBones.Any(t => !t))
                    throw new InvalidOperationException("Missing holding-pose rig or animator layers.");

                var report = new StringBuilder("Medkit holding pose: preview versus multiplayer Animator controller\n");
                foreach (string mode in new[] { "idle", "walk", "run", "crouch" })
                foreach (bool talking in new[] { false, true })
                {
                    animator.Rebind();
                    animator.SetFloat("Blend", mode == "walk" ? .5f : mode == "run" ? 1f : 0f);
                    animator.SetBool("IsCrouching", mode == "crouch");
                    animator.SetFloat("Crouch", .5f);
                    animator.SetBool("IsTalking", talking);
                    animator.SetLayerWeight(mouthLayer, talking ? 1f : 0f);
                    animator.SetFloat("ItemNumber", 2f);
                    animator.Update(.1f);
                    animator.SetFloat("ItemNumber", 3f);

                    float maxPositionError = 0f, maxRotationError = 0f;
                    for (int frame = 0; frame < 120; frame++)
                    {
                        animator.Update(1f / 60f);
                        for (int i = 0; i < expectedBones.Length; i++)
                        {
                            // Item-relative coordinates allow normal torso motion,
                            // while detecting shoulder drift and finger penetration.
                            float distance = Vector3.Distance(
                                expectedAnchor.InverseTransformPoint(expectedBones[i].position),
                                actualAnchor.InverseTransformPoint(actualBones[i].position));
                            float angle = Quaternion.Angle(expectedBones[i].localRotation, actualBones[i].localRotation);
                            maxPositionError = Mathf.Max(maxPositionError, distance);
                            maxRotationError = Mathf.Max(maxRotationError, angle);
                            if (distance > .001f || angle > .1f)
                                throw new InvalidOperationException($"{mode}, talking={talking}, frame={frame}: " +
                                    $"{expectedBones[i].name} differs from preview by {distance * 1000f:F4} mm / {angle:F4} degrees.");
                        }
                    }
                    var activeClip = animator.GetCurrentAnimatorClipInfo(itemsLayer);
                    if (!activeClip.Any(c => c.clip == clip && c.weight > .999f))
                        throw new InvalidOperationException("Medkit is not the active Items pose.");
                    report.AppendLine($"{mode}, talking={talking}: {maxPositionError * 1000f:F5} mm, {maxRotationError:F5} degrees");
                }
                return report.ToString();
            }
            finally
            {
                if (reference) PrefabUtility.UnloadPrefabContents(reference);
                if (evaluated) PrefabUtility.UnloadPrefabContents(evaluated);
            }
        }
    }
}
#endif
