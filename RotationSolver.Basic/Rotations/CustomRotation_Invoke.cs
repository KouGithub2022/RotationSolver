namespace RotationSolver.Basic.Rotations;

public abstract partial class CustomRotation
{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public bool TryInvoke(out IAction newAction, out IAction gcdAction)
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
    {
        newAction = gcdAction = null;
        if (!IsEnabled)
        {
            return false;
        }

        //if(DataCenter.Territory?.IsPvpZone ?? false)
        //{
        //    if (!Type.HasFlag(CombatType.PvP)) return false;
        //}
        //else
        //{
        //    if (!Type.HasFlag(CombatType.PvE)) return false;
        //}

        try
        {
            UpdateInfo();
            UpdateActions(ClassJob.GetJobRole());
            BaseAction.CleanSpecial();
            if (Player.HasStatus(true, StatusID.PvP_Guard)) return false;

            CountingOfLastUsing = CountingOfCombatTimeUsing = 0;
            newAction = Invoke(out gcdAction);
            if (InCombat || CountOfTracking == 0)
            {
                AverageCountOfLastUsing =
                    (AverageCountOfLastUsing * CountOfTracking + CountingOfLastUsing)
                    / ++CountOfTracking;
                MaxCountOfLastUsing = Math.Max(MaxCountOfLastUsing, CountingOfLastUsing);

                AverageCountOfCombatTimeUsing =
                    (AverageCountOfCombatTimeUsing * (CountOfTracking - 1) + CountingOfCombatTimeUsing)
                    / CountOfTracking;
                MaxCountOfCombatTimeUsing = Math.Max(MaxCountOfCombatTimeUsing, CountingOfCombatTimeUsing);
            }

            if (!IsValid) IsValid = true;
        }
        catch (Exception ex)
        {
            WhyNotValid = $"Failed to invoke the next action. Please contact the rotation author \"{{0}}\" on our discord.";

            while (ex != null)
            {
                if (!string.IsNullOrEmpty(ex.Message)) WhyNotValid += "\n" + ex.Message;
                if (!string.IsNullOrEmpty(ex.StackTrace)) WhyNotValid += "\n" + ex.StackTrace;
                ex = ex.InnerException;
            }
            IsValid = false;
        }

        return newAction != null;
    }

