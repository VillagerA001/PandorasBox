using Dalamud.Interface.Internal.Notifications;
using Dalamud.Memory;
using ECommons.Automation;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;
using PandorasBox.FeaturesSetup;
using System;
using System.Linq;

namespace PandorasBox.Features.Other
{
    public unsafe class AutoLogin : Feature
    {
        public override string Name => "[国服限定] 自动登录";

        public override string Description => "启动游戏后自动登录。";

        public override bool UseAutoConfig => false;

        private bool logging = false;

        public class Configs : FeatureConfig
        {
            public uint DataCenter = 0;
            public uint World = 0;
            public uint CharacterSlot = 0;
        }

        public Configs Config { get; private set; }

        public override FeatureType FeatureType => FeatureType.Other;

        private void CheckLogin(IFramework framework)
        {
            if (Svc.ClientState.IsLoggedIn)
            {
                logging = false;
                return;
            }

            if (Svc.KeyState[Dalamud.Game.ClientState.Keys.VirtualKey.SHIFT] && logging)
            {
                logging = false;
                TaskManager.Abort();
                Svc.PluginInterface.UiBuilder.AddNotification("自动登录已取消", "Pandoras", NotificationType.Warning);
                return;
            }
        }

        public bool CheckTitle()
        {
            var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName("_TitleMenu");
            return addon != null && addon->IsVisible;
        }

        public static bool Message()
        {
            Svc.PluginInterface.UiBuilder.AddNotification("开始自动登录,按Shift键中止", "Pandoras", NotificationType.Info);
            return true;
        }

        public bool ClickStart()
        {
            var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName("_TitleMenu");
            if (addon == null)
                return false;
            if (!addon->IsVisible)
                return false;
            Callback.Fire(addon, true, 1);
            return true;
        }

        public bool SelectWorld()
        {
            var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName("_CharaSelectWorldServer");
            if (addon == null)
                return false;
            var stringArray = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance()->UIModule->GetRaptureAtkModule()->AtkModule.AtkArrayDataHolder.StringArrays[1];
            if (stringArray == null)
                return false;

            var world = Svc.Data.Excel.GetSheet<World>()?.GetRow(Config.World);
            if (world is not { IsPublic: true })
                return false;

            for (var i = 0; i < 16; i++)
            {
                var n = stringArray->StringArray[i];
                if (n == null)
                    continue;
                var s = MemoryHelper.ReadStringNullTerminated(new IntPtr(n));
                if (s.Trim().Length == 0)
                    continue;
                if (s != world.Name.RawString)
                    continue;
                Callback.Fire(addon, true, 9, 0, i);
                return true;
            }
            return false;
        }

        public bool SelectCharacter()
        {
            var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName("_CharaSelectListMenu");
            if (addon == null)
                return false;
            Callback.Fire(addon, true, 18, 0, Config.CharacterSlot);
            var nextAddon = (AtkUnitBase*)Svc.GameGui.GetAddonByName("SelectYesno");
            return nextAddon != null;
        }

        public bool SelectYes()
        {
            var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName("SelectYesno");
            if (addon == null)
                return false;
            Callback.Fire(addon, true, 0);
            return true;
        }

        protected override DrawConfigDelegate DrawConfigTree => (ref bool _) =>
        {
            if (ImGui.BeginCombo("大区", Config.DataCenter == 0 ? "未选择" : Svc.Data.Excel.GetSheet<WorldDCGroupType>().GetRow(Config.DataCenter).Name.RawString))
            {
                foreach (var dc in Svc.Data.Excel.GetSheet<WorldDCGroupType>().Where(w => w.Region == 5 && w.Name.RawString.Trim().Length > 0))
                {
                    if (ImGui.Selectable(dc.Name.RawString, dc.RowId == Config.DataCenter))
                    {
                        Config.DataCenter = dc.RowId;
                        SaveConfig(Config);
                    }
                }
                ImGui.EndCombo();
            }
            if (Svc.Data.Excel.GetSheet<WorldDCGroupType>().GetRow(Config.DataCenter).Region != 0)
            {
                if (ImGui.BeginCombo("服务器", Config.World == 0 ? "未选择" : Svc.Data.Excel.GetSheet<World>().GetRow(Config.World).Name.RawString))
                {
                    foreach (var w in Svc.Data.Excel.GetSheet<World>().Where(w => w.DataCenter.Row == Config.DataCenter && w.IsPublic))
                    {
                        if (ImGui.Selectable(w.Name.RawString, w.RowId == Config.World))
                        {
                            Config.World = w.RowId;
                            SaveConfig(Config);
                        }
                    }
                    ImGui.EndCombo();
                }
            }

            if (Svc.Data.Excel.GetSheet<World>().GetRow(Config.World).IsPublic)
            {
                if (ImGui.BeginCombo("角色", $"角色#{Config.CharacterSlot + 1}"))
                {
                    for (uint i = 0; i < 8; i++)
                    {
                        if (ImGui.Selectable($"角色#{i + 1}", Config.CharacterSlot == i))
                        {
                            Config.CharacterSlot = i;
                            SaveConfig(Config);
                        }
                    }
                    ImGui.EndCombo();
                }
            }
        };

        public override void Enable()
        {
            Config = LoadConfig<Configs>() ?? new Configs();

            TaskManager.Enqueue(() => CheckTitle(), int.MaxValue, "CheckTitle");
            TaskManager.Enqueue(() => ClickStart(), true, "ClickStart");
            TaskManager.Enqueue(() => Message(), true, "Message");
            TaskManager.Enqueue(() => SelectWorld(), true, "SelectWorld");
            TaskManager.DelayNext(500);
            TaskManager.Enqueue(() => SelectCharacter(), true, "SelectCharacter");
            TaskManager.Enqueue(() => SelectYes(), true, "SelectYes");
            logging = true;

            base.Enable();
        }

        public override void Disable()
        {
            SaveConfig(Config);
            Svc.Framework.Update -= CheckLogin;
            logging = false;
            base.Disable();
        }
    }
}
