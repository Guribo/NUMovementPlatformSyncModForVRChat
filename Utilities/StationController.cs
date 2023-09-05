#define enableSmoothTimeControl
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace NUMovementPlatformSyncMod
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class StationController : UdonSharpBehaviour
    {
        public const int ExecutionOrder = 0;
        [SerializeField] VRCStation linkedStation;

        [UdonSynced] public int attachedTransformIndex = -1;
        [UdonSynced] Vector3 syncedLocalPlayerPosition = Vector3.zero;
        [UdonSynced] float syncedLocalPlayerHeading = 0;

        public Transform GroundTransform { set; private get; }

        Transform[] movingTransforms;

        int previouslyAttachedTransformIndex = -1;

        Vector3 previousPlayerPosition;
        Vector3 previousPlayerLinearVelocity;
        float previousPlayerAngularVelocity;

        //CyanPlayerObjectPool stuff
        public VRCPlayerApi Owner;

        NUMovementSyncMod NUMovementSyncModLink;

        bool setupComplete = false;

        bool inStation = false;

        float smoothTime = 0.068f;

        readonly float timeBetweenSerializations = 1f / 6f;
        float nextSerializationTime = 0f;
        public bool serialize = false;

        public override void PostLateUpdate()
        {
            var ownTransform = transform;
            if (Input.GetKeyDown(KeyCode.Home))
            {
                Debug.Log($" ");
                Debug.Log($"Debug of {nameof(StationController)}");
                Debug.Log($"{nameof(attachedTransformIndex)} = {attachedTransformIndex}");
                Debug.Log($"{nameof(syncedLocalPlayerPosition)} = {syncedLocalPlayerPosition}");
                Debug.Log($"transform.localPosition = {ownTransform.localPosition}");
                Debug.Log($"{nameof(syncedLocalPlayerHeading)} = {syncedLocalPlayerHeading}");
                Debug.Log($"transform.localRotation.eulerAngles = {ownTransform.localRotation.eulerAngles}");
                Debug.Log($"{nameof(GroundTransform)} = {GroundTransform}");
                Debug.Log($"{nameof(movingTransforms)}.Length = {movingTransforms.Length}");
                Debug.Log($"{nameof(previouslyAttachedTransformIndex)} = {previouslyAttachedTransformIndex}");
                Debug.Log($"{nameof(Owner)}.isLocal = {Owner.isLocal}");
                if (linkedStation) Debug.Log($"{nameof(linkedStation)}.PlayerMobility = {linkedStation.PlayerMobility}");
                Debug.Log($"{nameof(NUMovementSyncModLink)} = {NUMovementSyncModLink}");
                Debug.Log($"{nameof(setupComplete)} = {setupComplete}");
                //Debug.Log($"{nameof()} = {}");
            }

#if enableSmoothTimeControl
            if (Input.GetKeyDown(KeyCode.KeypadPlus))
            {
                smoothTime *= 1.1f;
                Debug.Log($"{nameof(smoothTime)} now set to {smoothTime}");
            }

            if (Input.GetKeyDown(KeyCode.KeypadMinus))
            {
                smoothTime /= 1.1f;
                Debug.Log($"{nameof(smoothTime)} now set to {smoothTime}");
            }
#endif

            if (inStation)
            {
                ownTransform.localPosition = Vector3.SmoothDamp(
                    ownTransform.localPosition,
                    syncedLocalPlayerPosition,
                    ref previousPlayerLinearVelocity,
                    smoothTime,
                    Mathf.Infinity,
                    Time.deltaTime
                );
                ownTransform.localRotation = Quaternion.Euler(
                    0,
                    Mathf.SmoothDampAngle(
                        ownTransform.localRotation.eulerAngles.y,
                        syncedLocalPlayerHeading,
                        ref previousPlayerAngularVelocity,
                        smoothTime,
                        Mathf.Infinity,
                        Time.deltaTime
                    ),
                    0
                );

                // ensure player is always level with horizon
                ownTransform.rotation = Quaternion.Euler(0, ownTransform.rotation.y, 0);
            }

            if (serialize && Time.timeSinceLevelLoad > nextSerializationTime)
            {
                serialize = false;
                RequestSerialization();
            }
        }

        public void _OnOwnerSet()
        {
            if (!transform.parent)
            {
                return;
            }

            movementModLinker linker = transform.parent.GetComponent<movementModLinker>();
            if (!Utilities.IsValid(linker))
            {
                Debug.LogError($"{nameof(movementModLinker)} missing on {transform.parent.name}");
                return;
            }

            NUMovementSyncModLink = linker.LinkedMovementMod;

            movingTransforms = NUMovementSyncModLink.MovingTransforms;

            if (Owner.isLocal)
            {
                linkedStation.PlayerMobility = VRCStation.Mobility.Mobile;

                NUMovementSyncModLink.AttachStation(this, linkedStation);
            }
            else
            {
                //linkedStation.PlayerMobility = VRCStation.Mobility.Immobilize;
            }

            setupComplete = true;
        }

        public void _OnCleanup()
        {
            linkedStation.PlayerMobility = VRCStation.Mobility.ImmobilizeForVehicle;
        }

        //VRChat functions
        public override void OnPreSerialization()
        {
            nextSerializationTime = Time.timeSinceLevelLoad + timeBetweenSerializations;
            if (!setupComplete || attachedTransformIndex == -1)
            {
                return;
            }

            var playerOrigin = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
            var playerRotationOnGround = Quaternion.Inverse(GroundTransform.rotation) * playerOrigin.rotation;

            syncedLocalPlayerPosition = GroundTransform.InverseTransformPoint(playerOrigin.position);
            syncedLocalPlayerHeading = playerRotationOnGround.eulerAngles.y;
        }

        public override void OnDeserialization()
        {
            if (!setupComplete || previouslyAttachedTransformIndex == attachedTransformIndex)
            {
                return;
            }

            previouslyAttachedTransformIndex = attachedTransformIndex;
            if (attachedTransformIndex == -1)
            {
                return;
            }

            linkedStation.transform.parent = movingTransforms[attachedTransformIndex];
        }

        public override void OnStationEntered(VRCPlayerApi player)
        {
            if (player.isLocal) return;

            previousPlayerLinearVelocity = Vector3.zero;
            previousPlayerAngularVelocity = 0;
            transform.SetPositionAndRotation(player.GetPosition(), player.GetRotation());
            linkedStation.PlayerMobility = VRCStation.Mobility.ImmobilizeForVehicle;

            inStation = true;
        }

        public override void OnStationExited(VRCPlayerApi player)
        {
            if (player.isLocal) return;

            linkedStation.PlayerMobility = VRCStation.Mobility.Mobile;
            inStation = false;
        }
    }
}