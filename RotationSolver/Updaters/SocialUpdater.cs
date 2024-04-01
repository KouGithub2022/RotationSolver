using ECommons.DalamudServices;
using ECommons.GameHelpers;
using Lumina.Excel.GeneratedSheets;
using RotationSolver.Basic.Configuration;
using RotationSolver.Commands;
using RotationSolver.Localization;

namespace RotationSolver.Updaters;

internal class SocialUpdater
{
    public static string GetDutyName(TerritoryType territory) => territory.ContentFinderCondition?.Value?.Name?.RawString;

    internal static void Enable()
    {
        Svc.DutyState.DutyStarted += DutyState_DutyStarted;
        Svc.DutyState.DutyWiped += DutyState_DutyWiped;
        Svc.DutyState.DutyCompleted += DutyState_DutyCompleted;
        Svc.ClientState.TerritoryChanged += ClientState_TerritoryChanged;
        ClientState_TerritoryChanged(Svc.ClientState.TerritoryType);
    }

    static async void DutyState_DutyCompleted(object sender, ushort e)
    {
        if (DataCenter.PartyMembers.Count() < 2) return;

        await Task.Delay(new Random().Next(4000, 6000));

        if (Service.Config.GetValue(PluginConfigBool.AutoOffWhenDutyCompleted))
        {
            RSCommands.CancelState();
        }
    }

    static void ClientState_TerritoryChanged(ushort id)
    {
        DataCenter.ResetAllRecords();

        var territory = Service.GetSheet<TerritoryType>().GetRow(id);

        DataCenter.Territory = territory;

        try
        {
            DataCenter.RightNowRotation?.OnTerritoryChanged();
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "Failed on Territory changed.");
        }
    }

    static void DutyState_DutyStarted(object sender, ushort e)
    {
        if (!Player.Available) return;
        if (!Player.Object.IsJobCategory(JobRole.Tank) && !Player.Object.IsJobCategory(JobRole.Healer)) return;

        if (DataCenter.IsInHighEndDuty)
        {
            string.Format(LocalizationManager.RightLang.HighEndWarning,
                DataCenter.ContentFinderName).ShowWarning();
        }
    }

    static void DutyState_DutyWiped(object sender, ushort e)
    {
        if (!Player.Available) return;
        DataCenter.ResetAllRecords();
    }

    internal static void Disable()
    {
        Svc.DutyState.DutyStarted -= DutyState_DutyStarted;
        Svc.DutyState.DutyWiped -= DutyState_DutyWiped;
        Svc.DutyState.DutyCompleted -= DutyState_DutyCompleted;
        Svc.ClientState.TerritoryChanged -= ClientState_TerritoryChanged;
    }
}
