using MelonLoader;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Il2Cpp;
using Il2CppQuantum;
using Il2CppView_BigScreen;
using Il2CppQuantum_BigScreen;
using Il2CppQuantum_Core;
using AFUtils;

[assembly: MelonInfo(typeof(InfiniteWarehouse.Core), "InfiniteWarehouse", "1.0.6", "Klehrik", null)]
[assembly: MelonGame("Videocult", "Airframe")]
[assembly: MelonAdditionalDependencies("AFUtils")]

namespace InfiniteWarehouse;

public class Core : MelonMod
{
    private static float timer = 0f;
    private static float timerMax = 25f;
    private static bool infinite = false;
    private static LabelScreen labelComp;

    private static bool allow = true;
    private static List<Il2CppPhoton.Realtime.Player> responses = new List<Il2CppPhoton.Realtime.Player>();
    private static int responsesRequired;

    private static Command delayCmd;

    private static Packet syncInfinitePacket;
    private static Packet queryPacket;
    private static Packet responsePacket;

    public static MelonLogger.Instance Logger => Melon<Core>.Logger;

    public override void OnInitializeMelon()
    {
        var option = new ActionMenu.Option(
            () =>
            {
                delayCmd.Send();
            },
            "delay vote"
        );
        var option2 = new ActionMenu.Option(
            () =>
            {
                infinite = !infinite;
                Logger.Msg($"Infinite: {infinite.ToString()}");
                SyncInfinite();
            }
        );
        ActionMenu.RegisterForCollection(
            () =>
            {
                if (!allow) return;

                if (Misc.IsHost()
                 && SceneManager.GetActiveScene().name == "Warehouse")
                {
                    ActionMenu.AddOption(option);
                    ActionMenu.AddOption(option2, "delay forever [" + (infinite ? "ON" : "OFF") + "]");
                }
            }
        );

        delayCmd = new Command(
            "InfiniteWarehouse_Delay",
            (Frame f) =>
            {
                if (!allow) return;
                if (!f.IsVerified) return;

                GameSettingsSystemPatch.Instance.ChangeSetting(f, GameSettingsSystem.ConsoleID.DelayVote);
                Logger.Msg("Added 30 seconds to vote timer");
            }
        );

        syncInfinitePacket = new Packet(
            "InfiniteWarehouse_SyncInfinite",
            (Il2CppPhoton.Realtime.Player player, Dictionary<string, string> data) =>
            {
                infinite = bool.Parse(data["infinite"]);
                Logger.Msg($"Infinite: {infinite.ToString()}");
            }
        );
        queryPacket = new Packet(
            "InfiniteWarehouse_Query",
            (Il2CppPhoton.Realtime.Player player, Dictionary<string, string> data) =>
            {
                responsePacket.Send(new Dictionary<string, string>());
            }
        );
        responsePacket = new Packet(
            "InfiniteWarehouse_Response",
            (Il2CppPhoton.Realtime.Player player, Dictionary<string, string> data) =>
            {
                if (!responses.Contains(player))
                {
                    responses.Add(player);
                    var met = responses.Count >= responsesRequired;
                    Logger.Msg($"Responses: {responses.Count} /{responsesRequired}" + (met ? " :)" : ""));
                    if (met)
                    {
                        allow = true;
                        Logger.Msg("Reenabled options");
                    }
                }
            }
        );
    }

    public override void OnUpdate()
    {
        timer -= UnityEngine.Time.deltaTime;
        if (allow && infinite && timer <= 0 && !VoteStarted())
        {
            if (delayCmd.Send())
            {
                timer = timerMax;
            }
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
        allow = true;
    }

    private bool VoteStarted()
    {
        if (labelComp == null) return true;
        if (labelComp.oldText.Contains("00:00")) return true;
        return false;
    }

    public static void SyncInfinite()
    {
        if (Misc.IsHost())
        {
            // Sync `infinite` toggle status with everyone; if the host leaves, infinite will stay on/off
            syncInfinitePacket.Send(new Dictionary<string, string> { ["infinite"] = infinite.ToString() });
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

    [HarmonyPatch(typeof(PlayerJoinSystem), nameof(PlayerJoinSystem.OnPlayerAdded))]
    public static class PlayerJoinSystemPatch
    {
        static void Postfix(PlayerJoinSystem __instance)
        {
            if (Misc.IsHost()
             && SceneManager.GetActiveScene().name == "Warehouse")
            {
                SyncInfinite();

                responses.Clear();
                responsesRequired = PhotonController.instance.GetCurrentRoomPlayers().Count - 1;
                if (responsesRequired > 0)
                {
                    allow = false;
                    Logger.Msg($"Disabled options; {responsesRequired} responses required");
                    queryPacket.Send(new Dictionary<string, string>());
                }
            }
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