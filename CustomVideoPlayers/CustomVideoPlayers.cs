using HarmonyLib;
using NeosModLoader;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using FrooxEngine;
using FrooxEngine.LogiX;
using FrooxEngine.UIX;
using BaseX;
using System.Reflection.Emit;

namespace CustomVideoPlayers
{
    public class CustomVideoPlayers : NeosMod
    {
        public override string Name => "CustomVideoPlayers";
        public override string Author => "art0007i";
        public override string Version => "1.0.1";
        public override string Link => "https://github.com/art0007i/CustomVideoPlayers/";
        public override void OnEngineInit()
        {
            Harmony harmony = new Harmony("me.art0007i.CustomVideoPlayers");
            harmony.PatchAll();
        }

        // Constant defintions, including: an enum with a value that is normally impossible, a tag that gets added to all saved video players and a cloud variable that stores player urls
        public static InventoryBrowser.SpecialItemType VIDEO_PLAYER_SPECIAL_ITEM { get { return (InventoryBrowser.SpecialItemType)6; } }
        public static string VIDEO_PLAYER_TAG { get { return "custom_video_player"; } } 
        public static string VIDEO_PLAYER_VARIABLE { get { return "U-art0007i.video_player.current"; } }

        // Video player variable, it's a property that updates your cloud variable when you change it
        private static Uri _VideoPlayerUrl;
        public static Uri VideoPlayerUrl { get
            {
                return _VideoPlayerUrl;
            }
            set
            {
                if(_VideoPlayerUrl != value)
                {
                    if(Engine.Current.Cloud.CurrentUser == null && value != null)
                    {
                        throw new InvalidOperationException("Cannot set video player URL without being signed in");
                    }
                    _VideoPlayerUrl = value;
                    if(Engine.Current.Cloud.CurrentUser != null)
                    {
                        Engine.Current.Cloud.WriteVariable(VIDEO_PLAYER_VARIABLE, value);
                    }

                    AccessTools.Method(typeof(ProfileManager), "SafeInvoke").Invoke(Engine.Current.Cloud.Profile, new object[] { VideoPlayerChanged, value });
                }
            }
        }

        public static event Action<Uri> VideoPlayerChanged;

        // This loads your video player url from the cloud
        [HarmonyPatch(typeof(ProfileManager), "SignIn")]
        class ProfileManager_SignIn_Patch
        {
            public static async void Prefix(ProfileManager __instance)
            {
                var videoResult = await __instance.Cloud.ReadVariable<Uri>(VIDEO_PLAYER_VARIABLE);
                if (videoResult.IsOK)
                {
                    VideoPlayerUrl = videoResult.Entity;
                }
            }
        }

        // This makes it so whenever you change the player url the pink background updates
        [HarmonyPatch(typeof(InventoryBrowser))]
        class InventoryBrowser_VideoChangeEvents_Patch
        {
            private static Dictionary<InventoryBrowser, Action<Uri>> ActiveVideoEvents = new Dictionary<InventoryBrowser, Action<Uri>>();

            [HarmonyPostfix]
            [HarmonyPatch("OnAwake")]
            public static void PostAwake(InventoryBrowser __instance)
            {
                ActiveVideoEvents.Add(__instance, (uri) =>
                {
                    if (__instance.CanInteract(__instance.LocalUser))
                    {
                        __instance.RunSynchronously(() =>
                        {
                            AccessTools.Method(typeof(InventoryBrowser), "ReprocessItems").Invoke(__instance, null);
                        });
                    }
                });
                VideoPlayerChanged += ActiveVideoEvents[__instance];

            }
            [HarmonyPostfix]
            [HarmonyPatch("OnDispose")]
            public static void PostDispose(InventoryBrowser __instance)
            {
                var del = ActiveVideoEvents[__instance];
                ActiveVideoEvents.Remove(__instance);
                VideoPlayerChanged -= del;
            }

            [HarmonyPostfix]
            [HarmonyPatch("ProcessItem")]
            public static void PostProcess(InventoryItemUI item)
            {
                Record record = (Record)AccessTools.Field(item.GetType(), "Item").GetValue(item);
                Uri uri = record?.URL;
                InventoryBrowser.SpecialItemType specialItemType = InventoryBrowser.ClassifyItem(item);
                if (uri != null && specialItemType == VIDEO_PLAYER_SPECIAL_ITEM && uri == VideoPlayerUrl)
                {
                    item.NormalColor.Value = InventoryBrowser.ACTIVE_AVATAR_COLOR;
                    item.SelectedColor.Value = InventoryBrowser.ACTIVE_AVATAR_COLOR.MulA(2f);
                    return;
                }
            }
        }

        // This allows identifying which items in the inventory are video players
        [HarmonyPatch(typeof(InventoryBrowser), "ClassifyItem")]
        class InventoryBrowser_ClassifyItem_Patch
        {
            public static void Postfix(InventoryItemUI itemui, ref InventoryBrowser.SpecialItemType __result)
            {
                if (itemui != null)
                {
                    Record record = (Record)AccessTools.Field(itemui.GetType(), "Item").GetValue(itemui);
                    if(record != null && record.Tags != null)
                    {
                        if (record.Tags.Contains(VIDEO_PLAYER_TAG))
                        {
                            __result = VIDEO_PLAYER_SPECIAL_ITEM;
                        }
                    }
                }
            }
        }

