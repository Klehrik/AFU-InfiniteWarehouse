using MelonLoader;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Il2Cpp;
using Il2CppQuantum;
using Il2CppView_BigScreen;
using Il2CppQuantum_BigScreen;
using AFUtils;

[assembly: MelonInfo(typeof(InfiniteWarehouse.Core), "InfiniteWarehouse", "1.0.3", "Klehrik", null)]
[assembly: MelonGame("Videocult", "Airframe")]
[assembly: MelonAdditionalDependencies("AFUtils")]

namespace InfiniteWarehouse;

public class Core : MelonMod
{
    private float timer    = 0f;
    private float timerMax = 29.98f;
    private bool infinite  = false;
    private LabelScreen labelComp;
    private Command delayCmd;

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
            }
        );
        ActionMenu.RegisterForCollection(
            () =>
            {
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
                if (!f.IsVerified) return;

                GameSettingsSystemPatch.Instance.ChangeSetting(f, GameSettingsSystem.ConsoleID.DelayVote);
                Logger.Msg("Added 30 seconds to vote timer.");
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
    }

    public bool VoteStarted()
    {
        if (labelComp == null) return true;
        if (labelComp.oldText.Contains("00:00")) return true;
        return false;
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