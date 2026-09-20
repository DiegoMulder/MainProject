using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
namespace SurvivalFP
{
    // Same motor on both peers: local prediction, server collision/stamina validation,
    // and replay of unacknowledged commands. Remote avatars retain NetworkTransform smoothing.
    public sealed class PlayerPrediction : NetworkBehaviour
    {
        const float Step=1f/60f;
        struct Frame {public MovementInput input;public MotorSnapshot state;}
        readonly List<Frame> history=new();
        NetworkPlayer player;float accumulator,credit,creditTime,lastReceived,nextSnapshot;
        int sequence,lastProcessed,epoch,lastAck;bool jump;
        Vector3 previousPosition,currentPosition;bool presentationReady;
        // Render between fixed prediction steps. Physics and authoritative corrections remain immediate.
        public Vector3 RenderOffset=>IsSpawned&&IsOwner&&!IsServer&&player.CanMove&&presentationReady
            ? Vector3.Lerp(previousPosition,currentPosition,Mathf.Clamp01(accumulator/Step))-transform.position:Vector3.zero;
        void ResetPresentation(){previousPosition=currentPosition=transform.position;presentationReady=true;}
        public int Corrections {get;private set;}
        public float LastCorrection {get;private set;}
        void Awake()=>player=GetComponent<NetworkPlayer>();
        public override void OnNetworkSpawn()
        {
            credit=.15f;creditTime=lastReceived=Time.unscaledTime;
            ResetPresentation();
            if(IsServer)player.Motor.Teleported+=ForceState;
        }
        public override void OnNetworkDespawn(){if(player && player.Motor)player.Motor.Teleported-=ForceState;history.Clear();}
        public void Predict(PlayerCommand command,float yaw,float pitch,float dt)
        {
            if(!IsOwner || IsServer || !player.CanMove)return;
            if(!presentationReady)ResetPresentation();
            jump|=command.JumpPressed;accumulator=Mathf.Min(accumulator+dt,.1f);
            bool stepped=false;
            while(accumulator>=Step)
            {
                accumulator-=Step;var input=new MovementInput {sequence=++sequence,epoch=epoch,move=command.Move,
                    yaw=yaw,pitch=pitch,sprint=command.Sprint,crouch=command.Crouch,jump=jump};jump=false;
                previousPosition=transform.position;
                player.Motor.Tick(input.Command,Step);currentPosition=transform.position;
                history.Add(new Frame {input=input,state=player.Motor.Capture()});stepped=true;
                if(history.Count>256)history.RemoveAt(0);
            }
            if(!stepped)return;
            int start=Mathf.Max(0,history.Count-6);var batch=new MovementInput[history.Count-start];
            for(int i=0;i<batch.Length;i++)batch[i]=history[start+i].input;
            InputRpc(batch);
        }
        [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Owner,Delivery=RpcDelivery.Unreliable)]
        void InputRpc(MovementInput[] batch)
        {
            if(IsOwner || batch==null || batch.Length>6 || !player.CanMove)return;
            float now=Time.unscaledTime;credit=Mathf.Min(.2f,credit+now-creditTime);creditTime=now;
            foreach(var input in batch)
            {
                if(input.epoch!=epoch || input.sequence<=lastProcessed || !float.IsFinite(input.move.sqrMagnitude)
                    || !float.IsFinite(input.yaw) || !float.IsFinite(input.pitch))continue;
                if(credit<Step)break;
                credit-=Step;lastProcessed=input.sequence;lastReceived=now;
                player.transform.rotation=Quaternion.Euler(0,input.yaw%360,0);
                player.SetNetworkLook(input.yaw,Mathf.Clamp(input.pitch,-85,85));
                player.Motor.Tick(input.Command,Step);
            }
            if(now>=nextSnapshot){nextSnapshot=now+.05f;StateRpc(lastProcessed,epoch,player.Motor.Capture(),false);}
        }
        void Update()
        {
            if(!IsSpawned || !IsServer || IsOwner || !player.CanMove || Time.unscaledTime-lastReceived<.3f)return;
            // Lost connection/input must not leave a character running or suspended in the air.
            player.Motor.Tick(default,Mathf.Min(Time.deltaTime,.05f));
            if(Time.unscaledTime>=nextSnapshot){nextSnapshot=Time.unscaledTime+.1f;StateRpc(lastProcessed,epoch,player.Motor.Capture(),false);}
        }
        public void ForceState()
        {
            if(!IsSpawned || !IsServer)return;
            epoch++;credit=.15f;creditTime=lastReceived=Time.unscaledTime;
            StateRpc(lastProcessed,epoch,player.Motor.Capture(),true);
        }
        [Rpc(SendTo.Owner,InvokePermission=RpcInvokePermission.Server)]
        void StateRpc(int acknowledged,int revision,MotorSnapshot state,bool force)
        {
            if(IsServer || revision<epoch || (!force && acknowledged<lastAck))return;
            if(force || revision!=epoch)
            {
                epoch=revision;lastAck=acknowledged;history.Clear();accumulator=0;jump=false;
                player.Motor.Restore(state);ResetPresentation();return;
            }
            lastAck=acknowledged;
            int index=history.FindIndex(f=>f.input.sequence==acknowledged);
            bool correct=index<0 || Vector3.Distance(history[index].state.position,state.position)>.035f
                || Mathf.Abs(history[index].state.vertical-state.vertical)>.15f
                || Mathf.Abs(history[index].state.stamina.current-state.stamina.current)>.1f
                || history[index].state.grounded!=state.grounded;
            history.RemoveAll(f=>f.input.sequence<=acknowledged);
            if(!correct)return;
            var before=transform.position;var look=transform.rotation;
            player.Motor.Replaying=true;
            try
            {
                player.Motor.Restore(state);
                if(player.CanMove)for(int i=0;i<history.Count;i++)
                {
                    var frame=history[i];transform.rotation=Quaternion.Euler(0,frame.input.yaw,0);
                    player.Motor.Tick(frame.input.Command,Step);frame.state=player.Motor.Capture();history[i]=frame;
                }
            }
            finally{player.Motor.Replaying=false;transform.rotation=look;}
            var correction=transform.position-before;previousPosition+=correction;currentPosition=transform.position;
            LastCorrection=Vector3.Distance(before,transform.position);if(LastCorrection>.035f)Corrections++;
        }
    }
}
