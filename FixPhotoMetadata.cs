using System;
using System.Collections.Generic;
using System.Xml.Linq;
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
        public override string Version => "1.0.1";
        public override string Link => "https://github.com/BlueberryWolf/FixPhotoMetadata";

        [AutoRegisterConfigKey]
        public static readonly ModConfigurationKey<bool> InjectOcclusionKey =
            new ModConfigurationKey<bool>("InjectOcclusion", "Inject occlusion/in-view data into screenshot metadata", () => true);

        private static ModConfiguration? _config;

        public struct CapturedMetadata
        {
            public float3 Position;
            public floatQ Rotation;
            public float FOV;
            public float Aspect;

            public CapturedMetadata(float3 position, floatQ rotation, float fov, float aspect)
            {
                Position = position;
                Rotation = rotation;
                FOV = fov;
                Aspect = aspect;
            }
        }

        public static readonly Queue<CapturedMetadata> PendingCaptures = new Queue<CapturedMetadata>();
        public static readonly HashSet<string> OccludedUserIds = new HashSet<string>();

        public override void OnEngineInit()
        {
            _config = GetConfiguration();
            Harmony harmony = new Harmony("com.github.BlueberryWolf.FixPhotoMetadata");
            harmony.PatchAll();

            // try to hook metadata serializer from ResoniteScreenshotExtensions
            // ie: ScreenshotExtensionsExtensions LOL
            try
            {
                var type = Type.GetType("ResoniteScreenshotExtensions.XmpMetadata, ResoniteScreenshotExtensions");
                if (type == null)
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        var t = asm.GetType("ResoniteScreenshotExtensions.XmpMetadata");
                        if (t != null)
                        {
                            type = t;
                            break;
                        }
                    }
                }

                if (type != null)
                {
                    var targetMethod = type.GetMethod("SerializeMetadataUserInfo", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    var postfixMethod = typeof(XmpMetadata_SerializeMetadataUserInfo_Patch).GetMethod("Postfix", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    if (targetMethod != null && postfixMethod != null)
                    {
                        harmony.Patch(targetMethod, postfix: new HarmonyMethod(postfixMethod));
                        Msg("Successfully patched ResoniteScreenshotExtensions to inject occlusion/in-view metadata!");
                    }
                }
                else
                {
                    Msg("ResoniteScreenshotExtensions not found. Skipping optional XMP metadata injection patch.");
                }
            }
            catch (Exception e)
            {
                Error("Failed to patch ResoniteScreenshotExtensions: " + e);
            }
        }

        [HarmonyPatch(typeof(PhotoCaptureManager), "TakePhoto", new Type[] { typeof(Slot), typeof(int2), typeof(bool) })]
        class PhotoCaptureManager_TakePhoto_Patch
        {
            [HarmonyPostfix]
            static void Postfix(PhotoCaptureManager __instance, int2 resolution)
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
                    float aspect = (resolution.y > 0) ? ((float)resolution.x / resolution.y) : (16f / 9f);
                    lock (PendingCaptures)
                    {
                        PendingCaptures.Enqueue(new CapturedMetadata(globalPos, globalRot, fov, aspect));
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
            static void Postfix(InteractiveCamera __instance, InteractiveCamera.Mode mode, int2 resolution, bool __result)
            {
                if (!__result) return;

                try
                {
                    var mainCamera = __instance.MainCamera.Target;
                    if (mainCamera == null) return;

                    float3 globalPos = mainCamera.Slot.GlobalPosition;
                    floatQ globalRot = mainCamera.Slot.GlobalRotation;
                    float fov = (mode == InteractiveCamera.Mode.Camera360) ? 360f : mainCamera.FieldOfView.Value;
                    float aspect = (resolution.y > 0) ? ((float)resolution.x / resolution.y) : (16f / 9f);

                    lock (PendingCaptures)
                    {
                        PendingCaptures.Enqueue(new CapturedMetadata(globalPos, globalRot, fov, aspect));
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
                    lock (OccludedUserIds)
                    {
                        OccludedUserIds.Clear();
                    }

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

                    float aspect = metadata.Aspect;
                    float fovRad = metadata.FOV * MathF.PI / 180f;
                    float tanHalfFov = MathF.Tan(fovRad / 2f);
                    floatQ qInv = new floatQ(-metadata.Rotation.x, -metadata.Rotation.y, -metadata.Rotation.z, metadata.Rotation.w);

                    var tagsSlot = __instance.Slot.FindChild("PhotoMetadata_Tags");
                    if (tagsSlot == null)
                    {
                        tagsSlot = __instance.Slot.AddSlot("PhotoMetadata_Tags", true);
                        var space = tagsSlot.AttachComponent<DynamicVariableSpace>();
                        space.SpaceName.Value = "PhotoMetadata";
                    }
                    tagsSlot.PersistentSelf = true;

                    for (int i = 0; i < __instance.UserInfos.Count; i++)
                    {
                        var userInfo = __instance.UserInfos[i];
                        if (userInfo.User.Target == null) continue;
                        var userId = userInfo.User.Target.UserID;
                        if (userId == null) continue;

                        float3 headPos = userInfo.HeadPosition.Value;
                        float3 relativePos = headPos - metadata.Position;
                        float3 localPos = qInv * relativePos;

                        bool visible = true;

                        if (localPos.z <= 0.05f)
                        {
                            visible = false;
                        }
                        else
                        {
                            float verticalLimit = localPos.z * tanHalfFov;
                            float horizontalLimit = verticalLimit * aspect;

                            if (MathF.Abs(localPos.x) > horizontalLimit * 1.2f || MathF.Abs(localPos.y) > verticalLimit * 1.2f)
                            {
                                visible = false;
                            }
                            else
                            {
                                float3 dir = relativePos.Normalized;
                                float dist = relativePos.Magnitude;
                                if (dist > 0.3f)
                                {
                                    var hit = __instance.World.Physics.RaycastOne(metadata.Position, dir, dist, c => {
                                        // skip triggers & no-collision slots
                                        var col = c as Collider;
                                        if (col != null)
                                        {
                                            var type = col.Type.Value;
                                            if (type == ColliderType.Trigger || type == ColliderType.StaticTrigger || type == ColliderType.NoCollision)
                                                return false;
                                        }

                                        // skip target's own avatar
                                        if (userInfo.User.Target != null && userInfo.User.Target.Root != null && c.Slot.IsChildOf(userInfo.User.Target.Root.Slot))
                                            return false;

                                        // skip the camera itself
                                        if (c.Slot.IsChildOf(__instance.Slot))
                                            return false;

                                        return true;
                                    });

                                    if (hit.HasValue)
                                    {
                                        visible = false;
                                    }
                                }
                            }
                        }

                        if (!visible)
                        {
                            lock (OccludedUserIds)
                            {
                                OccludedUserIds.Add(userId);
                            }
                        }

                        var userSlot = tagsSlot.FindChild(userId);
                        if (userSlot == null)
                        {
                            userSlot = tagsSlot.AddSlot(userId, true);
                        }
                        userSlot.PersistentSelf = true;

                        float userScale = 1.0f;
                        if (userInfo.User.Target.Root != null)
                        {
                            userScale = userInfo.User.Target.Root.Slot.GlobalScale.x;
                        }

                        var scaleVar = userSlot.AttachComponent<DynamicValueVariable<float>>();
                        scaleVar.VariableName.Value = "PhotoMetadata/" + userId + "/headScale";
                        scaleVar.Value.Value = userScale;

                        var viewVar = userSlot.AttachComponent<DynamicValueVariable<bool>>();
                        viewVar.VariableName.Value = "PhotoMetadata/" + userId + "/isInView";
                        viewVar.Value.Value = visible;
                    }
                }
                catch (Exception e)
                {
                    Error("Error in AssetMetadata.SetFromCurrentWorld postfix: " + e);
                }
            }
        }

        class XmpMetadata_SerializeMetadataUserInfo_Patch
        {
            internal static void Postfix(XElement element, object userInfo)
            {
                if (!(_config?.GetValue(InjectOcclusionKey) ?? true)) return;

                try
                {
                    var userProp = userInfo.GetType().GetProperty("User");
                    if (userProp == null) return;
                    var userObj = userProp.GetValue(userInfo);
                    if (userObj == null) return;
                    var idProp = userObj.GetType().GetProperty("Id");
                    if (idProp == null) return;
                    var userId = idProp.GetValue(userObj) as string;
                    if (userId == null) return;

                    bool isOccluded = false;
                    lock (OccludedUserIds)
                    {
                        isOccluded = OccludedUserIds.Contains(userId);
                    }

                    var ns = XNamespace.Get("http://ns.baru.dev/resonite-ss-ext/2.0/");
                    var scaleProp = userInfo.GetType().GetProperty("UserScale");
                    float userScale = 1.0f;
                    if (scaleProp != null)
                    {
                        var scaleVal = scaleProp.GetValue(userInfo);
                        if (scaleVal is float f)
                        {
                            userScale = f;
                        }
                    }

                    element.SetAttributeValue(ns + "UI-HeadScale", userScale.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    element.SetAttributeValue(ns + "UI-IsInView", (!isOccluded).ToString().ToLower());
                }
                catch
                {
                    // ignore
                }
            }
        }
    }
}
