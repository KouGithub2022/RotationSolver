using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using RotationSolver.Basic.Configuration;
using RotationSolver.Commands;
using RotationSolver.Localization;
using RotationSolver.UI;
using System.Runtime.InteropServices;

namespace RotationSolver.Updaters;

internal static class MajorUpdater
{
    public static unsafe bool IsValid => Svc.Condition.Any()
        && !Svc.Condition[ConditionFlag.BetweenAreas]
        && !Svc.Condition[ConditionFlag.BetweenAreas51]
        && Player.Available
        && Player.Interactable;

    static bool _showedWarning, _work;
    static Exception _threadException;
    static DateTime _lastUpdatedWork = DateTime.Now;

    private unsafe static void FrameworkUpdate(IFramework framework)
    {
        if (!_showedWarning)
        {
            _showedWarning = true;
            ShowWarning();
        }

        PainterManager.ActionIds.Clear();
        RotationSolverPlugin.UpdateDisplayWindow();
        if (!IsValid)
        {
            TargetUpdater.ClearTarget();
            ActionUpdater.ClearNextAction();
            CustomRotation.MoveTarget = null;

            return;
        }

        try
        {
            PreviewUpdater.UpdatePreview();

            if (Service.Config.GetValue(PluginConfigBool.TeachingMode) && ActionUpdater.NextAction != null)
            {
                //Sprint action id is 3 however the id in hot bar is 4.
                var id = ActionUpdater.NextAction.AdjustedID;
                PainterManager.ActionIds.Add(id == (uint)ActionID.Sprint ? 4 : id);
            }

            if (DataCenter.IsActivated())
            {
                ActionUpdater.UpdateActionInfo();

                var canDoAction = ActionUpdater.CanDoAction();
                MovingUpdater.UpdateCanMove(canDoAction);
                if (canDoAction)
                {
                    RSCommands.DoAction();
                }

                MacroUpdater.UpdateMacro();
            }

            CloseWindow();
            OpenChest();
        }
        catch (Exception ex)
        {
            if (_threadException != ex)
            {
                _threadException = ex;
                Svc.Log.Error(ex, "Main Thread Exception");
            }
        }

        try
        {
            if (_work) return;
            if (DateTime.Now - _lastUpdatedWork < TimeSpan.FromSeconds(Service.Config.GetValue(PluginConfigFloat.MinUpdatingTime)))
                return;

            _work = true;
            _lastUpdatedWork = DateTime.Now;

            if (Service.Config.GetValue(PluginConfigBool.UseWorkTask))
            {
                Task.Run(UpdateWork);
            }
            else
            {
                UpdateWork();
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "Worker Exception");
        }
    }

    private static void ShowWarning()
    {
        if (!Svc.PluginInterface.InstalledPlugins.Any(p => p.InternalName == "Avarice"))
        {
            LocalizationManager.RightLang.AvariceWarning.ShowWarning(0);
        }
        if (!Svc.PluginInterface.InstalledPlugins.Any(p => p.InternalName == "TextToTalk"))
        {
            LocalizationManager.RightLang.TextToTalkWarning.ShowWarning(0);
        }
    }

    public static void Enable()
    {
        ActionSequencerUpdater.Enable(Svc.PluginInterface.ConfigDirectory.FullName + "\\Conditions");
        SocialUpdater.Enable();

        Svc.Framework.Update += FrameworkUpdate;
    }

    static Exception _innerException;
    private static void UpdateWork()
    {
        var waitingTime = (DateTime.Now - _lastUpdatedWork).TotalMilliseconds;
        if (waitingTime > 100)
        {
            Svc.Log.Warning($"The time for completing a running cycle for RS is {waitingTime:F2} ms, try disabling the option \"{LocalizationManager.RightLang.ConfigWindow_Param_UseWorkTask}\" to get better performance or check your other running plugins for one of them using too many resources and try disabling that.");
        }

        if (!IsValid)
        {
            ActionUpdater.NextAction = ActionUpdater.NextGCDAction = null;
            return;
        }

        try
        {
            if (Service.Config.GetValue(PluginConfigBool.AutoLoadCustomRotations))
            {
                RotationUpdater.LocalRotationWatcher();
            }

            RotationUpdater.UpdateRotation();

            if (DataCenter.IsActivated())
            {
                TargetUpdater.UpdateTarget();
                ActionSequencerUpdater.UpdateActionSequencerAction();
                ActionUpdater.UpdateNextAction();
            }

            RSCommands.UpdateRotationState();
            PainterManager.UpdateSettings();
        }
        catch (Exception ex)
        {
            if (_innerException != ex)
            {
                _innerException = ex;
                Svc.Log.Error(ex, "Inner Worker Exception");
            }
        }

        _work = false;
    }

    static DateTime _closeWindowTime = DateTime.Now;
    private unsafe static void CloseWindow()
    {
        if (_closeWindowTime < DateTime.Now) return;

        var needGreedWindow = Svc.GameGui.GetAddonByName("NeedGreed", 1);
        if (needGreedWindow == IntPtr.Zero) return;

        var notification = (AtkUnitBase*)Svc.GameGui.GetAddonByName("_Notification", 1);
        if (notification == null) return;

        var atkValues = (AtkValue*)Marshal.AllocHGlobal(2 * sizeof(AtkValue));
        atkValues[0].Type = atkValues[1].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int;
        atkValues[0].Int = 0;
        atkValues[1].Int = 2;
        try
        {
            notification->FireCallback(2, atkValues);
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "Failed to close the window!");
        }
        finally
        {
            Marshal.FreeHGlobal(new IntPtr(atkValues));
        }
    }

    static DateTime _nextOpenTime = DateTime.Now;
    static uint _lastChest = 0;
    private unsafe static void OpenChest()
    {
        if (!Service.Config.GetValue(PluginConfigBool.AutoOpenChest)) return;
        var player = Player.Object;

        var treasure = Svc.Objects.FirstOrDefault(o =>
        {
            if (o == null) return false;
            var dis = Vector3.Distance(player.Position, o.Position) - player.HitboxRadius - o.HitboxRadius;
            if (dis > 0.5f) return false;

            var address = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)(void*)o.Address;
            if ((ObjectKind)address->ObjectKind != ObjectKind.Treasure) return false;

            //Opened!
            foreach (var item in Loot.Instance()->ItemArraySpan)
            {
                if (item.ChestObjectId == o.ObjectId) return false;
            }

            return true;
        });

        if (treasure == null) return;
        if (DateTime.Now < _nextOpenTime) return;
        if (treasure.ObjectId == _lastChest && DateTime.Now - _nextOpenTime < TimeSpan.FromSeconds(10)) return;

        _nextOpenTime = DateTime.Now.AddSeconds(new Random().NextDouble() + 0.2);
        _lastChest = treasure.ObjectId;

        try
        {
            Svc.Targets.Target = treasure;

            TargetSystem.Instance()->InteractWithObject((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)(void*)treasure.Address);

            Notify.Plain($"Try to open the chest {treasure.Name}");
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "Failed to open the chest!");
        }

        if (!Service.Config.GetValue(PluginConfigBool.AutoCloseChestWindow)) return;
        _closeWindowTime = DateTime.Now.AddSeconds(0.5);
    }

    public static void Dispose()
    {
        Svc.Framework.Update -= FrameworkUpdate;
        PreviewUpdater.Dispose();
        ActionSequencerUpdater.SaveFiles();
        SocialUpdater.Disable();
        ActionUpdater.ClearNextAction();
    }
}
