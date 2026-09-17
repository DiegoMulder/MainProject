#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
namespace SurvivalFP.Editor
{
    public sealed class ImpactAudioValidation:MonoBehaviour
    {
        readonly List<string> results=new();readonly List<GameplayNoise> heard=new();
        void OnEnable(){EditorApplication.update+=Pump;GameplayNoiseSystem.Emitted+=OnNoise;}
        void OnDisable(){EditorApplication.update-=Pump;GameplayNoiseSystem.Emitted-=OnNoise;}
        void Pump(){if(EditorApplication.isPlaying)EditorApplication.QueuePlayerLoopUpdate();}
        void OnNoise(GameplayNoise n)=>heard.Add(n);
        void Check(bool pass,string name){results.Add((pass?"PASS: ":"FAIL: ")+name);File.WriteAllText("Logs/MansionFeedbackRound/impact-audio.txt",string.Join("\n",results));}
        IEnumerator Start()
        {
            var round=RoundManager.Instance;var enemy=EnemyController.Enemies.First();var agent=enemy.GetComponent<NavMeshAgent>();
            enemy.enabled=false;agent.isStopped=true;yield return new WaitForSecondsRealtime(1);
            var origin=round.World.SpawnPosition+Vector3.forward;
            var far=round.World.Rooms.OrderByDescending(r=>Vector3.Distance(r.NavigationPosition,origin)).First().NavigationPosition;
            for(int test=0;test<3;test++)
            {
                var target=test==1?far:origin+Vector3.forward*2;
                if(NavMesh.SamplePosition(target,out var nav,2,NavMesh.AllAreas))agent.Warp(nav.position);
                enemy.State.Value=EnemyState.Idle;
                var item=Instantiate(round.settings.flashlight,origin+Vector3.up*1.5f,Quaternion.identity);
                var sound=item.GetComponent<ImpactNoiseEmitter>();
                var clip=sound.impactSounds.FirstOrDefault(c=>c);
                sound.impactSounds=test==2?System.Array.Empty<AudioClip>():new[]{clip,Resources.Load<AudioClip>("Flashlight/switch_002")};
                item.GetComponent<Unity.Netcode.NetworkObject>().Spawn();heard.Clear();bool audible=false;
                float until=Time.realtimeSinceStartup+2;
                while(Time.realtimeSinceStartup<until){audible|=item.GetComponents<AudioSource>().Any(a=>a.isPlaying);yield return null;}
                var impacts=heard.Where(n=>n.Category==NoiseCategory.Impact && n.Source==item.gameObject).ToArray();
                Check(impacts.Length>0,"Actual collision emits gameplay noise (case "+test+")");
                if(test==0){Check(audible,"Configured random impact clip actually plays on a spatial AudioSource");Check(enemy.State.Value==EnemyState.Investigate,"Nearby enemy investigates the dropped item collision");}
                if(test==1)Check(Vector3.Distance(enemy.transform.position,origin)>sound.maximumRadius && enemy.State.Value==EnemyState.Idle,"Enemy outside impact hearing range remains idle");
                if(test==2)Check(!audible && enemy.State.Value==EnemyState.Investigate,"Empty clip array stays silent while the actual collision still alerts the enemy");
                item.NetworkObject.Despawn();yield return null;
            }
            Check(true,"Impact audio validation complete");
        }
    }
}
#endif
