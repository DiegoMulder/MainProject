using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
namespace SurvivalFP.Editor
{
    public static class MansionGeometryValidation
    {
        public static void Run()
        {
            var results=new List<string>();void Check(bool pass,string name)=>results.Add((pass?"PASS: ":"FAIL: ")+name);
            var stairs=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Stairs/Staircase.prefab"),new Vector3(1000,0,1000),Quaternion.identity);
            var player=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AI-Tools-DEV/Prefabs/SurvivalPlayer.prefab"));player.GetComponent<PlayerController>().enabled=false;
            var motor=player.GetComponent<PlayerMovement>();const float dt=1f/60;
            void Set(Vector3 local){motor.Teleport(stairs.transform.position+local);Physics.SyncTransforms();for(int i=0;i<20;i++)motor.Tick(default,dt);}
            try
            {
                Set(new Vector3(0,.1f,-6));int air=0,steps=0;
                while(player.transform.position.z<1006 && steps++<400){motor.Tick(new PlayerCommand{Move=Vector2.up},dt);if(!motor.IsGrounded)air++;}
                Check(player.transform.position.y>3.85f && steps<400,"Original motor climbs the generated staircase");
                Check(air<steps*.1f,"Stair ascent stays grounded on at least 90% of ticks");
                player.transform.rotation=Quaternion.Euler(0,180,0);steps=0;air=0;
                while(player.transform.position.z>994 && steps++<400){motor.Tick(new PlayerCommand{Move=Vector2.up},dt);if(!motor.IsGrounded)air++;}
                Check(player.transform.position.y<.2f && steps<400,"Original motor descends the generated staircase");
                Check(air<steps*.1f,"Stair descent stays grounded on at least 90% of ticks");
                // Isolated ledge avoids the staircase's enclosing walls.
                var ledge=GameObject.CreatePrimitive(PrimitiveType.Cube);ledge.transform.position=new Vector3(1020,-.25f,1000);ledge.transform.localScale=new Vector3(4,.5f,4);
                motor.Teleport(new Vector3(1022.2f,.02f,1000));Physics.SyncTransforms();for(int i=0;i<10;i++)motor.Tick(default,dt);
                Check(motor.IsGrounded,"Partial capsule support at a ledge counts as grounded");
                motor.Tick(new PlayerCommand{JumpPressed=true},dt);Check(motor.VerticalSpeed>0 && !motor.IsGrounded,"Supported ledge permits a normal jump");
                motor.Teleport(new Vector3(1023,1,1000));Physics.SyncTransforms();motor.Tick(default,dt);Check(!motor.IsGrounded,"Unsupported player is not grounded by distant probes");
                foreach(float angle in new[]{0f,22.5f,45f,67.5f})
                {
                    ledge.transform.rotation=Quaternion.Euler(0,angle,0);
                    motor.Teleport(ledge.transform.TransformPoint(new Vector3(.565f,.54f,0)));
                    Physics.SyncTransforms();bool stable=true;
                    for(int i=0;i<180;i++){motor.Tick(default,dt);stable&=motor.IsGrounded;}
                    Check(stable,"Three seconds of partial edge support at "+angle+" degrees");
                }
                ledge.transform.rotation=Quaternion.identity;
                motor.Teleport(new Vector3(1022.35f,.02f,1000));Physics.SyncTransforms();motor.Tick(default,dt);
                Check(!motor.IsGrounded,"Flat footprint stops supporting beyond its outer edge");
                Object.DestroyImmediate(ledge);
                var table=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Props/Table.prefab"),new Vector3(1000,0,994),Quaternion.identity);
                Check(table.transform.Find("Tabletop").position.y<.9f,"Ordinary table is no longer elevated for hiding");
                Check(!table.GetComponentInChildren<ClosetHideout>(),"Ordinary table has no intentional hiding interaction");Object.DestroyImmediate(table);
            }
            finally{Object.DestroyImmediate(player);Object.DestroyImmediate(stairs);File.WriteAllText("Logs/Mansion-feedback-geometry.txt",string.Join("\n",results));}
        }
    }
}
