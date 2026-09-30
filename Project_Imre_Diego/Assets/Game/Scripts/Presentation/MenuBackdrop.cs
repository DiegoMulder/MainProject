using UnityEngine;
using UnityEngine.Rendering;
namespace SurvivalFP
{
    // The main menu's living background: the real Grand Hall, lit by its lanterns and chandelier, seen
    // through a camera that drifts very slowly through the hall. Now and then a shadow crosses the gallery,
    // the door at the top of the stairs eases open, or unseen footsteps cross the gallery above.
    // Presentation only; it never touches the session, lobby or networking.
    public sealed class MenuBackdrop : MonoBehaviour
    {
        [SerializeField] GameObject roomPrefab;
        [SerializeField] MapAtmosphere atmosphere;
        [SerializeField] Camera view;
        [Tooltip("Camera path through the room, in room space. Looped with smooth interpolation.")]
        [SerializeField] Vector3[] path = { new(2.6f, 1.6f, 9.8f), new(1f, 1.65f, 7.4f), new(-1.3f, 1.7f, 7f), new(-2.8f, 1.6f, 9.4f) };
        [SerializeField] Vector3[] lookAt = { new(.8f, 2.1f, -6f), new(0f, 2.4f, -8f), new(-.8f, 2.2f, -6f), new(0f, 1.9f, -5f) };
        [SerializeField, Min(10f)] float loopSeconds = 90f;
        [SerializeField, Range(0f, 1f)] float ambienceVolume = .55f;
        [Tooltip("Seconds between shadows passing along the gallery.")]
        [SerializeField] Vector2 passerInterval = new(28f, 55f);
        [Tooltip("Door whose Hinge model hangs in the gallery's far doorway (only its visual; no networking).")]
        [SerializeField] GameObject doorPrefab;
        [Tooltip("Seconds between rare events: the far door easing open, footsteps crossing the gallery.")]
        [SerializeField] Vector2 eventInterval = new(38f, 75f);

        Transform room, passer, doorHinge; AudioSource ambience, eventSource;
        float passerTimer, passerProgress = -1f; Vector3 passerFrom, passerTo;
        float eventTimer, doorAngle, doorTarget, doorHold;

        void Start()
        {
            if (!view) view = Camera.main;
            if (atmosphere) atmosphere.Apply();
            if (roomPrefab)
            {
                room = Instantiate(roomPrefab, transform).transform;
                // Show every furniture piece: the menu is a composed shot, not a random roll.
                var module = room.GetComponent<RoomModule>();
                if (module) foreach (var s in module.randomizedStructures) if (s.target) s.target.SetActive(!s.target.GetComponentInChildren<NetworkSpawnMarker>(true));
                // Networked props (closets) cannot spawn without a session; show just their model where the room places them.
                if (module) foreach (var marker in room.GetComponentsInChildren<NetworkSpawnMarker>(true))
                {
                    if (!marker.prefab) continue;
                    var model = marker.prefab.GetComponentsInChildren<MeshRenderer>(true);
                    foreach (var part in model)
                    {
                        if (!part.enabled) continue;
                        var copy = Instantiate(part.gameObject, marker.transform.position, marker.transform.rotation * part.transform.localRotation, room);
                        copy.transform.localScale = part.transform.lossyScale;
                        foreach (var extra in copy.GetComponents<Component>()) if (!(extra is Transform || extra is MeshFilter || extra is MeshRenderer)) Destroy(extra);
                    }
                }
            }
            if (view)
            {
                view.clearFlags = CameraClearFlags.SolidColor; view.backgroundColor = Color.black;
                ambience = view.gameObject.AddComponent<AudioSource>();
                ambience.clip = MenuAmbience.Create(); ambience.loop = true; ambience.volume = ambienceVolume; ambience.spatialBlend = 0;
                AudioVolumeBus.RouteSfx(ambience); ambience.Play();
            }
            // An unseen figure: a shadow-only capsule walking the gallery past the lanterns.
            passer = GameObject.CreatePrimitive(PrimitiveType.Capsule).transform;
            passer.name = "Gallery Passer"; passer.SetParent(transform, false); passer.localScale = new Vector3(.55f, .9f, .55f);
            Destroy(passer.GetComponent<Collider>());
            var r = passer.GetComponent<MeshRenderer>(); r.shadowCastingMode = ShadowCastingMode.ShadowsOnly; r.receiveShadows = false;
            passer.gameObject.SetActive(false);
            passerTimer = Random.Range(8f, 16f);
            HangFarDoor();
            eventSource = new GameObject("Menu Event Sound").AddComponent<AudioSource>();
            eventSource.transform.SetParent(transform, false);
            eventSource.spatialBlend = 1f; eventSource.minDistance = 4f; eventSource.maxDistance = 40f; eventSource.rolloffMode = AudioRolloffMode.Linear; eventSource.dopplerLevel = 0;
            eventSource.gameObject.AddComponent<AudioLowPassFilter>().cutoffFrequency = 1400f; // heard through the house
            AudioVolumeBus.RouteSfx(eventSource);
            eventTimer = Random.Range(18f, 30f);
        }

