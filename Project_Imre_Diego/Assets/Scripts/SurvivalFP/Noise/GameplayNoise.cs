using System;
using UnityEngine;
namespace SurvivalFP
{
    public enum NoiseCategory { Footstep, Impact, Door, Other, Voice, Flashlight }
    public readonly struct GameplayNoise
    {
        public readonly Vector3 Position;
        public readonly float Radius, Time;
        public readonly NoiseCategory Category;
        public readonly GameObject Source;
        public GameplayNoise(Vector3 position,float radius,NoiseCategory category,GameObject source)
        { Position=position; Radius=radius; Category=category; Source=source; Time=UnityEngine.Time.time; }
    }
    public static class GameplayNoiseSystem
    {
        public static event Action<GameplayNoise> Emitted;
        public static void Emit(Vector3 position,float radius,NoiseCategory category,GameObject source)
        {
            var player = source ? source.GetComponentInParent<NetworkPlayer>() : null;
            if (player && player.HiddenCloset) radius *= player.HiddenCloset.noiseMultiplier;
            if (radius > 0) Emitted?.Invoke(new GameplayNoise(position,radius,category,source));
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() => Emitted=null;
    }
}
