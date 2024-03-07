using Dalamud.Game.ClientState.Party;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Logging;
using ECommons;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using PandorasBox.FeaturesSetup;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace PandorasBox.Features.UI;

public class AutoVoteMvp : Feature
{
    public override string Name => "[国服已优化] 自动点赞";

    public override string Description => "副本结束时自动点赞小队中对位的队友。";

    public override FeatureType FeatureType => FeatureType.UI;

    public override bool UseAutoConfig => false;

    private List<string> PremadePartyID { get; set; } = new();

    private List<uint> DeadPlayers { get; set; } = new();

    private Dictionary<uint, int> DeathTracker { get; set; } = new();

    public class Configs : FeatureConfig
    {
        public bool HideChat = false;

        public bool ExcludeDeaths = false;

        public int HowManyDeaths = 1;

        public bool ResetOnWipe = false;
    }

    public Configs Config { get; private set; }

    public override unsafe void Enable()
    {
        if (GameMain.Instance()->CurrentContentFinderConditionId != 0)
        {
            var payload = PandoraPayload.Payloads.ToList();
            payload.Add(new TextPayload(" [自动点赞] 请注意，由于此功能是在副本期间启用的，如果您在加入之前与队伍中的其他玩家一起匹配，则可能无法正常运行。"));
            Svc.Chat.Print(new SeString(payload));
        }
        Config = LoadConfig<Configs>() ?? new Configs();
        Svc.Framework.Update += FrameworkUpdate;
        Svc.Condition.ConditionChange += UpdatePartyCache;
        base.Enable();
    }

    private void UpdatePartyCache(Dalamud.Game.ClientState.Conditions.ConditionFlag flag, bool value)
    {
        if (Svc.Condition.Any())
        {
            if (flag == Dalamud.Game.ClientState.Conditions.ConditionFlag.WaitingForDuty && value)
            {
                foreach (var partyMember in Svc.Party)
                {
                    Svc.Log.Debug($"Adding {partyMember.Name.ExtractText()} {partyMember.ObjectId} to premade list");
                    PremadePartyID.Add(partyMember.Name.ExtractText());
                }

                var countRemaining =
                    Svc.Party.Where(i => i.ObjectId != Player.Object.ObjectId && i.GameObject != null && !PremadePartyID.Any(y => y == i.Name.ExtractText())).Count();

                Svc.Log.Debug($"Party has {countRemaining} available to commend.");
            }

            if (flag == Dalamud.Game.ClientState.Conditions.ConditionFlag.BoundByDuty && !value)
            {
                PremadePartyID.Clear();
            }
        }
    }

    public override void Disable()
    {
        SaveConfig(Config);
        Svc.Framework.Update -= FrameworkUpdate;
        Svc.Condition.ConditionChange -= UpdatePartyCache;
        base.Disable();
    }

    private unsafe void FrameworkUpdate(IFramework framework)
    {
        if (Player.Object == null) return;
        if (Svc.ClientState.IsPvP) return;
        CheckForDeadPartyMembers();

        var bannerWindow = (AtkUnitBase*)Svc.GameGui.GetAddonByName("BannerMIP", 1);
        if (bannerWindow == null) return;

        var agentBanner = AgentBannerMIP.Instance();

        try
        {
            VoteBanner(bannerWindow, ChoosePlayer());
        }
        catch (Exception e)
        {
            Svc.Log.Error(e, "Failed to vote!");
        }
    }

    private void CheckForDeadPartyMembers()
    {
        if (Svc.Party.Any())
        {
            if (Config.ResetOnWipe && Svc.Party.All(x => x.GameObject?.IsDead == true))
            {
                DeathTracker.Clear();
            }

            foreach (var pm in Svc.Party)
            {
                if (pm.GameObject == null) continue;
                if (pm.ObjectId == Svc.ClientState.LocalPlayer.ObjectId) continue;
                if (pm.GameObject.IsDead)
                {
                    if (DeadPlayers.Contains(pm.ObjectId)) continue;
                    DeadPlayers.Add(pm.ObjectId);
                    if (DeathTracker.ContainsKey(pm.ObjectId))
                        DeathTracker[pm.ObjectId] += 1;
                    else
                        DeathTracker.TryAdd(pm.ObjectId, 1);

                }
                else
                {
                    DeadPlayers.Remove(pm.ObjectId);
                }
            }
        }
        else
        {
            DeathTracker.Clear();
            DeadPlayers.Clear();
        }
    }

