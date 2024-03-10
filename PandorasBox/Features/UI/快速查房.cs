using ECommons.Automation;
using ECommons.DalamudServices;
using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using PandorasBox.FeaturesSetup;
using PandorasBox.Helpers;
using PandorasBox.UI;
using System;
using System.Numerics;

namespace PandorasBox.Features.UI
{
    public unsafe class 快速查房 : Feature
    {
        public override string Name => "[国服限定] 一键扫房区";

        public override string Description => "一键扫房区，用于配合ACT房屋信息记录插件贡献数据。";

        public override FeatureType FeatureType => FeatureType.UI;

        internal Overlays window;
        private bool start = false;


        public override void Enable()
        {
            window = new(this);
            Svc.GameNetwork.NetworkMessage += GameNetwork_NetworkMessage;
            base.Enable();
        }

        private void GameNetwork_NetworkMessage(nint dataPtr, ushort opCode, uint sourceActorId, uint targetActorId, Dalamud.Game.Network.NetworkMessageDirection direction)
        {
            if (direction == Dalamud.Game.Network.NetworkMessageDirection.ZoneDown)
            {
                //PluginLog.Debug($"opCode: {opCode}");
                if (opCode == 370 && start)
                {
                    查房();
                }
            }
        }

        public override void Draw()
        {
            try
            {
                if (Svc.GameGui.GetAddonByName("HousingSelectBlock") != IntPtr.Zero)
                {
                    var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName("HousingSelectBlock");
                    var node = addon->UldManager.NodeList[18];
                    if (node == null)
                        return;

                    var position = AtkResNodeHelper.GetNodePosition(node);
                    var scale = AtkResNodeHelper.GetNodeScale(node);
                    var size = new Vector2(node->Width, node->Height) * scale;

                    ImGuiHelpers.ForceNextWindowMainViewport();
                    ImGuiHelpers.SetNextWindowPosRelativeMainViewport(new Vector2(position.X, position.Y - size.Y));

                    ImGui.PushStyleColor(ImGuiCol.WindowBg, 0);
                    var oldSize = ImGui.GetFont().Scale;
                    ImGui.GetFont().Scale *= scale.X;
                    ImGui.PushFont(ImGui.GetFont());
                    ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f.Scale());
                    ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0f.Scale(), 0f.Scale()));
                    ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0f.Scale(), 0f.Scale()));
                    ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f.Scale());
                    ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, size);
                    ImGui.Begin($"###查房{node->NodeID}", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoNavFocus
                        | ImGuiWindowFlags.AlwaysUseWindowPadding | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoSavedSettings);
                    if (ImGui.Button("快速扫房区", size))
                    {
                        start = true;
                        i = 1;
                        查房();
                    }

                    ImGui.End();
                    ImGui.PopStyleVar(5);
                    ImGui.GetFont().Scale = oldSize;
                    ImGui.PopFont();
                    ImGui.PopStyleColor();
                }
            }
            catch
            {

            }
        }

        private int i = 1;
        private void 查房()
        {
            var n = i;
            if (Svc.GameGui.GetAddonByName("HousingSelectBlock") != IntPtr.Zero)
            {
                var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName("HousingSelectBlock");
                if (n < 30)
                {
                    TaskManager.Enqueue(() => Callback.Fire(addon, false, 1, n));
                    Svc.Chat.Print($"已查询{n + 1} 号房区");
                    i++;
                }
                else
                {
                    start = false;
                }
            }
        }


        public override void Disable()
        {
            P.Ws.RemoveWindow(window);
            Svc.GameNetwork.NetworkMessage -= GameNetwork_NetworkMessage;
            base.Disable();
        }
    }
}
