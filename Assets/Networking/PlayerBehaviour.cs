﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UdpKit;

public class PlayerBehaviour : NetworkEntity {

    public Rigidbody rb;
    public Vector3 targetPos;
    public Quaternion targetRotation;
    public float moveSpeed = 1f;

    public void Awake() {
        rb = this.GetComponent<Rigidbody>();
    }

    public override void Update() {
        base.Update();


        if(isPredictingControl) {
            SetColor(Color.green);
        } else {
            if(controller == 0) {
                SetColor(Color.red);
            } else {
                SetColor(Color.blue);
            }
        }

        //if we don't care about this object anymore, because we're too far away and have stopped getting state updates for it
        //just destroy it now.
        //what if we're sitting on the threshold, will we get spawn/despawn/spawn/despawn?
        //if(!isOwner() && Priority(Core.net.me.steamID) <= 0f) {
        //    Hide();
        //    DestroyInternal(); 
        //}

        if(!hasControl()) {//  /should this be controller? it should be controller

            //the issue is we shoudl be storing this every frame, if we're not receiving updates
            //so that if we ever lose control we will still have something to interp to
            //not sure where to inject that though. In lateupdate?
            //update the position with the interpolated version (using our interp time
            this.transform.position = GetInterpolatedPosition(0); //we should come up with a more modular way to store these
                                                                  //what if we want more than one position per state (for whatever reason)
            this.transform.rotation = GetInterpolatedRotation(0); //or we want to interpolate a float (for colour or something) idk



        } else {
            //do control
            Vector3 mov = new Vector3();
            if(Input.GetKey(KeyCode.W)) {
                mov.z = 1f;
            } else if(Input.GetKey(KeyCode.S)) {
                mov.z = -1f;
            }

            if(Input.GetKey(KeyCode.D)) {
                mov.x = 1f;
            } else if(Input.GetKey(KeyCode.A)) {
                mov.x = -1f;
            }

            this.transform.position += mov * moveSpeed * Time.deltaTime;
        }
    }

    //


    public void SetColor(Color c) {
        this.GetComponent<Renderer>().material.color = c;
    }

    public override void OnSpawn(params object[] args) {
        base.Update();
        //if you own it, subscribe to the NetworkSendEvent
        if(args.Length != 0) {
            //are we spawing because it entered scope? if so, we have a state
            //otherwise we do not
            this.transform.position = new Vector3((float)args[0], (float)args[1], (float)args[2]);
            this.transform.rotation = (Quaternion)args[3];
        } else {
            //normal spawn, no state yet?
        }

        //always 
        Core.net.NetworkSendEvent += OnNetworkSend;

    }

    public override void OnChangeOwner(int newOwner) {
        base.OnChangeOwner(newOwner);
    }

    //triggered right before a packet is going out.  This is where you want to
    //queue the state update message
    public override void OnNetworkSend() {
        if(isController()) {
            float p = PriorityCaller(Core.net.me.steamID, true);
            if(p <= 0f) {
                DestroyInternal();
            } else {
                QueueEntityUpdate();
            }
        }
    }

    public override void OnEntityUpdate(params object[] args) {
        if(!shouldReplicate()) return; //don't want to apply anything if we're frozen (eg, dead or predicted dead)

        StorePositionSnapshot(0, (float)args[0], (float)args[1], (float)args[2]);
        StoreRotationSnapshot(0, (Quaternion)args[3]);
        //StoreIntSnapshot(0, (int)args[4]);
        //StoreFloatSnapshot(0, (float)args[5]);
        //if we had another float we wanted to interpolate
        //StoreFloatSnapshot(1, (float)args[6]); //then call it with GetInterpolatedFloat(1);
        //}
    }

    public override void LateUpdate() {
        base.LateUpdate();
        //we need to store these even if we are the controller, but we don't ever use the stored values
        //Only in the case where we pass control to someone else, this allows us to interpolate from
        //our last known position instead of snapping to 0 before our first state update from our new owner.
        //This just makes the transition between controllers smoother
        if(hasControl()) { //we need to store these 
            StorePositionSnapshot(0, transform.position.x, transform.position.y, transform.position.z);
            StoreRotationSnapshot(0, transform.rotation);
        }
    }

