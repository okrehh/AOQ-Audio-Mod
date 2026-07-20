using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using VRStandardAssets.Utils;

namespace AOQAudioMod
{
    [BepInPlugin("com.owen.aoqaudiomod", "AOQ Audio Mod", "1.0.0")]
    public class AudioMod : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        private static readonly Dictionary<string, List<AudioClip>> replacements =
            new Dictionary<string, List<AudioClip>>(
                System.StringComparer.OrdinalIgnoreCase
            );

        private static readonly HashSet<AudioClip> loadedReplacementClips =
            new HashSet<AudioClip>();

        

        private void Awake()
        {
            Log = Logger;

            Logger.LogInfo("AOQ Audio Mod loaded!");

            StartCoroutine(LoadSounds());
            StartCoroutine(WatchAudioSources());

            StartCoroutine(ReplaceReferencesLoop());
           
            Harmony harmony = new Harmony("com.owen.aoqaudiomod");
            harmony.PatchAll();
        }

        private static string GetReplacementGroup(string clipName)
        {
            if (string.IsNullOrEmpty(clipName))
                return clipName;

            int underscorePosition =
                clipName.LastIndexOf('_');

            if (underscorePosition > 0)
            {
                string suffix =
                    clipName.Substring(underscorePosition + 1);

                int variationNumber;

                if (int.TryParse(suffix, out variationNumber))
                {
                    return clipName.Substring(
                        0,
                        underscorePosition
                    );
                }
            }

            return clipName;
        }
        private IEnumerator LoadSounds()
        {
            string folder = Path.Combine(
                Paths.PluginPath,
                "AOQAudioMod",
                "Sounds"
            );


            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
                Logger.LogInfo("Created Sounds folder.");
                yield break;
            }


            string[] files = Directory.GetFiles(folder, "*.wav");


