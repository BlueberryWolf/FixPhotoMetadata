using System;
using System.Collections.Generic;
using HarmonyLib;
using ResoniteModLoader;
using FrooxEngine;
using Elements.Core;

namespace FixPhotoMetadata
{
    public class FixPhotoMetadata : ResoniteMod
    {
        public override string Name => "FixPhotoMetadata";
        public override string Author => "BlueberryWolf";
        public override string Version => "1.0.0";
        public override string Link => "https://github.com/BlueberryWolf/FixPhotoMetadata";

        public struct CapturedMetadata
        {
            public float3 Position;
            public floatQ Rotation;
            public float FOV;

            public CapturedMetadata(float3 position, floatQ rotation, float fov)
            {
                Position = position;
                Rotation = rotation;
                FOV = fov;
            }
        }

        public static readonly Queue<CapturedMetadata> PendingCaptures = new Queue<CapturedMetadata>();

        public override void OnEngineInit()
        {
            Harmony harmony = new Harmony("com.github.BlueberryWolf.FixPhotoMetadata");
            harmony.PatchAll();
        }

        [HarmonyPatch(typeof(PhotoCaptureManager), "TakePhoto", new Type[] { typeof(Slot), typeof(int2), typeof(bool) })]
        class PhotoCaptureManager_TakePhoto_Patch
        {
            [HarmonyPostfix]
            static void Postfix(PhotoCaptureManager __instance)
            {
                try
                {
                    var traverse = Traverse.Create(__instance);
                    var camera = traverse.Field("_camera").GetValue<SyncRef<Camera>>()?.Target;
                    if (camera == null) return;

                    var args = new object[] { float3.Zero, floatQ.Identity };
                    traverse.Method("GetCaptureCameraPosition", args).GetValue();
                    float3 pos = (float3)args[0];
                    floatQ rot = (floatQ)args[1];

                    Slot parent = camera.Slot.Parent;
                    float3 globalPos;
                    floatQ globalRot;
                    if (traverse.Field("CaptureStereo").GetValue<Sync<bool>>().Value)
                    {
                        float stereoSeparation = traverse.Field("StereoSeparation").GetValue<Sync<float>>().Value;
                        globalPos = parent.LocalPointToGlobal(pos + float3.Left * stereoSeparation * 0.5f);
                        globalRot = parent.LocalRotationToGlobal(rot);
                    }
                    else
                    {
                        globalPos = parent.LocalPointToGlobal(pos);
                        globalRot = parent.LocalRotationToGlobal(rot);
                    }

                    float fov = camera.FieldOfView.Value;
                    lock (PendingCaptures)
                    {
                        PendingCaptures.Enqueue(new CapturedMetadata(globalPos, globalRot, fov));
                    }
                }
                catch (Exception e)
                {
                    Error("Error in PhotoCaptureManager.TakePhoto postfix: " + e);
                }
            }
        }

        [HarmonyPatch(typeof(InteractiveCamera), nameof(InteractiveCamera.Capture), new Type[] { typeof(InteractiveCamera.Mode), typeof(int2), typeof(bool) })]
        class InteractiveCamera_Capture_Patch
        {
            [HarmonyPostfix]
            static void Postfix(InteractiveCamera __instance, InteractiveCamera.Mode mode, bool __result)
            {
                if (!__result) return;

                try
                {
                    var mainCamera = __instance.MainCamera.Target;
                    if (mainCamera == null) return;

                    float3 globalPos = mainCamera.Slot.GlobalPosition;
                    floatQ globalRot = mainCamera.Slot.GlobalRotation;
                    float fov = (mode == InteractiveCamera.Mode.Camera360) ? 360f : mainCamera.FieldOfView.Value;

                    lock (PendingCaptures)
                    {
                        PendingCaptures.Enqueue(new CapturedMetadata(globalPos, globalRot, fov));
                    }
                }
                catch (Exception e)
                {
                    Error("Error enqueuing InteractiveCamera metadata: " + e);
                }
            }
        }

        [HarmonyPatch(typeof(AssetMetadata), nameof(AssetMetadata.SetFromCurrentWorld))]
        class AssetMetadata_SetFromCurrentWorld_Patch
        {
            [HarmonyPostfix]
            static void Postfix(AssetMetadata __instance)
            {
                try
                {
                    CapturedMetadata metadata;
                    lock (PendingCaptures)
                    {
                        if (PendingCaptures.Count == 0) return;
                        metadata = PendingCaptures.Dequeue();
                    }

                    __instance.TakenGlobalPosition.Value = metadata.Position;
                    __instance.TakenGlobalRotation.Value = metadata.Rotation;

                    if (__instance is PhotoMetadata photoMetadata)
                    {
                        photoMetadata.CameraFOV.Value = metadata.FOV;
                    }
                }
                catch (Exception e)
                {
                    Error("Error in AssetMetadata.SetFromCurrentWorld postfix: " + e);
                }
            }
        }
    }
}
