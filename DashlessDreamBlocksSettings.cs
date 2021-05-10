using YamlDotNet.Serialization;

namespace Celeste.Mod.DashlessDreamBlocks {
    public class DashlessDreamBlocksSettings : EverestModuleSettings {
        [SettingName("DASHLESSDREAMBLOCKS_OVERRIDE")]
        [YamlMember(Alias = "Enabled")]
        public bool Enabled { get; protected set; }
    }
}