        // The doorway at the top of the stairs gets a real door model, closed, so it can later ease open.
        void HangFarDoor()
        {
            var hinge = doorPrefab ? doorPrefab.transform.Find("Hinge") : null;
            var module = room ? room.GetComponent<RoomModule>() : null;
            if (!hinge || !module) return;
            RoomConnector socket = null;
            foreach (var c in module.connectors) if (c && c.name.Contains("Gallery South")) socket = c;
            if (!socket) return;
            var pivot = new GameObject("Far Door").transform;
            pivot.SetParent(room, false); pivot.SetPositionAndRotation(socket.transform.position, socket.transform.rotation);
            doorHinge = Instantiate(hinge.gameObject, pivot).transform;
            doorHinge.localPosition = hinge.localPosition; doorHinge.localRotation = Quaternion.identity;
            foreach (var col in doorHinge.GetComponentsInChildren<Collider>()) Destroy(col);
            var panel = doorHinge.Find("Door panel"); if (panel) panel.gameObject.SetActive(false);
        }

        // Rare and quiet, never a jump scare: the far door eases open a hand's width and settles, or someone
        // unseen walks the gallery. They alternate with the shadow passing the lanterns.
        void UpdateEvents()
        {
            if (doorHinge)
            {
                if (doorHold > 0 && Mathf.Abs(doorAngle - doorTarget) < .05f && (doorHold -= Time.deltaTime) <= 0) doorTarget = 0;
                doorAngle = Mathf.MoveTowards(doorAngle, doorTarget, Time.deltaTime * (doorTarget == 0 ? 3f : 5f));
                doorHinge.localRotation = Quaternion.Euler(0, doorAngle, 0);
            }
            eventTimer -= Time.deltaTime;
            if (eventTimer > 0 || !eventSource) return;
            eventTimer = Random.Range(eventInterval.x, eventInterval.y);
            if (doorHinge && Random.value < .5f)
            {
                doorTarget = -Random.Range(9f, 16f); doorHold = Random.Range(6f, 12f);
                eventSource.transform.position = doorHinge.position + Vector3.up * 1.2f;
                eventSource.PlayOneShot(MenuAmbience.Creak(Random.Range(0, 999)), .55f);
            }
            else StartCoroutine(Footsteps());
        }

        System.Collections.IEnumerator Footsteps()
        {
            float side = Random.value < .5f ? -1f : 1f;
            Vector3 from = room.TransformPoint(new Vector3(side * 7.8f, 4.1f, 9f)), to = room.TransformPoint(new Vector3(side * 7.8f, 4.1f, -4f));
            int steps = Random.Range(5, 9);
            for (int s = 0; s < steps && eventSource; s++)
            {
                eventSource.transform.position = Vector3.Lerp(from, to, s / (float)steps);
                eventSource.PlayOneShot(MenuAmbience.Footstep(Random.Range(0, 999)), .5f * (1f - .06f * s));
                yield return new WaitForSeconds(Random.Range(.52f, .68f));
            }
        }

        void LateUpdate()
        {
            if (!view || !room || path.Length < 2) return;
            float t = Time.time / loopSeconds * path.Length;
            int i = Mathf.FloorToInt(t); float f = t - i;
            Vector3 P(Vector3[] a, int k) => room.TransformPoint(a[((k % a.Length) + a.Length) % a.Length]);
            var position = CatmullRom(P(path, i - 1), P(path, i), P(path, i + 1), P(path, i + 2), f);
            var target = CatmullRom(P(lookAt, i - 1), P(lookAt, i), P(lookAt, i + 1), P(lookAt, i + 2), f);
            // A slight handheld breath, far below anything that would read as motion sickness.
            position += new Vector3(Mathf.PerlinNoise(Time.time * .11f, 3f) - .5f, Mathf.PerlinNoise(Time.time * .09f, 7f) - .5f, 0) * .06f;
            view.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position, Vector3.up));
            UpdatePasser();
            UpdateEvents();
        }

        void UpdatePasser()
        {
            if (passerProgress < 0)
            {
                passerTimer -= Time.deltaTime;
                if (passerTimer > 0) return;
                // Along one gallery side, just inside the balustrade, between the lanterns and the camera.
                float side = Random.value < .5f ? -1f : 1f;
                passerFrom = room.TransformPoint(new Vector3(side * 7.3f, 4.9f, 10.5f));
                passerTo = room.TransformPoint(new Vector3(side * 7.3f, 4.9f, -8.5f));
                if (Random.value < .5f) (passerFrom, passerTo) = (passerTo, passerFrom);
                passerProgress = 0; passer.gameObject.SetActive(true);
            }
            passerProgress += Time.deltaTime / 14f;
            passer.position = Vector3.Lerp(passerFrom, passerTo, passerProgress) + Vector3.up * Mathf.Abs(Mathf.Sin(passerProgress * 60f)) * .03f;
            LanternLight.ShadowFocus = passer.position; // the gallery lanterns it walks past cast its shadow
            if (passerProgress >= 1f) { passerProgress = -1f; passer.gameObject.SetActive(false); passerTimer = Random.Range(passerInterval.x, passerInterval.y); LanternLight.ShadowFocus = null; }
        }

        void OnDestroy() => LanternLight.ShadowFocus = null;

        static Vector3 CatmullRom(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return .5f * (2f * b + (c - a) * t + (2f * a - 5f * b + 4f * c - d) * t2 + (3f * b - a - 3f * c + d) * t3);
        }
    }
}
