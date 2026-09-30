using UnityEngine;
namespace SurvivalFP
{
    // A seamless ambience loop synthesised at startup, so the menu needs no audio asset: a low house
    // drone with slow beating, draughts moving through the halls and the occasional creak of old wood.
    public static class MenuAmbience
    {
        public static AudioClip Create(int seed = 1931)
        {
            const int rate = 22050; const float seconds = 24f, fade = 3f;
            int total = Mathf.RoundToInt(rate * (seconds + fade));
            var raw = new float[total];
            var rng = new System.Random(seed);
            float Rand() => (float)rng.NextDouble() * 2f - 1f;
            // Draught: brown noise, low-passed, swelling and falling.
            float brown = 0, lp = 0, gust = 0, gustTarget = .5f;
            // Creaks: short resonant tones gliding down in pitch.
            float creakTime = 3f, creakAge = -1, creakPitch = 0, creakLength = 0, creakPhase = 0;
            for (int i = 0; i < total; i++)
            {
                float t = i / (float)rate;
                float drone = Mathf.Sin(2 * Mathf.PI * 55f * t) * .5f + Mathf.Sin(2 * Mathf.PI * 55.35f * t) * .35f
                    + Mathf.Sin(2 * Mathf.PI * 82.4f * t) * .18f + Mathf.Sin(2 * Mathf.PI * 110.2f * t) * .06f;
                drone *= .55f + .45f * Mathf.Sin(2 * Mathf.PI * t / 11f);
                brown = Mathf.Clamp(brown + Rand() * .02f, -1f, 1f) * .998f;
                lp += (brown - lp) * .05f;
                if (i % 2205 == 0) gustTarget = Mathf.Clamp01(.35f + Rand() * .5f);
                gust += (gustTarget - gust) * .00008f;
                float wind = lp * (1.6f + gust * 3.2f);
                float creak = 0;
                if (creakAge < 0 && t >= creakTime) { creakAge = 0; creakPitch = 170f + (float)rng.NextDouble() * 160f; creakLength = .35f + (float)rng.NextDouble() * .6f; }
                if (creakAge >= 0)
                {
                    float k = creakAge / creakLength;
                    float pitch = creakPitch * (1f - .25f * k) + Mathf.Sin(creakAge * 90f) * 6f;
                    creakPhase += 2 * Mathf.PI * pitch / rate;
                    float body = Mathf.Sign(Mathf.Sin(creakPhase)) * .4f + Mathf.Sin(creakPhase) * .6f;
                    creak = body * Mathf.Sin(Mathf.PI * Mathf.Clamp01(k)) * (.5f + .5f * Mathf.PerlinNoise(creakAge * 40f, 0)) * .09f;
                    creakAge += 1f / rate;
                    if (creakAge > creakLength) { creakAge = -1; creakTime = t + 5f + (float)rng.NextDouble() * 9f; }
                }
                raw[i] = drone * .16f + wind * .5f + creak;
            }
            // Loop seamlessly: the tail crossfades into the head.
            int loop = Mathf.RoundToInt(rate * seconds), blend = total - loop;
            var data = new float[loop];
            for (int i = 0; i < loop; i++) data[i] = raw[i];
            for (int i = 0; i < blend; i++) { float w = i / (float)blend; data[i] = raw[i] * w + raw[loop + i] * (1 - w); }
            float peak = 0; foreach (var s in data) peak = Mathf.Max(peak, Mathf.Abs(s));
            float gain = peak > 0 ? .35f / peak : 1;
            for (int i = 0; i < loop; i++) data[i] *= gain;
            var clip = AudioClip.Create("Menu Ambience", loop, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // One heavy footfall on old boards: a low thump and a short dry scuff.
        public static AudioClip Footstep(int seed)
        {
            const int rate = 22050; int length = rate / 3;
            var data = new float[length]; var rng = new System.Random(seed); float lp = 0;
            float pitch = 62f + (float)rng.NextDouble() * 16f;
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                float thump = Mathf.Sin(2 * Mathf.PI * pitch * t * (1f - t)) * Mathf.Exp(-t * 26f);
                lp += (((float)rng.NextDouble() * 2f - 1f) - lp) * .12f;
                float scuff = lp * Mathf.Exp(-t * 40f) * .5f;
                float board = Mathf.Sin(2 * Mathf.PI * 190f * t) * Mathf.Exp(-t * 18f) * .08f;
                data[i] = (thump + scuff + board) * .9f;
            }
            var clip = AudioClip.Create("Footstep", length, 1, rate, false); clip.SetData(data, 0); return clip;
        }

        // A slow hinge groan: a rough tone gliding down, swelling and fading.
        public static AudioClip Creak(int seed, float seconds = 2.2f)
        {
            const int rate = 22050; int length = Mathf.RoundToInt(rate * seconds);
            var data = new float[length]; var rng = new System.Random(seed); float phase = 0;
            float start = 210f + (float)rng.NextDouble() * 80f;
            for (int i = 0; i < length; i++)
            {
                float k = i / (float)length, age = i / (float)rate;
                float pitch = start * (1f - .35f * k) + Mathf.Sin(age * 23f) * 9f + Mathf.PerlinNoise(age * 7f, seed) * 14f;
                phase += 2 * Mathf.PI * pitch / rate;
                float body = Mathf.Sign(Mathf.Sin(phase)) * .35f + Mathf.Sin(phase) * .45f + Mathf.Sin(phase * 2.01f) * .2f;
                float stick = .55f + .45f * Mathf.PerlinNoise(age * 38f, 1f);
                data[i] = body * stick * Mathf.Sin(Mathf.PI * k) * .35f;
            }
            var clip = AudioClip.Create("Creak", length, 1, rate, false); clip.SetData(data, 0); return clip;
        }
    }
}
