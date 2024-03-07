using Dalamud;
using ECommons.DalamudServices;
using PandorasBox.FeaturesSetup;

namespace PandorasBox.Features.Other
{
    public unsafe class SkipCutscene : Feature
    {
        public override string Name => "[国服限定] 辍学";

        public override string Description => "主随辍学跳动画";

        public override FeatureType FeatureType => FeatureType.Other;

        private nint offset1;
        private readonly nint offset2;

        public override void Enable()
        {
            //offset1 = Svc.SigScanner.ScanText("75 33 48 8B 0D ?? ?? ?? ?? BA ?? 00 00 00 48 83 C1 10 E8 ?? ?? ?? ?? 83 78");
            //offset2 = Svc.SigScanner.ScanText("74 18 8B D7 48 8D 0D");
            //PluginLog.Debug("Offset1: [\"ffxiv_dx11.exe\"+{0}]", (offset1.ToInt64() - Process.GetCurrentProcess().MainModule!.BaseAddress.ToInt64()).ToString("X"));
            //PluginLog.Debug("Offset2: [\"ffxiv_dx11.exe\"+{0}]", (offset2.ToInt64() - Process.GetCurrentProcess().MainModule!.BaseAddress.ToInt64()).ToString("X"));
            //PluginLog.Debug("is Valid: " + (offset1 != IntPtr.Zero && offset2 != IntPtr.Zero));
            //if (offset1 != IntPtr.Zero && offset2 != IntPtr.Zero)
            //{
            //    SafeMemory.Write<short>(offset1, -28528);
            //    SafeMemory.Write<short>(offset2, -28528);
            //}
            offset1 = Svc.SigScanner.ScanText("?? 32 DB EB ?? 48 8B 01");
            SafeMemory.Write<byte>(offset1, 0x2e);
            base.Enable();
        }

        public override void Disable()
        {
            SafeMemory.Write<byte>(offset1, 0x4);
            //if (offset1 != IntPtr.Zero && offset2 != IntPtr.Zero)
            //{
            //    SafeMemory.Write<short>(offset1, 13173);
            //    SafeMemory.Write<short>(offset2, 6260);
            //}
            base.Disable();
        }
    }
}
