using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using ECommons;
using ECommons.Automation;
using ECommons.DalamudServices;
using ImGuiNET;
using PandorasBox.Features;
using PandorasBox.FeaturesSetup;
using PandorasBox.IPC;
using PandorasBox.UI;
using PunishLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace PandorasBox;

public class PandorasBox : IDalamudPlugin
{
    public string Name => "Pandora's Box";
    private const string CommandName = "/pandora";
    internal WindowSystem Ws;
    internal MainWindow MainWindow;

    internal static PandorasBox P;
    internal static DalamudPluginInterface pi;
    internal static Configuration Config;

    public List<FeatureProvider> FeatureProviders = new();
    private FeatureProvider provider;
    public IEnumerable<BaseFeature> Features => FeatureProviders.Where(x => !x.Disposed).SelectMany(x => x.Features).OrderBy(x => x.Name);
    internal TaskManager TaskManager;

    public PandorasBox(DalamudPluginInterface pluginInterface)
    {
        P = this;
        pi = pluginInterface;
        Initialize();
    }

    private bool isDev = false;
    private void Initialize()
    {
        ECommonsMain.Init(pi, P, ECommons.Module.DalamudReflector);
        PunishLibMain.Init(pi, "Pandora's Box", new AboutPlugin() { Sponsor = "https://ko-fi.com/taurenkey", Translator = "NiGuangOwO" });

#if RELEASE
        if (Svc.PluginInterface.IsDev || !Svc.PluginInterface.SourceRepository.Contains("NiGuangOwO/DalamudPlugins/main/pluginmaster.json"))
        {
            isDev = true;
            Svc.Framework.Update += Dev;
        }
        else
#endif
        {
            isDev = false;
            Ws = new();
            MainWindow = new();
            Ws.AddWindow(MainWindow);
            TaskManager = new();
            Config = pi.GetPluginConfig() as Configuration ?? new Configuration();
            Config.Initialize(Svc.PluginInterface);

            Svc.Commands.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "打开PandorasBox菜单.",
                ShowInHelp = true
            });

            Svc.PluginInterface.UiBuilder.Draw += Ws.Draw;
            Svc.PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;
            Svc.PluginInterface.UiBuilder.Draw += DrawDevMenu;
            Common.Setup();
            PandoraIPC.Init();
            Events.Init();
            AFKTimer.Init();
            provider = new FeatureProvider(Assembly.GetExecutingAssembly());
            provider.LoadFeatures();
            FeatureProviders.Add(provider);
        }
    }

    private bool showWarning = false;
    private void Dev(IFramework framework)
    {
        if (Svc.ClientState.IsLoggedIn && !showWarning)
        {
            showWarning = true;
            if (Svc.PluginInterface.IsDev)
            {
                Svc.Chat.PrintError("[Pandora's Box] 禁止通过本地加载本插件！");
            }
            if (!Svc.PluginInterface.SourceRepository.Contains("NiGuangOwO/DalamudPlugins/main/pluginmaster.json"))
            {
                Svc.Chat.PrintError($"[Pandora's Box] 当前安装来源 {Svc.PluginInterface.SourceRepository} 非本维护者仓库！");
            }
        }
    }

    public void Dispose()
    {
        if (isDev)
        {
            Svc.Framework.Update -= Dev;
        }
        else
        {
            Svc.Commands.RemoveHandler(CommandName);
            foreach (var f in Features.Where(x => x is not null && x.Enabled))
            {
                f.Disable();
                f.Dispose();
            }

            provider.UnloadFeatures();

            Svc.PluginInterface.UiBuilder.Draw -= Ws.Draw;
            Svc.PluginInterface.UiBuilder.OpenConfigUi -= DrawConfigUI;
            Svc.PluginInterface.UiBuilder.Draw -= DrawDevMenu;
            Ws.RemoveAllWindows();
            MainWindow = null;
            Ws = null;
            ECommonsMain.Dispose();
            PunishLibMain.Dispose();
            FeatureProviders.Clear();
            Common.Shutdown();
            PandoraIPC.Dispose();
            Events.Disable();
            AFKTimer.Dispose();
        }
        P = null;
    }

    private void OnCommand(string command, string args)
    {
        MainWindow.IsOpen = !MainWindow.IsOpen;
    }

    public void DrawConfigUI()
    {
        MainWindow.IsOpen = !MainWindow.IsOpen;
    }

    public void DrawDevMenu()
    {
        if (Svc.PluginInterface.IsDevMenuOpen && !isDev)
        {
            if (ImGui.BeginMainMenuBar())
            {
                if (ImGui.BeginMenu("PandorasBox"))
                {
                    MainWindow.Toggle();
                    ImGui.EndMenu();
                }
                ImGui.EndMainMenuBar();
            }
        }
    }
}