    private unsafe int ChoosePlayer()
    {
        var hud = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance()
            ->GetUiModule()->GetAgentModule()->GetAgentHUD();

        if (hud == null) throw new Exception("HUD is empty!");

        var list = Svc.Party.Where(i =>
        i.ObjectId != Player.Object.ObjectId && i.GameObject != null && !PremadePartyID.Any(y => y == i.Name.ExtractText()))
            .Select(PartyMember => (Math.Max(0, GetPartySlotIndex(PartyMember.ObjectId, hud) - 1), PartyMember))
            .ToList();

        if (!list.Any()) throw new Exception("No party members, skipping commend.");

        if (Config.ExcludeDeaths)
        {
            foreach (var deadPlayers in DeathTracker)
            {
                if (deadPlayers.Value >= Config.HowManyDeaths)
                {
                    list.RemoveAll(x => x.PartyMember.ObjectId == deadPlayers.Key);
                }
            }
        }

        var tanks = list.Where(i => i.PartyMember.ClassJob.GameData.Role == 1);
        var healer = list.Where(i => i.PartyMember.ClassJob.GameData.Role == 4);
        var dps = list.Where(i => i.PartyMember.ClassJob.GameData.Role is 2 or 3);
        var melee = list.Where(i => i.PartyMember.ClassJob.GameData.Role is 2);
        var range = list.Where(i => i.PartyMember.ClassJob.GameData.Role is 3);
        var myjob = Svc.ClientState.LocalPlayer.ClassJob.GameData.Role;

        (int index, PartyMember member) voteTarget = new();
        if (myjob == 1)
        {
            voteTarget = tanks.Any() ? tanks.FirstOrDefault() : healer.FirstOrDefault();
        }
        else if (myjob == 4)
        {
            voteTarget = healer.Any() ? healer.FirstOrDefault() : tanks.FirstOrDefault();
        }
        else if (myjob == 2)
        {
            voteTarget = melee.Any() ? melee.FirstOrDefault() : range.FirstOrDefault();
        }
        else
        {
            voteTarget = range.Any() ? range.FirstOrDefault() : melee.FirstOrDefault();
        }

        if (voteTarget.member == null) throw new Exception("No members! Can't vote!");

        if (!Config.HideChat)
        {
            var payload = PandoraPayload.Payloads.ToList();
            payload.AddRange(new List<Payload>()
            {
                new TextPayload("点赞给 "),
                voteTarget.member.ClassJob.GameData.Role switch
                {
                    1 => new IconPayload(BitmapFontIcon.Tank),
                    4 => new IconPayload(BitmapFontIcon.Healer),
                    _ => new IconPayload(BitmapFontIcon.DPS),
                },
                new PlayerPayload(voteTarget.member.Name.TextValue, voteTarget.member.World.GameData.RowId),
            });
            Svc.Chat.Print(new SeString(payload));
        }

        return voteTarget.index;
    }

    private static unsafe int GetPartySlotIndex(uint objectId, AgentHUD* hud)
    {
        var list = (HudPartyMember*)hud->PartyMemberList;
        for (var i = 0; i < hud->PartyMemberCount; i++)
        {
            if (list[i].ObjectId == objectId)
            {
                return i;
            }
        }

        return 0;
    }

    private static T RandomPick<T>(IEnumerable<T> list)
        => list.ElementAt(new Random().Next(list.Count() - 1));

    private static unsafe void VoteBanner(AtkUnitBase* bannerWindow, int index)
    {
        var atkValues = (AtkValue*)Marshal.AllocHGlobal(2 * sizeof(AtkValue));
        atkValues[0].Type = atkValues[1].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int;
        atkValues[0].Int = 12;
        atkValues[1].Int = index;
        try
        {
            bannerWindow->FireCallback(2, atkValues);
        }
        finally
        {
            Marshal.FreeHGlobal(new nint(atkValues));
        }
    }

    protected override DrawConfigDelegate DrawConfigTree => (ref bool _) =>
    {
        bool hasChanged = false;
        if (ImGui.Checkbox("不在聊天中显示投票结果", ref Config.HideChat))
            hasChanged = true;

        if (ImGui.Checkbox("排除死亡过的队友", ref Config.ExcludeDeaths))
            hasChanged = true;

        if (Config.ExcludeDeaths)
        {
            if (ImGuiEx.InputIntBounded("大于等于多少次不投他", ref Config.HowManyDeaths, 1, 100)) hasChanged = true;
            if (ImGui.Checkbox("团灭时重置死亡次数统计", ref Config.ResetOnWipe)) hasChanged = true;
        }

        if (hasChanged)
            SaveConfig(Config);
    };
}