    private void UpdateActions(JobRole role)
    {
        BaseAction.OtherOption = CanUseOption.IgnoreTarget;

        ActionMoveForwardGCD = MoveForwardGCD(out var act) ? act : null;

        if (!DataCenter.HPNotFull && role == JobRole.Healer)
        {
            ActionHealAreaGCD = ActionHealAreaAbility = ActionHealSingleGCD = ActionHealSingleAbility = null;
        }
        else
        {
            ActionHealAreaGCD = HealAreaGCD(out act) ? act : null;
            ActionHealSingleGCD = HealSingleGCD(out act) ? act : null;

            BaseAction.OtherOption |= CanUseOption.IgnoreClippingCheck;

            ActionHealAreaAbility = HealAreaAbility(out act) ? act : null;
            ActionHealSingleAbility = HealSingleAbility(out act) ? act : null;

            BaseAction.OtherOption &= ~CanUseOption.IgnoreClippingCheck;
        }

        ActionDefenseAreaGCD = DefenseAreaGCD(out act, Array.Empty<BattleChara>()) ? act : null;

        ActionDefenseSingleGCD = DefenseSingleGCD(out act, Array.Empty<BattleChara>()) ? act : null;

        EsunaStanceNorthGCD = role switch
        {
            JobRole.Healer => DataCenter.WeakenPeople.Any() && EsunaAction(out act, CanUseOption.MustUse) ? act : null,
            _ => null,
        };

        RaiseShirkGCD = role switch
        {
            JobRole.Healer => DataCenter.DeathPeopleAll.Any() && RaiseAction(out act) ? act : null,
            _ => null,
        };

        BaseAction.OtherOption |= CanUseOption.IgnoreClippingCheck;

        ActionDefenseAreaAbility = DefenseAreaAbility(out act, Array.Empty<BattleChara>()) ? act : null;

        ActionDefenseSingleAbility = DefenseSingleAbility(out act, Array.Empty<BattleChara>()) ? act : null;

        EsunaStanceNorthAbility = role switch
        {
            JobRole.Melee => TrueNorth.CanUse(out act) ? act : null,
            JobRole.Tank => TankStance.CanUse(out act) ? act : null,
            _ => null,
        };

        RaiseShirkAbility = role switch
        {
            JobRole.Tank => Shirk.CanUse(out act) ? act : null,
            _ => null,
        };
        AntiKnockbackAbility = AntiKnockback(role, out act) ? act : null;

        BaseAction.OtherOption |= CanUseOption.EmptyOrSkipCombo;

        var movingTarget = MoveForwardAbility(out act);
        ActionMoveForwardAbility = movingTarget ? act : null;

        if (movingTarget && act is IBaseAction a)
        {
            if (a.Target == null || a.Target == Player)
            {
                if ((ActionID)a.ID == ActionID.EnAvant)
                {
                    var dir = new Vector3(MathF.Sin(Player.Rotation), 0, MathF.Cos(Player.Rotation));
                    MoveTarget = Player.Position + dir * 10;
                }
                else
                {
                    MoveTarget = a.Position == a.Target.Position ? null : a.Position;
                }
            }
            else
            {
                var dir = Player.Position - a.Target.Position;
                var length = dir.Length();
                if (length != 0)
                {
                    dir /= length;

                    MoveTarget = a.Target.Position + dir * MathF.Min(length, Player.HitboxRadius + a.Target.HitboxRadius);
                }
                else
                {
                    MoveTarget = a.Target.Position;
                }
            }
        }
        else
        {
            MoveTarget = null;
        }

        ActionMoveBackAbility = MoveBackAbility(out act) ? act : null;
        ActionSpeedAbility = SpeedAbility(out act) ? act : null;

        BaseAction.OtherOption = CanUseOption.None;
    }

    private IAction Invoke(out IAction gcdAction)
    {
        var countDown = Service.CountDownTime;
        if (countDown > 0)
        {
            gcdAction = null;
            return CountDownAction(countDown);
        }

        var hostilesCastingAOE = Service.Config.GetValue(Configuration.PluginConfigBool.UseDefenseAbility)
            ? DataCenter.HostileTargetsCastingAOE.AsEnumerable()
            : Array.Empty<BattleChara>();

        IEnumerable<BattleChara> hostilesCastingST = Array.Empty<BattleChara>();
        if (ClassJob.GetJobRole() == JobRole.Healer || ClassJob.RowId == (uint)ECommons.ExcelServices.Job.PLD)
        {
            hostilesCastingST = DataCenter.HostileTargetsCastingToTank.IntersectBy(
                DataCenter.PartyTanks.Select(t => (ulong)t.ObjectId),
                t => t.TargetObjectId
            );
        }

        BaseAction.OtherOption = CanUseOption.IgnoreClippingCheck;
        gcdAction = GCD(hostilesCastingAOE, hostilesCastingST);
        BaseAction.OtherOption = CanUseOption.None;

        if (gcdAction != null)
        {
            if (DataCenter.NextAbilityToNextGCD < DataCenter.MinAnimationLock + DataCenter.Ping
                || DataCenter.WeaponTotal < DataCenter.CastingTotal) return gcdAction;

            if (Ability(gcdAction, out IAction ability, hostilesCastingAOE, hostilesCastingST)) return ability;

            return gcdAction;
        }
        else
        {
            BaseAction.OtherOption = CanUseOption.IgnoreClippingCheck;
            if (Ability(Addle, out IAction ability, hostilesCastingAOE, hostilesCastingST)) return ability;
            BaseAction.OtherOption = CanUseOption.None;

            return null;
        }
    }

    /// <summary>
    /// The action in countdown.
    /// </summary>
    /// <param name="remainTime"></param>
    /// <returns></returns>
    protected virtual IAction CountDownAction(float remainTime) => null;
}
