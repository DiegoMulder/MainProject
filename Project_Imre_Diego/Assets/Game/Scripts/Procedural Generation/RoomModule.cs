using System;
using System.Collections.Generic;
using UnityEngine;
namespace SurvivalFP
{
    public sealed class RoomModule : MonoBehaviour
    {
        [Tooltip("Fallback generation volume (centred on X/Z, floor at Y=0) used only when no occupancy volumes are authored.")]
        public Vector3 size = new Vector3(10, 3, 10);
        public bool staircase;
        public Vector3 navigationAnchor;
        public Vector3 NavigationPosition=>transform.TransformPoint(navigationAnchor);
        [Tooltip("Required on a map's Starting Room: where players spawn, facing its blue arrow. Follows the room's generated rotation.")]
        public Transform playerStart;
        [Tooltip("How its wall lanterns burn. Auto: decided per room from its position (mostly lit, some dim, a few dark), identically on every client.")]
        public LightingMood lighting = LightingMood.Auto;
        // Auto resolves from the room's generated position, so every client picks the same mood.
        public LightingMood ResolvedLighting
        {
            get
            {
                if(lighting!=LightingMood.Auto)return lighting;
                var r=transform.position;
                float roll=Mathf.Sin(Mathf.Round(r.x)*12.9898f+Mathf.Round(r.y)*78.233f+Mathf.Round(r.z)*37.719f)*43758.5453f;roll-=Mathf.Floor(roll);
                return roll<.58f?LightingMood.Lit:roll<.84f?LightingMood.Dim:LightingMood.Dark;
            }
        }
        public RoomConnector[] connectors;
        [Tooltip("Existing child groups that are independently enabled by Spawn Chance. The only furniture randomization system. Networked props (closets) go in a group via a Network Spawn Marker.")]
        public RandomizedStructure[] randomizedStructures = Array.Empty<RandomizedStructure>();
        [Tooltip("Generation-only occupancy boxes in room-local space. Separate from gameplay collision; use several boxes for L-shaped or irregular rooms.")]
        public Bounds[] occupancy = Array.Empty<Bounds>();
        public IReadOnlyList<Bounds> OccupancyVolumes=>occupancy!=null && occupancy.Length>0?occupancy:new[]{new Bounds(Vector3.up*size.y*.5f,size)};
        // Structural parts may never be randomized; a bad entry is ignored with a useful message.
        public bool TryGetStructure(int index,out GameObject target,out string problem)
        {
            target=null;problem=null;
            if(randomizedStructures==null || index<0 || index>=randomizedStructures.Length){problem="index out of range";return false;}
            var candidate=randomizedStructures[index].target;
            if(!candidate){problem=$"Randomized Structures element {index} has no target";return false;}
            if(candidate==gameObject || !candidate.transform.IsChildOf(transform)){problem=$"'{candidate.name}' must be a child of the room, not the root or an outside object";return false;}
            if(candidate.GetComponentInChildren<RoomConnector>(true)){problem=$"'{candidate.name}' contains a connector";return false;}
            if(candidate.GetComponentInChildren<Light>(true)){problem=$"'{candidate.name}' contains a lantern light";return false;}
            var visuals=transform.Find("_Visuals");
            if(visuals && (visuals==candidate.transform || visuals.IsChildOf(candidate.transform))){problem=$"'{candidate.name}' contains the room shell";return false;}
            target=candidate;return true;
        }
        // Room-local footprint of a structure, including networked props it would spawn.
        public Bounds StructureLocalBounds(GameObject target)
        {
            var toRoom=transform.worldToLocalMatrix;var bounds=new Bounds();bool any=false;
            void Add(Vector3 center,Vector3 extents,Matrix4x4 matrix)
            {
                for(int i=0;i<8;i++)
                {
                    var point=matrix.MultiplyPoint3x4(center+Vector3.Scale(extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));
                    if(!any){bounds=new Bounds(point,Vector3.zero);any=true;}else bounds.Encapsulate(point);
                }
            }
            foreach(var renderer in target.GetComponentsInChildren<Renderer>(true))Add(renderer.localBounds.center,renderer.localBounds.extents,toRoom*renderer.transform.localToWorldMatrix);
            foreach(var box in target.GetComponentsInChildren<BoxCollider>(true))Add(box.center,box.size*.5f,toRoom*box.transform.localToWorldMatrix);
            foreach(var marker in target.GetComponentsInChildren<NetworkSpawnMarker>(true))
                if(marker.prefab)
                {
                    var markerMatrix=toRoom*marker.transform.localToWorldMatrix;var prefabRoot=marker.prefab.transform.worldToLocalMatrix;
                    foreach(var renderer in marker.prefab.GetComponentsInChildren<Renderer>(true))Add(renderer.localBounds.center,renderer.localBounds.extents,markerMatrix*prefabRoot*renderer.transform.localToWorldMatrix);
                }
            return bounds;
        }
        void OnValidate()
        {
            if(randomizedStructures==null)return;
            for(int i=0;i<randomizedStructures.Length;i++)
                if(randomizedStructures[i].target && !TryGetStructure(i,out _,out var problem))Debug.LogWarning($"{name}: {problem}. It will not be randomized.",this);
        }
        void OnDrawGizmosSelected()
        {
            Gizmos.matrix=transform.localToWorldMatrix;
            Gizmos.color=new Color(.2f,.8f,1,.35f);
            foreach(var volume in OccupancyVolumes)Gizmos.DrawWireCube(volume.center,volume.size);
            if(connectors==null)return;
            Gizmos.matrix=Matrix4x4.identity;Gizmos.color=Color.yellow;
            foreach(var connector in connectors)
                if(connector){Gizmos.DrawSphere(connector.transform.position,.12f);Gizmos.DrawRay(connector.transform.position,connector.transform.forward);}
        }
    }
    [Serializable] public struct RandomizedStructure
    {
        [Tooltip("An existing child group of this room prefab.")]
        public GameObject target;
        [Range(0, 100)] public float spawnChance;
    }
    [Serializable] public struct WeightedRoom { public RoomModule prefab; [Min(1)] public int weight; }
}
