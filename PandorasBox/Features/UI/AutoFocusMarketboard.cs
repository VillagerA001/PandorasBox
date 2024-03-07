using PandorasBox.FeaturesSetup;

namespace PandorasBox.Features.UI
{
    public unsafe class AutoFocus : Feature
    {
        public override string Name => "自动聚焦市场版搜索";

        public override string Description => "自动聚焦到市场板的搜索框上";

        public override FeatureType FeatureType => FeatureType.UI;

        private void AddonSetup(SetupAddonArgs obj)
        {
            if (obj.AddonName != "ItemSearch") return;
            obj.Addon->SetFocusNode(obj.Addon->CollisionNodeList[11]);
        }

        public override void Enable()
        {
            Common.OnAddonSetup += AddonSetup;
            base.Enable();
        }

        public override void Disable()
        {
            Common.OnAddonSetup -= AddonSetup;
            base.Disable();
        }
    }
}
