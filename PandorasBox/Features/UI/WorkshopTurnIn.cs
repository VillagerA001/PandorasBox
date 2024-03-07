using ClickLib.Clicks;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Utility;
using ECommons;
using ECommons.Automation;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using ECommons.Logging;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Common.Math;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;
using PandorasBox.FeaturesSetup;
using PandorasBox.Helpers;
using PandorasBox.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using static ECommons.GenericHelpers;

namespace PandorasBox.Features.UI
{
    public unsafe class WorkshopTurnin : Feature
    {
        public override string Name => "部队工房提交";

        public override string Description => "将当前阶段和整个项目中用于自动提交的按钮添加到工房菜单中。";

        public override FeatureType FeatureType => FeatureType.UI;

        private Overlays overlay;
        private float height;

        internal static bool active = false;
        internal bool phaseActive = false;
        internal bool projectActive = false;
        internal bool partLoopActive = false;
        internal bool needToDisableTurnIns = false;
        internal bool needToDisableConfig = false;
        private static readonly string[] SkipCutsceneStr = { "Skip cutscene?", "要跳过这段过场动画吗？", "要跳過這段過場動畫嗎？", "Videosequenz überspringen?", "Passer la scène cinématique ?", "このカットシーンをスキップしますか？" };
        private static readonly string[] ContributeMaterialsStr = { "Contribute materials.", "交纳素材", "Materialien abliefern", "Fournir des matériaux", "素材を納品する" }; // 2
        private static readonly string[] AdvancePhaseStr = { "Advance to the next phase of production.", "推进工程进展", "Arbeitsschritt ausführen", "Faire progresser un projet de con", "作業工程を進捗させる" }; // 5
        private static readonly string[] CompleteConstructionStr = { "Complete the construction", "完成", "Herstellung", "Terminer la con", "を完成させる" }; // 6
        private static readonly string[] CollectProductStr = { "Collect finished product.", "领取道具", "Produkt entgegennehmen", "Récupérer un projet terminé", "アイテムを受け取る" }; // 4
        private static readonly string[] LeaveWorkshopStr = { "Nothing.", "取消", "Nichts", "Annuler", "やめる" }; // 7
        private static readonly string[] ConfirmContributionStr = { "to the company project?", "确定要为合建设备提供", "schaftsprojekt bereitstellen?", "pour le projet de con", "カンパニー製作設備に納品します。" };
        private static readonly string[] ConfirmProductRetrievalStr = { "Retrieve", "回收", "entnehmen", "Récupérer", "を回収します。" };

        public Configs Config { get; private set; }
        public class Configs : FeatureConfig
        {
            [FeatureConfigOption("循环次数", "", 1, IntMin = 0, IntMax = 100, EditorSize = 300)]
            public int partsToBuild = 1;
        }

        public override void Enable()
        {
            Config = LoadConfig<Configs>() ?? new Configs();
            overlay = new Overlays(this);
            base.Enable();
        }

        public override void Disable()
        {
            SaveConfig(Config);
            P.Ws.RemoveWindow(overlay);
            base.Disable();
        }

        public readonly struct PartIngredient(Item ingredient, uint reqLevel, ClassJob reqJob, uint inventory, uint perturn, uint total, uint timesSoFar, uint timesTotal)
        {
            public Item Ingredient { get; } = ingredient;
            public uint RequiredLevelToTurnIn { get; } = reqLevel;
            public ClassJob RequiredJobToTurnIn { get; } = reqJob;
            public uint AmountInInventory { get; } = inventory;
            public uint AmountPerTurnIn { get; } = perturn;
            public uint TotalRequiredAmount { get; } = total;
            public uint TurnedInSoFar { get; } = timesSoFar;
            public uint TotalTimesToTurnIn { get; } = timesTotal;
        }

        public override void Draw()
        {
            if (TryGetAddonByName<AtkUnitBase>("SubmarinePartsMenu", out var addon))
            {
                if (!(addon->UldManager.NodeListCount > 1)) return;
                if (addon->UldManager.NodeListCount < 38) return;
                if (!addon->UldManager.NodeList[1]->IsVisible) return;

                var node = addon->UldManager.NodeList[1];

                if (!node->IsVisible)
                    return;

                var position = AtkResNodeHelper.GetNodePosition(node);

                ImGuiHelpers.ForceNextWindowMainViewport();
                ImGuiHelpers.SetNextWindowPosRelativeMainViewport(new Vector2(addon->X, addon->Y - height));

                ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(7f, 7f));
                ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, new Vector2(10f, 10f));
                ImGui.Begin($"###LoopButtons{node->NodeID}", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoNavFocus
                | ImGuiWindowFlags.AlwaysUseWindowPadding | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoSavedSettings);

                if (active && !phaseActive) ImGui.BeginDisabled();