        // This adds the purple heart button to video players
        [HarmonyPatch(typeof(InventoryBrowser), "OnItemSelected")]
        class InventoryBrowser_OnItemSelected_Patch
        {
            public static void Prefix(InventoryBrowser __instance, out InventoryBrowser.SpecialItemType __state)
            {
                __state = (AccessTools.Field(typeof(InventoryBrowser), "_lastSpecialItemType").GetValue(__instance) as Sync<InventoryBrowser.SpecialItemType>).Value;
            }

            public static void Postfix(InventoryBrowser __instance, BrowserItem currentItem, InventoryBrowser.SpecialItemType __state)
            {
                InventoryItemUI inventoryItemUI = currentItem as InventoryItemUI;
                InventoryBrowser.SpecialItemType specialItemType = InventoryBrowser.ClassifyItem(inventoryItemUI);
                var buttonsRoot = (AccessTools.Field(typeof(InventoryBrowser), "_buttonsRoot").GetValue(__instance) as SyncRef<Slot>).Target[0];
                if (__state == specialItemType) return;
                if (specialItemType == VIDEO_PLAYER_SPECIAL_ITEM)
                {
                    UIBuilder uibuilder = new UIBuilder(buttonsRoot);
                    uibuilder.Style.PreferredWidth = BrowserDialog.DEFAULT_ITEM_SIZE * 0.6f;

                    //MixColor method, since its a one liner i would rather just copy source than reflection to get it
                    var pink = MathX.Lerp(color.Purple, color.White, 0.5f);

                    var but = uibuilder.Button(NeosAssets.Common.Icons.Heart, pink, color.Black);

                    but.Slot.OrderOffset = -1;
                    but.LocalPressed += (IButton button, ButtonEventData data) => {
                        Uri url = (AccessTools.Field(typeof(InventoryItemUI), "Item").GetValue(__instance.SelectedInventoryItem) as Record).URL;
                        if (VideoPlayerUrl == url)
                        {
                            url = null;
                        }
                        VideoPlayerUrl = url;
                    };
                }
            }
        }

        // This adds the custom video player tag to saved video players
        [HarmonyPatch(typeof(SlotHelper), "GenerateTags", new Type[] { typeof(Slot), typeof(HashSet<string>) })]
        class SlotHelper_GenerateTags_Patch
        {
            static void Postfix(Slot slot, HashSet<string> tags)
            {
                if(slot.GetComponentInChildren<VideoTextureProvider>() != null)
                {
                    tags.Add(VIDEO_PLAYER_TAG);
                }
            }
        }

        // This actually spawns the video player when you import a video
        // I got help with writing this code from https://github.com/EIA485
        [HarmonyPatch(typeof(VideoImportDialog), "ImportAsync")]
        class VideoImportDialog_ImportAsync_Patch
        {
            // if you are trying to compile the mod and get an error saying that the type VideoImportDialog.VideoType is not accessible,
            // just change it to public using dnSpy, it will work even if the type becomes private again since access modifiers are only a suggestion
            static bool Prefix(ref Task __result, Slot slot, string path, VideoImportDialog.VideoType type, StereoLayout stereo)
            {
                if (VideoPlayerUrl == null) return true;
                if (type == VideoImportDialog.VideoType.Regular)
                {
                    __result = slot.StartTask(async delegate ()
                    {
                        var pos = slot.GlobalPosition; //we need to store the trs of the slot before we load an object to it
                        var rot = slot.GlobalRotation;

                        await slot.LoadObjectAsync(VideoPlayerUrl);
                        InventoryItem component = slot.GetComponent<InventoryItem>();
                        slot = ((component != null) ? component.Unpack(null) : null) ?? slot;

                        slot.GlobalPosition = pos; //revert back to its rts pre object load
                        slot.GlobalRotation = rot;

                        VideoTextureProvider player = slot.GetComponentInChildren<VideoTextureProvider>();

                        Uri uri = new Uri(path);
                        if (uri.Scheme.Contains("file"))
                        {
                            await default(ToBackground);
                            uri = await slot.World.Engine.LocalDB.ImportLocalAssetAsync(path, LocalDB.ImportLocation.Original, null).ConfigureAwait(false);
                            await default(ToWorld);
                        }

                        player.Stream.Value = uri.Scheme == "http" || uri.Scheme == "https" || uri.Scheme == "neosdb" || uri.Scheme == "rtp" || uri.Scheme == "mms" || uri.Scheme == "rtsp";

                        player.URL.Value = uri;

                        slot.ForeachComponentInChildren((VideoPlayer video) => video.StereoLayout.Value = stereo); //this line exists cause otherwise if your spawning an edited default video player when you click play it changes the stereo layout to whatever the player was last set to
                        foreach (IAssetRef Ref in player.References)
                        {
                            if (Ref.Parent is IStereoMaterial)
                            {
                                ImageImporter.SetupStereoLayout(Ref.Parent as IStereoMaterial, stereo);
                            }
                        }
                    });
                    return false;
                }
                return true;
            }
        }
    }
}