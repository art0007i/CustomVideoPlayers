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
using SpecialItemsLib;

namespace CustomVideoPlayers
{
    public class CustomVideoPlayers : NeosMod
    {
        public override string Name => "CustomVideoPlayers";
        public override string Author => "art0007i";
        public override string Version => "2.0.0";
        public override string Link => "https://github.com/art0007i/CustomVideoPlayers/";
        public override void OnEngineInit()
        {
            Harmony harmony = new Harmony("me.art0007i.CustomVideoPlayers");
            harmony.PatchAll();
            // Register our item in the library, this returns an object that allows retrieving the url which is managed by the library
            OurItem = SpecialItemsLib.SpecialItemsLib.RegisterItem(VIDEO_PLAYER_TAG);
        }

        // Constant defintions, including: a tag that gets added to all saved video players, and our special item which we can use to retrieve the url
        private static string VIDEO_PLAYER_TAG { get { return "custom_video_player"; } } 
        private static CustomSpecialItem OurItem;

        // This adds the custom video player tag to saved video players
        // It's necessary for the library to recognize it as a special item and allow favoriting it
        [HarmonyPatch(typeof(SlotHelper), "GenerateTags", new Type[] { typeof(Slot), typeof(HashSet<string>) })]
        class SlotHelper_GenerateTags_Patch
        {
            static void Postfix(Slot slot, HashSet<string> tags)
            {
                // usually you only need to change which component you are looking for
                // but you can change up this entire method in case you have a more complex
                // algorithm for figuring out if an item should be favoritable
                if (slot.GetComponent<VideoTextureProvider>() != null)
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
            //the `int type` should be an enum but the enum is private so compiler doesn't like it, but luckily we can use an integer instead
            static bool Prefix(ref Task __result, Slot slot, string path, int type, StereoLayout stereo)
            {
                if (OurItem.Uri == null) return true;
                if (type == 0) //VideoImportDialog.VideoType.Regular has a value of 0
                {
                    __result = slot.StartTask(async delegate ()
                    {
                        var pos = slot.GlobalPosition; //we need to store the trs of the slot before we load an object to it
                        var rot = slot.GlobalRotation;

                        await slot.LoadObjectAsync(OurItem.Uri);
                        InventoryItem component = slot.GetComponent<InventoryItem>();
                        slot = ((component != null) ? component.Unpack(null) : null) ?? slot;

                        slot.GlobalPosition = pos; //revert back to its rts pre object load
                        slot.GlobalRotation = rot;

                        VideoTextureProvider player = slot.GetComponentInChildren<VideoTextureProvider>();

                        Uri uri = new Uri(path);
                        if (uri.Scheme.Contains("file"))
                        {
                            uri = await slot.World.Engine.LocalDB.ImportLocalAssetAsync(path, LocalDB.ImportLocation.Original, null).ConfigureAwait(false);
                        }

                        player.Stream.Value = uri.Scheme == "http" || uri.Scheme == "https" || uri.Scheme == "neosdb" || uri.Scheme == "rtp" || uri.Scheme == "mms" || uri.Scheme == "rtsp";

                        player.URL.Value = uri;

                        slot.ForeachComponentInChildren((VideoPlayer video) => video.StereoLayout.Value = stereo);
                        //this line exists cause otherwise if your spawning an edited default video player when you click play it changes the stereo layout to whatever the player was last set to
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
