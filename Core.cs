using MelonLoader;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Il2Cpp;
using Il2CppQuantum;
using Il2CppView_BigScreen;
using Il2CppQuantum_BigScreen;
using Il2CppPhoton.Realtime;
using AFUtils;

[assembly: MelonInfo(typeof(InfiniteWarehouse.Core), "InfiniteWarehouse", "1.0.4", "Klehrik", null)]
[assembly: MelonGame("Videocult", "Airframe")]
[assembly: MelonAdditionalDependencies("AFUtils")]

namespace InfiniteWarehouse;

public class Core : MelonMod
{
    private static float timer = 0f;
    private static float timerMax = 29.98f;
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
                }
            }
        );
    }

    public override void OnUpdate()
    {
        timer -= UnityEngine.Time.deltaTime;
        if (infinite && timer <= 0 && !VoteStarted())
        {
            timer = timerMax;
            delayCmd.Send();
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
        await Task.Delay(TimeSpan.FromSeconds(wait));

        var controllerInstance = PhotonController.instance;
        if (controllerInstance != null
         && controllerInstance.IsMasterClient())
        {
            responses = 0;
            responsesRequired = controllerInstance.GetCurrentRoomPlayers().Count - 1;
            Logger.Msg($"Querying lobby (need {responsesRequired} responses)");

            if (responsesRequired <= 0)
            {
                allow = true;
                infinite = infiniteOld;
            }
            else
            {
                infinite = false;
                queryCmd.Send();
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
            allow = false;
            QueryLobby(1.5f);
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(InRoomCallbacksContainer.OnPlayerLeftRoom))]
        static void OnPlayerLeftRoom(Il2CppPhoton.Realtime.Player otherPlayer)
        {
            allow = false;
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
}