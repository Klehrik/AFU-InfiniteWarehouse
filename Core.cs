using MelonLoader;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using Il2CppQuantum;
using Il2CppView_BigScreen;
using Il2CppQuantum_BigScreen;
using Il2CppView_Humanoid;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

[assembly: MelonInfo(typeof(InfiniteWarehouse.Core), "InfiniteWarehouse", "1.0.1", "Klehrik", null)]
[assembly: MelonGame("Videocult", "Airframe")]

namespace InfiniteWarehouse;

public class Core : MelonMod
{
    private float timer    = 0f;
    private float timerMax = 30f;
    private bool infinite  = false;
    private LabelScreen labelComp;
    private static int mostRecentFrame = 0; // Prevent duplicate requests on the same frame
    public static MelonLogger.Instance Logger => Melon<Core>.Logger;
    private const int BarkID = 20260728;

    public override void OnUpdate()
    {
        if (Keyboard.current.oKey.wasPressedThisFrame)
        {
            DelayRequest();
        }

        if (Keyboard.current.iKey.wasPressedThisFrame)
        {
            infinite = !infinite;
            Logger.Msg("Infinite: " + infinite.ToString());
        }

        timer -= UnityEngine.Time.deltaTime;
        if (infinite && timer <= 0 && !VoteStarted())
        {
            timer = timerMax;
            DelayRequest();
        }
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        var transform = GameObject.Find("ConsolesScreensPodiums")?.transform.Find("LabelScreen");
        if (transform == null) return;

        var obj = transform.gameObject;
        if (obj == null) return;

        var comp = obj.GetComponent<LabelScreen>();
        if (comp == null) return;

        labelComp = comp;
    }

    public void DelayRequest()
    {
        var game = QuantumRunner.Default?.Game;
        if (game == null) return;

        Il2CppArrayBase<Humanoid_View> views = GameObject.FindObjectsOfType<Humanoid_View>();
        foreach (var view in views)
        {
            if (view.isLocal)
            {
                var cmd = new BarkCommand();
                cmd.bark = BarkID;
                cmd.senderPlayer = view.playerEntityRef;
                game.SendCommand(cmd);
            }
        }
    }

    public static void DelayAdd(int frameNumber)
    {
        if (GameSettingsSystemPatch.Instance == null) return;
        if (frameNumber <= mostRecentFrame) return;
        mostRecentFrame = frameNumber;

        var game = QuantumRunner.Default?.Game;
        if (game == null) return;

        var frame = game.Frames?.Verified;
        if (frame == null) return;

        GameSettingsSystemPatch.Instance.ChangeSetting(frame, GameSettingsSystem.ConsoleID.DelayVote);
        Logger.Msg("Added 30 seconds to vote timer.");
    }

    public bool VoteStarted()
    {
        if (labelComp == null) return true;
        if (labelComp.oldText.Contains("00:00")) return true;
        return false;
    }

    // Hijacking BarkCommand to signal delay request
    // For some reason, Execute can call multiple times on the same frame
    [HarmonyPatch(typeof(BarkCommand), nameof(BarkCommand.Execute))]
    public static class BarkCommandPatch
    {
        static bool Prefix(BarkCommand __instance, Frame f)
        {
            if (__instance.bark == BarkID)
            {
                DelayAdd(f.Number);
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(GameSettingsSystem), nameof(GameSettingsSystem.OnInit))]
    public static class GameSettingsSystemPatch
    {
        public static GameSettingsSystem Instance;

        static void Postfix(GameSettingsSystem __instance)
        {
            Instance = __instance;
        }
    }

    // Keep all LabelScreens on
    [HarmonyPatch(typeof(LabelScreen), nameof(LabelScreen.ToggleScreenOn))]
    public static class LabelScreenPatch
    {
        static void Prefix(ref bool newScreenOn)
        {
            newScreenOn = true;
        }
    }
}