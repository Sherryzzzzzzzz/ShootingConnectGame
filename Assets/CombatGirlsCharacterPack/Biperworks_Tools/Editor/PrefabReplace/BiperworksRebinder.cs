// ========================================
// BiperworksRebinder.cs
// ========================================

// PURPOSE:
//   Editor-time component for rebinding SkinnedMeshRenderer bones by name.
//   Used to transfer outfit meshes between different character rigs.
//
// USAGE:
//   1. Attach this component to a GameObject in the Unity Editor
//   2. Assign the outfit's SkinnedMeshRenderer to 'outfitSMR'
//   3. Assign the target character's rig root and pelvis transforms
//   4. Call Rebind() (via Inspector button or script)
//
// LOCATION:
//   Lives in Editor folder - this is an edit-time tool only.
//   Will NOT be included in game builds.
//
// DEPENDENCIES:
//   - Unity Transform hierarchy
//   - SkinnedMeshRenderer component
//
// ========================================

using System.Collections.Generic;
using UnityEngine;

namespace Biperworks.Tools.PrefabReplace
{
    public class BiperworksRebinder : MonoBehaviour
    {
        [Header("Dress (SkinnedMeshRenderer)")]
        public SkinnedMeshRenderer outfitSMR;

        [Header("A Character Rig")]
        public Transform aRigRoot;
        public Transform aPelvis;

        // ========================================
        // FUNCTION: Rebind
        // ========================================
        // PURPOSE:
        //   Rebinds the outfit's SkinnedMeshRenderer bones to a new character rig
        //   by matching bone names.
        //
        // ALGORITHM:
        //   1. Build a name-to-Transform dictionary from target rig
        //   2. For each bone in outfit, find matching bone by name
        //   3. Replace bone array with new references
        //   4. Set root bone to target pelvis
        //
        // ⚠️ MODIFICATION WARNINGS:
        //   - Name-based matching ONLY - does not consider hierarchy or bone paths
        //   - Duplicate bone names will match the FIRST occurrence found
        //   - Missing bones are set to null - can cause rendering issues
        //   - Changing to path-based matching requires complete algorithm rewrite
        //   - Must set rootBone correctly or mesh will not follow character
        //
        // PARAMETERS:
        //   None (uses public fields)
        //
        // RETURNS:
        //   void
        // ========================================
        public void Rebind()
        {
            // Validate required references
            if (outfitSMR == null || aRigRoot == null || aPelvis == null)
            {
                Debug.LogError("BiperworksRebinder: Reference missing");
                return;
            }

            // Build name-based lookup dictionary for target rig
            // WARNING: First occurrence wins if duplicate names exist
            var map = new Dictionary<string, Transform>();
            foreach (var t in aRigRoot.GetComponentsInChildren<Transform>(true))
                map[t.name] = t;

            // Remap each bone by name
            var newBones = new Transform[outfitSMR.bones.Length];
            for (int i = 0; i < newBones.Length; i++)
            {
                var oldBone = outfitSMR.bones[i];
                if (oldBone != null && map.TryGetValue(oldBone.name, out var newBone))
                    newBones[i] = newBone;
                else
                    Debug.LogWarning($"Bone not found on A: {oldBone?.name}");
            }

            // Apply new bone array and root bone
            outfitSMR.bones = newBones;
            outfitSMR.rootBone = aPelvis;
            outfitSMR.updateWhenOffscreen = true;

            Debug.Log("Outfit rebind complete!");
        }
    }
}
