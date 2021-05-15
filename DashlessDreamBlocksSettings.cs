using YamlDotNet.Serialization;

namespace Celeste.Mod.DashlessDreamBlocks {
    public class DashlessDreamBlocksSettings : EverestModuleSettings {
        [SettingName("DASHLESSDREAMBLOCKS_OVERRIDE")]
        [YamlMember(Alias = "OverrideEnabled")]
        public bool OverrideEnabled { get; protected set; }
    }
}