    public override int Peek() {
        int s = 0;
        s += SerializerUtils.RequiredBitsFloat(-100f, 100f, 0.0001f);
        s += SerializerUtils.RequiredBitsFloat(-100f, 100f, 0.0001f);
        s += SerializerUtils.RequiredBitsFloat(-100f, 100f, 0.0001f);
        s += SerializerUtils.RequiredBitsQuaternion(0.001f);
        return s;
    }

    //this is a helper, this is called in the network look to get the priority for this entity
    //override this here, otherwise it gets called with no args.
    public override float PriorityCaller(ulong steamId, bool isSending = true, params object[] args) {
        if(ignoreZones) {
            return 1f;
        } else {
            if(Core.net.me.inSameZone(Core.net.GetConnection(steamId))) {
                if(isSending) {
                    return Priority(steamId, this.transform.position.x, this.transform.position.y, this.transform.position.z);
                } else {
                    float x = (float)args[0];
                    float y = (float)args[1];
                    float z = (float)args[2];

                    return Priority(Core.net.me.steamID, x, y, z);
                }
            } else {
                return 0f;//not in the same zone, 
            }
        }
    }

    //we should do a priority check while sending to remove messages to people who don't want it
    //we should also do a priority check when receving to filter out messages we don't want that might have been sent
    //eg. we change scenes, still get events while the sender realizes we changed scenes.  We should ignore those events
    public override float Priority(ulong sendTo, params object[] args) {

        float x = (float)args[0];
        float y = (float)args[1];
        float z = (float)args[2];

        float r = 1f;
        //in the same zone, send it
        float d = Vector3.Distance(new Vector3(x, y, z), Core.net.GetConnection(sendTo).lastPosition);
        //scale linearly at 25m, then anything larger set to 0
        float radius = 25f;

        r = Mathf.Clamp(radius - d, 0f, radius);


        //Debug.Log("Entity.Priority: " + r);
        //could check the connections[sendTo], get their player position and find out the distance between them
        //and this object.  And scale priority based on that, so it's lower the further away they are
        //requires some player metatdata to be accessed from *somewhere* though...
        return r;        //return 0f;
    }



    //bolt 3 float properties compressed the same (18 bits each = 54 bits)
    //20 packets per second, means 1080 bits or 135 bytes per second or 0.135 bytes per second

    public override void Serialize(UdpStream stream) {
        SerializerUtils.WriteFloat(stream, this.transform.position.x, -100f, 100f, 0.0001f);
        SerializerUtils.WriteFloat(stream, this.transform.position.y, -100f, 100f, 0.0001f);
        SerializerUtils.WriteFloat(stream, this.transform.position.z, -100f, 100f, 0.0001f);
        SerializerUtils.WriteQuaterinion(stream, this.transform.rotation, 0.001f);
    }

    public override void Deserialize(UdpStream stream, int prefabId, int networkId, int owner, int controller) {
        //deserialize any state data.

        float x = SerializerUtils.ReadFloat(stream, -100f, 100f, 0.0001f);
        float y = SerializerUtils.ReadFloat(stream, -100f, 100f, 0.0001f);
        float z = SerializerUtils.ReadFloat(stream, -100f, 100f, 0.0001f);
        Quaternion rotation = SerializerUtils.ReadQuaternion(stream, 0.001f);

        //we have to do this after the deseralize, otherwise the data will be corrupted
        //we can't do this here because this call to priority would be on the prefab, not the instance because
        //this is on the receiver. and we usually want to use this.transform.position in the priority check
        //and it would always be 0,0,0!

        //at this point, we don't know if this entity is even instantiated in the world
        //we don't do that check until inside of ProcessEntityMessage, but in some cases we don't
        //want to process it because then it will get instantiated when we don't want it to (eg, in a different scene)
        //so we need to make some compromises with how the priority system will work for entities
        //eg, we can only use prefab values as we don't have an entity instance to work with
        //but we can push in any extra data if we really need to, we just need to modify it
        //we could pass in args, too..
        if(PriorityCaller(Core.net.GetConnection(controller).steamID, false, x, y, z) > 0f) { //make sure we're getting an update we care about (same zone, etc)
            Core.net.ProcessEntityMessage(prefabId, networkId, owner, controller, x, y, z, rotation);
        }


    }

    public void OnDestroy() {
        Core.net.NetworkSendEvent -= OnNetworkSend;
    }
}