            foreach (string file in files)
            {
                using (WWW www = new WWW("file://" + file))
                {
                    yield return www;

                    AudioClip clip = www.GetAudioClip(false, false);

                    if (clip != null)
                    {
                        string fileName =
                            Path.GetFileNameWithoutExtension(file);

                        string groupName =
                            GetReplacementGroup(fileName);

                        // Keep the variation name for logging.
                        clip.name = fileName;

                        List<AudioClip> group;

                        if (!replacements.TryGetValue(
                                groupName,
                                out group))
                        {
                            group = new List<AudioClip>();
                            replacements[groupName] = group;
                        }

                        group.Add(clip);

                        // Marks this exact AudioClip object as one loaded by the mod.
                        loadedReplacementClips.Add(clip);

                        Logger.LogInfo(
                            "Loaded replacement: " +
                            fileName +
                            " | Group=" +
                            groupName
                        );
                    }
                }
            }
        }

        private IEnumerator WatchAudioSources()
        {
            yield return new WaitUntil(
                () => replacements.Count > 0
            );

            while (true)
            {
                AudioSource[] sources =
                    Resources.FindObjectsOfTypeAll<AudioSource>();

                foreach (AudioSource source in sources)
                {
                    if (source == null || source.clip == null)
                        continue;

                    AudioClip original = source.clip;

                    // Critical:
                    // Do not reroll a variation every 0.1 seconds.
                    if (IsLoadedReplacement(original))
                        continue;

                    AudioClip replacement;

                    if (TryGetReplacement(
                            original.name,
                            out replacement) &&
                        replacement != null &&
                        original != replacement)
                    {
                        bool wasPlaying =
                            source.isPlaying;

                        string objectName =
                            source.gameObject != null
                                ? source.gameObject.name
                                : "(no object)";

                        source.clip = replacement;

                        // Restart only once if the original sound
                        // was already playing when first replaced.
                        if (wasPlaying &&
                            source.enabled &&
                            source.gameObject.activeInHierarchy)
                        {
                            source.time = 0f;
                            source.Play();
                        }

                        Logger.LogInfo(
                            "FAST SOURCE REPLACED: " +
                            objectName +
                            " | " +
                            original.name +
                            " -> " +
                            replacement.name +
                            " | wasPlaying=" +
                            wasPlaying
                        );
                    }
                }

                yield return new WaitForSecondsRealtime(0.1f);
            }
        }
        private IEnumerator ReplaceReferencesLoop()
        {
            // Give the first scene and the replacement WAVs time to load.
            yield return new WaitForSeconds(5f);

            while (true)
            {
                int replacedCount = 0;



                // Handles AudioClip fields stored inside game scripts,
                // such as hookRetract, gasReload, attachBlade, etc.
                MonoBehaviour[] behaviours =
                    Resources.FindObjectsOfTypeAll<MonoBehaviour>();

                foreach (MonoBehaviour behaviour in behaviours)
                {
                    if (behaviour != null)
                    {
                        replacedCount +=
                            ReplaceAudioClipFields(behaviour);
                    }
                }

                // Also checks ScriptableObjects in case the game stores
                // audio references inside data/configuration objects.
                ScriptableObject[] scriptableObjects =
                    Resources.FindObjectsOfTypeAll<ScriptableObject>();

                foreach (ScriptableObject scriptableObject in scriptableObjects)
                {
                    if (scriptableObject != null)
                    {
                        replacedCount +=
                            ReplaceAudioClipFields(scriptableObject);
                    }
                }

                if (replacedCount > 0)
                {
                    Logger.LogInfo(
                        "Reference scan replaced " +
                        replacedCount +
                        " audio reference(s)."
                    );
                }

                // Repeat so newly spawned network objects and Titans
                // also receive replacements.
                yield return new WaitForSeconds(3f);
            }
        }


        private int ReplaceAudioClipFields(object target)
        {
            int replacedCount = 0;
            Type currentType = target.GetType();

            while (currentType != null &&
                   currentType != typeof(object))
            {
                FieldInfo[] fields = currentType.GetFields(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly
                );

                foreach (FieldInfo field in fields)
                {
                    if (field.IsLiteral || field.IsInitOnly)
                        continue;

                    try
                    {
                        // Single AudioClip field
                        if (field.FieldType == typeof(AudioClip))
                        {
                            AudioClip original =
                                field.GetValue(target) as AudioClip;

                            if (original == null)
                                continue;

                            if (IsLoadedReplacement(original))
                                continue;

                            AudioClip replacement;

                            if (TryGetReplacement(
                                    original.name,
                                    out replacement) &&
                                original != replacement)
                            {
                                field.SetValue(target, replacement);
                                replacedCount++;

                                Logger.LogInfo(
                                    "Replaced field: " +
                                    currentType.Name +
                                    "." +
                                    field.Name +
                                    " (" +
                                    original.name +
                                    ")"
                                );
                            }
                        }

                        // AudioClip array field
                        else if (field.FieldType == typeof(AudioClip[]))
                        {
                            AudioClip[] clips =
                                field.GetValue(target) as AudioClip[];

                            if (clips == null)
                                continue;

                            bool arrayChanged = false;

                            for (int i = 0; i < clips.Length; i++)
                            {
                                AudioClip original = clips[i];

                                if (original == null)
                                    continue;

                                if (IsLoadedReplacement(original))
                                    continue;

                                AudioClip replacement;

                                if (TryGetReplacement(
                                        original.name,
                                        out replacement) &&
                                    original != replacement)
                                {
                                    clips[i] = replacement;
                                    replacedCount++;
                                    arrayChanged = true;

                                    Logger.LogInfo(
                                        "Replaced array field: " +
                                        currentType.Name +
                                        "." +
                                        field.Name +
                                        "[" +
                                        i +
                                        "] (" +
                                        original.name +
                                        ")"
                                    );
                                }
                            }

                            if (arrayChanged)
                            {
                                field.SetValue(target, clips);
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        // Some Unity/native-backed fields cannot be inspected.
                        // Skip those rather than stopping the entire scan.
                        Logger.LogDebug(
                            "Could not inspect " +
                            currentType.Name +
                            "." +
                            field.Name +
                            ": " +
                            exception.Message
                        );
                    }
                }

                currentType = currentType.BaseType;
            }

            return replacedCount;
        }

            internal static void LogCableEvent(
            AudioSource source,
            string action)
            {
            if (source == null || source.clip == null)
                return;

            if (!source.clip.name.Equals(
                    "CableRetract2",
                    System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string objectName =
                source.gameObject != null
                    ? source.gameObject.name
                    : "(no object)";

            Log.LogInfo(
                "CABLE EVENT -> " +
                action +
                " | Object=" +
                objectName +
                " | Enabled=" +
                source.enabled +
                " | Active=" +
                source.gameObject.activeInHierarchy +
                " | Volume=" +
                source.volume +
                " | IsPlayingBefore=" +
                source.isPlaying
            );
        }
        internal static bool TryPlayOverlappingRandomClip(
            AudioSource source,
            string playbackMethod)
        {
            if (source == null || source.clip == null)
                return false;

            // Do not interfere with looping sounds such as
            // CableRetract2 or SteamSound.
            if (source.loop)
                return false;

            string groupName =
                GetReplacementGroup(source.clip.name);

            // Only HookFire needs overlapping playback right now.
            if (!groupName.Equals(
                    "HookFire",
                    System.StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            List<AudioClip> group;

            if (!replacements.TryGetValue(
                    groupName,
                    out group))
            {
                return false;
            }

            if (group == null || group.Count == 0)
                return false;

            AudioClip replacement;

            if (!TryGetReplacement(
                    source.clip.name,
                    out replacement) ||
                replacement == null)
            {
                return false;
            }

            string objectName =
                source.gameObject != null
                    ? source.gameObject.name
                    : "(no object)";

            // PlayOneShot allows this sound to overlap an earlier
            // hook-fire sound on the same AudioSource.
            source.PlayOneShot(replacement, 1f);

            Log.LogInfo(
                playbackMethod +
                " overlapping random sound: Object=" +
                objectName +
                " | Group=" +
                groupName +
                " | Selected=" +
                replacement.name
            );

            // Tell the Harmony patch that we handled the playback.
            return true;
        }
        internal static void ReplaceSourceClip(
        AudioSource source,
        string playbackMethod)
        {
            if (source == null || source.clip == null)
                return;

            AudioClip previousClip =
                source.clip;

            AudioClip replacement;

            if (TryGetReplacement(
                    previousClip.name,
                    out replacement) &&
                replacement != null)
            {
                source.clip = replacement;

                string objectName =
                    source.gameObject != null
                        ? source.gameObject.name
                        : "(no object)";

                Log.LogInfo(
                    playbackMethod +
                    " random selection: Object=" +
                    objectName +
                    " | " +
                    previousClip.name +
                    " -> " +
                    replacement.name
                );
            }
        }

        internal static bool IsLoadedReplacement(AudioClip clip)
        {
            return clip != null &&
                   loadedReplacementClips.Contains(clip);
        }
        internal static bool TryGetReplacement(
        string originalName,
        out AudioClip replacement)
        {
            replacement = null;

            string groupName =
                GetReplacementGroup(originalName);

            List<AudioClip> group;

            if (!replacements.TryGetValue(
                    groupName,
                    out group))
            {
                return false;
            }

            if (group == null || group.Count == 0)
                return false;

            int randomIndex =
                UnityEngine.Random.Range(
                    0,
                    group.Count
                );

            replacement = group[randomIndex];

            return replacement != null;

        }
        

        

    }

    [HarmonyPatch(
    typeof(AudioSource),
    "Play",
    new System.Type[] { })]
    class PlayNoArgumentsPatch
    {
        static bool Prefix(AudioSource __instance)
        {
            // HookFire is played as an overlapping one-shot.
            if (AudioMod.TryPlayOverlappingRandomClip(
                    __instance,
                    "PLAY()"))
            {
                // Skip the game's original Play(), since running it
                // would restart the shared AirBlast AudioSource.
                return false;
            }

            // All other AudioSources keep their existing behavior.
            AudioMod.ReplaceSourceClip(
                __instance,
                "PLAY()"
            );

            return true;
        }
    }


    [HarmonyPatch(
     typeof(AudioSource),
     "Play",
     new System.Type[] { typeof(ulong) })]
    class PlayWithDelayPatch
    {
        static void Prefix(AudioSource __instance)
        {
            AudioMod.ReplaceSourceClip(
                __instance,
                "PLAY(ulong)"
            );
        }
    }


    [HarmonyPatch(
    typeof(AudioSource),
    "PlayOneShot",
    new Type[]
    {
        typeof(AudioClip),
        typeof(float)
    })]
    public class PlayOneShotPatch
    {
        static void Prefix(
            AudioSource __instance,
            ref AudioClip clip)
        {
            if (clip == null)
                return;

            AudioClip replacement;

            if (AudioMod.TryGetReplacement(
                clip.name,
                out replacement))
            {
                AudioMod.Log.LogInfo(
                    "PlayOneShot replacement: " +
                    clip.name
                );

                clip = replacement;
            }
        }
    }



    [HarmonyPatch(typeof(AudioSource), "PlayOneShot",
    new System.Type[]
    {
        typeof(AudioClip)
    })]
    class PlayOneShotNoVolumePatch
    {
        static void Prefix(ref AudioClip clip)
        {
            if (clip == null)
                return;

            AudioMod.Log.LogInfo(
                "PlayOneShot(no volume): " + clip.name
            );

            AudioClip replacement;

            if (AudioMod.TryGetReplacement(
                clip.name,
                out replacement))
            {
                AudioMod.Log.LogInfo(
                    "SUCCESS replacing: " + clip.name
                );

                clip = replacement;
            }
        }
    }

    [HarmonyPatch(typeof(AudioSource),
    "PlayOneShot",
    new System.Type[]
    {
        typeof(AudioClip)
    })]

    class PlayOneShotSimplePatch
    {
        static void Prefix(ref AudioClip clip)
        {
            if (clip == null)
                return;


            AudioMod.Log.LogInfo(
                "ONESHOT AUDIO: "
                + clip.name
            );


            AudioClip replacement;


            if (AudioMod.TryGetReplacement(
                clip.name,
                out replacement))
            {
                AudioMod.Log.LogInfo(
                    "REPLACED ONESHOT: "
                    + clip.name
                );

                clip = replacement;
            }
        }
    }





    [HarmonyPatch(typeof(NetworkBlades), "Start")]
    class NetworkBladesPatch
    {
        static void Postfix(NetworkBlades __instance)
        {
            if (__instance.airBlast == null)
            {
                AudioMod.Log.LogInfo("airBlast is NULL");
                return;
            }

            AudioMod.Log.LogInfo("==== AIRBLAST ====");
            AudioMod.Log.LogInfo("Object: " + __instance.airBlast.gameObject.name);

            if (__instance.airBlast.clip != null)
                AudioMod.Log.LogInfo("Clip: " + __instance.airBlast.clip.name);
            else
                AudioMod.Log.LogInfo("Clip is NULL");
        }
    }

    [HarmonyPatch(typeof(AudioSource), "PlayDelayed")]
    class PlayDelayedPatch
    {
        static void Prefix(AudioSource __instance)
        {
            if (__instance.clip == null)
                return;

            AudioMod.Log.LogInfo("[PlayDelayed] " + __instance.clip.name);

            if (AudioMod.TryGetReplacement(__instance.clip.name, out AudioClip replacement))
            {
                AudioMod.Log.LogInfo("Replacing PlayDelayed: " + __instance.clip.name);
                __instance.clip = replacement;
            }
        }
    }

    [HarmonyPatch(typeof(AudioSource), "PlayScheduled")]
    class PlayScheduledPatch
    {
        static void Prefix(AudioSource __instance)
        {
            if (__instance.clip == null)
                return;

            AudioMod.Log.LogInfo("[PlayScheduled] " + __instance.clip.name);

            if (AudioMod.TryGetReplacement(__instance.clip.name, out AudioClip replacement))
            {
                AudioMod.Log.LogInfo("Replacing PlayScheduled: " + __instance.clip.name);
                __instance.clip = replacement;
            }
        }
    }

    [HarmonyPatch(typeof(AudioSource),
    "PlayClipAtPoint",
    new System.Type[]
    {
        typeof(AudioClip),
        typeof(Vector3),
        typeof(float)
    })]
    class PlayClipAtPointPatch
    {
        static void Prefix(ref AudioClip clip)
        {
            if (clip == null)
                return;

            AudioMod.Log.LogInfo("[PlayClipAtPoint] " + clip.name);

            if (AudioMod.TryGetReplacement(clip.name, out AudioClip replacement))
            {
                AudioMod.Log.LogInfo("Replacing PlayClipAtPoint: " + clip.name);
                clip = replacement;
            }
        }
    }


    [HarmonyPatch(typeof(AudioSource), "set_clip")]

    class SetClipPatch
    {
        static void Prefix(AudioSource __instance, ref AudioClip value)
        {
            if (value == null)
                return;

            AudioMod.Log.LogInfo(
                $"SET CLIP -> Object={__instance.gameObject.name}  Clip={value.name}"
            );

            AudioClip replacement;

            if (AudioMod.TryGetReplacement(value.name, out replacement))
            {
                AudioMod.Log.LogInfo(
                    $"SET CLIP REPLACED -> {value.name}"
                );

                value = replacement;
            }
        }
    }


                   
        [HarmonyPatch(typeof(GrapplingHook), "Update")]
    class GrapplingHookCableLogicPatch
    {
        private static System.Type staticVariablesType;

        private static System.Reflection.FieldInfo leftHookedField;
        private static System.Reflection.FieldInfo rightHookedField;
        private static System.Reflection.FieldInfo leftReelField;
        private static System.Reflection.FieldInfo rightReelField;

        private static System.Reflection.PropertyInfo leftHookedProperty;
        private static System.Reflection.PropertyInfo rightHookedProperty;
        private static System.Reflection.PropertyInfo leftReelProperty;
        private static System.Reflection.PropertyInfo rightReelProperty;

        private static bool attemptedResolution;
        private static bool warnedAboutFailure;
        private static bool fixWasActive;
        private static string lastLoggedState;


        private static void ResolveStaticVariables()
        {
            if (attemptedResolution)
                return;

            attemptedResolution = true;

            // Try Harmony's normal type lookup first.
            staticVariablesType =
                AccessTools.TypeByName("StaticVariables");

            // Fall back to searching every loaded assembly.
            if (staticVariablesType == null)
            {
                foreach (System.Reflection.Assembly assembly
                         in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        foreach (System.Type type in assembly.GetTypes())
                        {
                            if (type != null &&
                                type.Name == "StaticVariables")
                            {
                                staticVariablesType = type;
                                break;
                            }
                        }
                    }
                    catch (System.Reflection.ReflectionTypeLoadException exception)
                    {
                        foreach (System.Type type in exception.Types)
                        {
                            if (type != null &&
                                type.Name == "StaticVariables")
                            {
                                staticVariablesType = type;
                                break;
                            }
                        }
                    }

                    if (staticVariablesType != null)
                        break;
                }
            }

            if (staticVariablesType == null)
            {
                AudioMod.Log.LogError(
                    "CABLE FIX: Could not find StaticVariables."
                );

                return;
            }

            leftHookedField =
                AccessTools.Field(staticVariablesType, "leftHooked");

            rightHookedField =
                AccessTools.Field(staticVariablesType, "rightHooked");

            leftReelField =
                AccessTools.Field(staticVariablesType, "leftReel");

            rightReelField =
                AccessTools.Field(staticVariablesType, "rightReel");

            // These fallbacks handle the possibility that they are properties
            // rather than fields.
            leftHookedProperty =
                AccessTools.Property(staticVariablesType, "leftHooked");

            rightHookedProperty =
                AccessTools.Property(staticVariablesType, "rightHooked");

            leftReelProperty =
                AccessTools.Property(staticVariablesType, "leftReel");

            rightReelProperty =
                AccessTools.Property(staticVariablesType, "rightReel");

            AudioMod.Log.LogInfo(
                "CABLE FIX: Found StaticVariables type: " +
                staticVariablesType.FullName
            );
        }


        private static bool ReadBool(
            System.Reflection.FieldInfo field,
            System.Reflection.PropertyInfo property)
        {
            try
            {
                if (field != null)
                {
                    object value = field.GetValue(null);

                    if (value is bool)
                        return (bool)value;
                }

                if (property != null)
                {
                    object value = property.GetValue(null, null);

                    if (value is bool)
                        return (bool)value;
                }
            }
            catch (System.Exception exception)
            {
                AudioMod.Log.LogError(
                    "CABLE FIX: Failed reading state: " +
                    exception.Message
                );
            }

            return false;
        }


        static void Postfix(
            GrapplingHook __instance,
            AudioSource ___cableRetract)
        {
            if (__instance == null ||
                !__instance.enabled ||
                !__instance.gameObject.activeInHierarchy)
            {
                return;
            }

            ResolveStaticVariables();

            if (staticVariablesType == null)
                return;

            bool leftHooked =
                ReadBool(leftHookedField, leftHookedProperty);

            bool rightHooked =
                ReadBool(rightHookedField, rightHookedProperty);

            bool leftReel =
                ReadBool(leftReelField, leftReelProperty);

            bool rightReel =
                ReadBool(rightReelField, rightReelProperty);

            string currentState =
                "LH=" + leftHooked +
                " RH=" + rightHooked +
                " LR=" + leftReel +
                " RR=" + rightReel;

            // Logs only when one of the hook/reel values changes.
            if (currentState != lastLoggedState)
            {
                AudioMod.Log.LogInfo(
                    "CABLE STATE: " + currentState
                );

                lastLoggedState = currentState;
            }

            bool bothHooksAttached =
                leftHooked && rightHooked;

            bool exactlyOneReel =
                leftReel ^ rightReel;

            bool applyFix =
                bothHooksAttached && exactlyOneReel;

            if (!applyFix)
            {
                fixWasActive = false;
                return;
            }

            if (___cableRetract == null)
            {
                if (!warnedAboutFailure)
                {
                    AudioMod.Log.LogError(
                        "CABLE FIX: cableRetract AudioSource was null."
                    );

                    warnedAboutFailure = true;
                }

                return;
            }

            AudioMod.ReplaceSourceClip(
                ___cableRetract,
                "CABLE LOGIC FIX"
            );

            // GrapplingHook.Update paused it earlier in this same frame.
            if (!___cableRetract.isPlaying)
            {
                ___cableRetract.UnPause();

                // UnPause only works if it was actually paused.
                // Play is the fallback if it had fully stopped.
                if (!___cableRetract.isPlaying)
                {
                    ___cableRetract.Play();
                }
            }

            if (!fixWasActive)
            {
                AudioMod.Log.LogInfo(
                    "CABLE FIX ACTIVE: " + currentState
                );
            }

            fixWasActive = true;
        }
    


        private static readonly Dictionary<string, List<AudioClip>> replacements =
        new Dictionary<string, List<AudioClip>>(
        System.StringComparer.OrdinalIgnoreCase
         );

        private static readonly HashSet<AudioClip> loadedReplacementClips =
            new HashSet<AudioClip>();

       


       

    }




}
