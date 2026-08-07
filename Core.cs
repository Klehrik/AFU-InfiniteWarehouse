using MelonLoader;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Il2Cpp;
using Il2CppQuantum;
using Il2CppView_BigScreen;
using Il2CppQuantum_BigScreen;
using Il2CppPhoton.Realtime;
using Il2CppPhoton.Client;
using Il2CppQuantum_Core;
using AFUtils;
using System.Diagnostics.Metrics;

[assembly: MelonInfo(typeof(InfiniteWarehouse.Core), "InfiniteWarehouse", "1.0.5", "Klehrik", null)]
[assembly: MelonGame("Videocult", "Airframe")]
[assembly: MelonAdditionalDependencies("AFUtils")]

namespace InfiniteWarehouse;

public class Core : MelonMod
{
    private static float timer = 0f;
    private static float timerMax = 20f;
    private static bool infinite = false;
    private static bool infiniteOld = false;
    private static LabelScreen labelComp;

    private static bool allow = true;
    private static int responses;
    private static int responsesRequired;

    private static Command delayCmd;
    private static Command queryCmd;
    private static Command respondCmd;

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
                infiniteOld = infinite;
            }
        );
        ActionMenu.RegisterForCollection(
            () =>
            {
                if (!allow) return;

                var controllerInstance = PhotonController.instance;
                if (controllerInstance != null
                 && controllerInstance.IsMasterClient()
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
        queryCmd = new Command(
            "InfiniteWarehouse_Query",
            (Frame f) =>
            {
                var controllerInstance = PhotonController.instance;
                if (controllerInstance == null
                 || !controllerInstance.IsMasterClient())
                {
                    respondCmd.Send();
                }
            }
        );
        respondCmd = new Command(
            "InfiniteWarehouse_Respond",
            (Frame f) =>
            {
                var controllerInstance = PhotonController.instance;
                if (controllerInstance != null
                 && controllerInstance.IsMasterClient())
                {
                    responses += 1;
                    var met = responses >= responsesRequired;
                    if (met)
                    {
                        allow = true;
                        infinite = infiniteOld;
                    }
                    Logger.Msg($"Responses: {responses} /{responsesRequired}" + (met ? " :)" : ""));
                    Logger.Msg("Reenabled options");
                }
            }
        );
    }

    public override void OnUpdate()
    {
        timer -= UnityEngine.Time.deltaTime;
        if (infinite && timer <= 0 && !VoteStarted())
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

    private static async void QueryLobby(float wait)
    {
        allow = false;
        Logger.Msg($"Disabled options");

        if (wait > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(wait));
        }

        var controllerInstance = PhotonController.instance;
        if (controllerInstance != null
         && controllerInstance.IsMasterClient()
         && SceneManager.GetActiveScene().name == "Warehouse")
        {
            responses = 0;
            responsesRequired = controllerInstance.GetCurrentRoomPlayers().Count - 1;
            Logger.Msg($"Querying lobby (need {responsesRequired} responses)");

            if (responsesRequired <= 0)
            {
                allow = true;
                infinite = infiniteOld;
                Logger.Msg("Reenabled options");
            }
            else
            {
                infinite = false;
                if (!queryCmd.Send(out var error))
                {
                    Logger.Msg(error);
                }
            }
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

    [HarmonyPatch(typeof(InRoomCallbacksContainer))]
    public static class InRoomCallbacksContainerPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(InRoomCallbacksContainer.OnPlayerEnteredRoom))]
        static void OnPlayerEnteredRoom(Il2CppPhoton.Realtime.Player newPlayer)
        {
            QueryLobby(2.5f);
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(InRoomCallbacksContainer.OnPlayerLeftRoom))]
        static void OnPlayerLeftRoom(Il2CppPhoton.Realtime.Player otherPlayer)
        {
            QueryLobby(0);
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

    [HarmonyPatch(typeof(PlayerJoinSystem), nameof(PlayerJoinSystem.OnPlayerAdded))]
    public static class PlayerJoinSystemPatch
    {
        static void Postfix(PlayerJoinSystem __instance)
        {
            var controllerInstance = PhotonController.instance;
            if (controllerInstance.IsMasterClient())
            {
                var client = controllerInstance.client;
                var room = client.CurrentRoom;
                if (client.CurrentRoom != null)
                {
                    var commandMapping = new PhotonHashtable();
                    commandMapping.Add("enabled", infiniteOld.ToString());
                    var properties = new PhotonHashtable();
                    properties.Add("InfiniteWarehouse", commandMapping);
                    client.LocalPlayer.SetCustomProperties(properties, null);
                }
            }
        }
    }

    [HarmonyPatch(typeof(PhotonController), nameof(PhotonController.OnPlayerPropertiesUpdate))]
    public static class PhotonControllerPatch
    {
        static void Postfix(Il2CppPhoton.Realtime.Player targetPlayer, PhotonHashtable changedProps)
        {
            if (targetPlayer.IsLocal) return;
            if (!changedProps.ContainsKey("InfiniteWarehouse")) return;

            var commands = changedProps["InfiniteWarehouse"].Cast<PhotonHashtable>();
            infinite = bool.Parse(commands["enabled"].ToString());
            infiniteOld = infinite;
        }
    }
}