                if (ImGui.Button(!phaseActive ? $"阶段提交###StartPhaseLooping" : $"提交中... 点击取消###AbortPhaseLoop"))
                {
                    if (!phaseActive)
                    {
                        phaseActive = true;
                        TurnInPhase();
                    }
                    else
                    {
                        EndLoop("用户取消");
                    }
                }

                if (active && !phaseActive) ImGui.EndDisabled();

                ImGui.SameLine();

                if (active && !projectActive) ImGui.BeginDisabled();

                if (ImGui.Button(!projectActive ? $"项目提交###StartProjectLooping" : $"提交中... 点击取消###AbortProjectLoop"))
                {
                    if (!projectActive)
                    {
                        projectActive = true;
                        TurnInProject();
                    }
                    else
                    {
                        EndLoop("用户取消");
                    }
                }

                if (active && !projectActive) ImGui.EndDisabled();

                active = phaseActive || projectActive || partLoopActive;

                height = ImGui.GetWindowSize().Y;

                ImGui.End();
                ImGui.PopStyleVar(2);
            }
        }


        private bool MustEndLoop(bool condition, string message)
        {
            if (condition)
            {
                PrintModuleMessage(message);
                EndLoop(message);
            }
            return condition;
        }

        private bool EndLoop(string msg)
        {
            Svc.Log.Debug($"取消中... 原因： {msg}");
            active = false;
            phaseActive = false;
            projectActive = false;
            partLoopActive = false;

            YesAlready.Unlock();

            if (needToDisableTurnIns)
                FeatureHelper.DisableFeature<AutoSelectTurnin>();

            if (needToDisableConfig)
                FeatureHelper.GetConfig<AutoSelectTurnin>().ToggleConfig("AutoConfirm", false);

            TaskManager.Abort();

            return true;
        }

        private bool TurnInPhase()
        {
            YesAlready.Lock();
            needToDisableConfig = false;
            needToDisableTurnIns = false;

            if (!FeatureHelper.IsEnabled<AutoSelectTurnin>())
            {
                needToDisableTurnIns = true;
                FeatureHelper.EnableFeature<AutoSelectTurnin>();
            }

            if (!FeatureHelper.GetConfig<AutoSelectTurnin>().IsEnabled("AutoConfirm").Value)
            {
                needToDisableConfig = true;
                FeatureHelper.GetConfig<AutoSelectTurnin>().ToggleConfig("AutoConfirm", true);
            }

            if (TryGetAddonByName<AtkUnitBase>("SubmarinePartsMenu", out var addon) && addon->AtkValues[12].Type != 0)
            {
                var requiredIngredients = GetRequiredItems();
                if (requiredIngredients?.Count == 0) { Svc.Log.Debug("req is 0"); return true; }

                if (MustEndLoop(!IsSufficientlyLeveled(requiredIngredients), "等级不足，无法提交道具。") ||
                    MustEndLoop(Svc.ClientState.LocalPlayer.ClassJob.Id is < 8 or > 15, "必须切换至能工巧匠才能提交道具。") ||
                    MustEndLoop(requiredIngredients.All(x => x.AmountInInventory < x.AmountPerTurnIn), "没有道具可以提交。"))
                {
                    YesAlready.Unlock();

                    if (needToDisableTurnIns)
                        FeatureHelper.DisableFeature<AutoSelectTurnin>();

                    if (needToDisableConfig)
                        FeatureHelper.GetConfig<AutoSelectTurnin>().ToggleConfig("AutoConfirm", false);
                    return true;
                }

                foreach (var ingredient in requiredIngredients)
                {
                    if (ingredient.AmountPerTurnIn > ingredient.AmountInInventory) continue;

                    for (var i = ingredient.TurnedInSoFar; i < ingredient.TotalTimesToTurnIn; i++)
                    {
                        TaskManager.EnqueueImmediate(() => ClickItem(requiredIngredients.IndexOf(ingredient), ingredient.AmountPerTurnIn), $"{nameof(ClickItem)} {ingredient.Ingredient.Name}");
                        TaskManager.DelayNextImmediate(300);
                        TaskManager.EnqueueImmediate(() => ConfirmHQTrade(), 300, $"{nameof(ConfirmHQTrade)}");
                        TaskManager.DelayNextImmediate(300);
                        TaskManager.EnqueueImmediate(() => ConfirmContribution(), $"{nameof(ConfirmContribution)}");
                        TaskManager.DelayNextImmediate(300);
                    }
                }

                var hasMorePhases = addon->AtkValues[6].UInt != addon->AtkValues[7].UInt - 1;
                TaskManager.EnqueueImmediate(!hasMorePhases ? CompleteConstruction : AdvancePhase);
                TaskManager.EnqueueImmediate(WaitForCutscene, 2000, $"{nameof(WaitForCutscene)}");
                TaskManager.EnqueueImmediate(PressEsc, 1000, $"{nameof(PressEsc)}");
                TaskManager.EnqueueImmediate(ConfirmSkip, 1000, $"{nameof(ConfirmSkip)}");
                TaskManager.EnqueueImmediate(() =>
                {
                    YesAlready.Unlock();

                    if (needToDisableTurnIns)
                        FeatureHelper.DisableFeature<AutoSelectTurnin>();

                    if (needToDisableConfig)
                        FeatureHelper.GetConfig<AutoSelectTurnin>().ToggleConfig("AutoConfirm", false);
                });

                if (phaseActive) TaskManager.EnqueueImmediate(() => EndLoop("Phase Complete"));
                return true;
            }
            else
            {
                YesAlready.Unlock();
                return false;
            }
        }

        private bool TurnInProject()
        {
            if (TryGetAddonByName<AtkUnitBase>("SubmarinePartsMenu", out var addon))
            {
                for (var i = addon->AtkValues[6].UInt; i < addon->AtkValues[7].UInt; i++)
                {
                    var hasMorePhases = i != addon->AtkValues[7].UInt - 1;
                    TaskManager.Enqueue(() => TurnInPhase(), $"{nameof(TurnInPhase)} {i}");
                    TaskManager.Enqueue(InteractWithFabricationPanel, $"{nameof(InteractWithFabricationPanel)}");
                    if (hasMorePhases)
                        TaskManager.Enqueue(ContributeMaterials, $"{nameof(ContributeMaterials)}");
                    else
                    {
                        TaskManager.Enqueue(CollectProduct, $"{nameof(CollectProduct)}");
                        TaskManager.Enqueue(() => ConfirmProductRetrieval(), $"{nameof(ConfirmProductRetrieval)}");
                        TaskManager.Enqueue(LeaveWorkshop, $"{nameof(LeaveWorkshop)}");
                    }
                }
            }
            else
            {
                EndLoop("无法找到SubmarinePartsMenu");
                return true;
            }

            if (projectActive) TaskManager.Enqueue(() => EndLoop($"完成 {nameof(TurnInProject)}"));
            return true;
        }

        private List<PartIngredient> GetRequiredItems()
        {
            // 12-23 item ids
            // 36-47 names
            // 60-71 amount per turn in (uint)
            // 72-83 amount in inventory (uint)
            // 84-95 amount in inventory, nq/hq split (string)
            // 108-119 times turned in so far for phase (uint)
            // 120-131 times to turn in for the phase (uint)
            // 144-156 level requirement to turn in part (uint)
            if (!TryGetAddonByName<AtkUnitBase>("SubmarinePartsMenu", out var addon))
            {
                EndLoop("无法找到SubmarinePartsMenu");
                return null;
            }

            return Enumerable.Range(0, 12)
                .Where(i => addon->AtkValues[36 + i].Type != 0)
                .Select(i => new PartIngredient(
                    Svc.Data.GetExcelSheet<Item>(Svc.ClientState.ClientLanguage).GetRow(addon->AtkValues[12 + i].UInt),
                    addon->AtkValues[144 + i].UInt,
                    Svc.Data.GetExcelSheet<ClassJob>(Svc.ClientState.ClientLanguage).FirstOrDefault(x => x.RowId == addon->AtkValues[48 + i].Int - 62000),
                    addon->AtkValues[72 + i].UInt,
                    addon->AtkValues[60 + i].UInt,
                    addon->AtkValues[60 + i].UInt * addon->AtkValues[120 + i].UInt,
                    addon->AtkValues[108 + i].UInt,
                    addon->AtkValues[120 + i].UInt
                )).ToList();
        }


        private static bool IsSufficientlyLeveled(List<PartIngredient> requiredIngredients)
        {
            foreach (var i in requiredIngredients)
                if (PlayerState.Instance()->ClassJobLevelArray[i.RequiredJobToTurnIn.ExpArrayIndex] < i.RequiredLevelToTurnIn)
                    return false;

            return true;
        }

        private bool ClickItem(int positionInList, uint turnInAmount)
        {
            // I cannot detect if you have the right amount of items but not within a sufficiently large single stack
            if (TryGetAddonByName<AtkUnitBase>("Request", out var requestAddon) && requestAddon->IsVisible) return false;

            if (TryGetAddonByName<AtkUnitBase>("SubmarinePartsMenu", out var addon))
            {
                Callback.Fire(addon, false, 0, (uint)positionInList, turnInAmount);
                return TryGetAddonByName<AtkUnitBase>("Request", out var rAddon) && rAddon->IsVisible;
            }
            return false;
        }

        private static bool ConfirmContribution() =>
            ConfirmContributionStr.Any(str =>
            {
                var x = GetSpecificYesno((s) => s.ContainsAny(StringComparison.OrdinalIgnoreCase, str));
                if (x != null)
                {
                    ClickSelectYesNo.Using((nint)x).Yes();
                    return true;
                }
                return false;
            });

        private static bool ConfirmHQTrade()
        {
            var x = GetSpecificYesno(Svc.Data.GetExcelSheet<Addon>().GetRow(102434).Text);
            if (x != null)
            {
                ClickSelectYesNo.Using((nint)x).Yes();
                return true;
            }
            return false;
        }

        private static bool ConfirmProductRetrieval() =>
            ConfirmProductRetrievalStr.Any(str =>
            {
                var x = GetSpecificYesno((s) => s.ContainsAny(StringComparison.OrdinalIgnoreCase, str));
                if (x != null)
                {
                    ClickSelectYesNo.Using((nint)x).Yes();
                    return true;
                }
                return false;
            });



        private static bool? ContributeMaterials() =>
            ContributeMaterialsStr.Any(str => TrySelectSpecificEntry(str, () => GenericThrottle && EzThrottler.Throttle($"{nameof(WorkshopTurnin)}.{nameof(ContributeMaterials)}", 1000)));

        private static bool? AdvancePhase() =>
            AdvancePhaseStr.Any(str => TrySelectSpecificEntry(str, () => GenericThrottle && EzThrottler.Throttle($"{nameof(WorkshopTurnin)}.{nameof(AdvancePhase)}", 1000)));

        private static bool? CompleteConstruction() =>
            CompleteConstructionStr.Any(str => TrySelectSpecificEntry(str, () => GenericThrottle && EzThrottler.Throttle($"{nameof(WorkshopTurnin)}.{nameof(CompleteConstruction)}", 1000)));

        private static bool? CollectProduct() =>
            CollectProductStr.Any(str => TrySelectSpecificEntry(str, () => GenericThrottle && EzThrottler.Throttle($"{nameof(WorkshopTurnin)}.{nameof(CollectProduct)}", 1000)));

        private static bool? LeaveWorkshop() =>
            LeaveWorkshopStr.Any(str => TrySelectSpecificEntry(str, () => GenericThrottle && EzThrottler.Throttle($"{nameof(WorkshopTurnin)}.{nameof(CollectProduct)}", 1000)));

        private static bool? WaitForCutscene() =>
            Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent] || Svc.Condition[ConditionFlag.WatchingCutscene78];

        private static bool? PressEsc()
        {
            var nLoading = Svc.GameGui.GetAddonByName("NowLoading", 1);
            if (nLoading != nint.Zero)
            {
                var nowLoading = (AtkUnitBase*)nLoading;
                if (nowLoading->IsVisible)
                {
                    //pi.Framework.Gui.Chat.Print(Environment.TickCount + " Now loading visible");
                }
                else
                {
                    //pi.Framework.Gui.Chat.Print(Environment.TickCount + " Now loading not visible");
                    if (WindowsKeypress.SendKeypress(Keys.Escape))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool? ConfirmSkip()
        {
            var addon = Svc.GameGui.GetAddonByName("SelectString", 1);
            if (addon == nint.Zero) return false;
            var selectStrAddon = (AddonSelectString*)addon;
            if (!IsAddonReady(&selectStrAddon->AtkUnitBase))
            {
                return false;
            }
            if (!SkipCutsceneStr.Contains(selectStrAddon->AtkUnitBase.UldManager.NodeList[3]->GetAsAtkTextNode()->NodeText.ToString())) return false;
            if (EzThrottler.Throttle($"{nameof(WorkshopTurnin)}.{nameof(ConfirmSkip)}"))
            {
                Svc.Log.Debug("Selecting cutscene skipping");
                ClickSelectString.Using(addon).SelectItem(0);
                return true;
            }
            return false;
        }

        internal static bool TryGetNearestFabricationPanel(out Dalamud.Game.ClientState.Objects.Types.GameObject obj) =>
            Svc.Objects.TryGetFirst(x => x.Name.ToString().EqualsIgnoreCaseAny(Svc.Data.GetExcelSheet<EObjName>(Svc.ClientState.ClientLanguage).GetRow(2005).Singular.RawString) && x.IsTargetable, out obj);

        internal static bool? InteractWithFabricationPanel()
        {
            if (IsLoading()) return false;

            if (TryGetNearestFabricationPanel(out var obj))
            {
                if (Svc.Targets.Target?.Address == obj.Address)
                {
                    if (GenericThrottle && EzThrottler.Throttle($"{nameof(WorkshopTurnin)}.{nameof(InteractWithFabricationPanel)}", 2000))
                    {
                        TargetSystem.Instance()->InteractWithObject(obj.Struct(), false);
                        return true;
                    }
                }
                else
                {
                    if (obj.IsTargetable && GenericThrottle)
                    {
                        Svc.Targets.Target = obj;
                    }
                }
            }
            return false;
        }
    }
}
