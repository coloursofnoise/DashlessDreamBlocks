using MonoMod.Utils;
using System.Collections.Generic;
using System;

namespace Celeste.Mod.DashlessDreamBlocks {
    public class DashlessDreamBlocksMapProcessor : EverestMapDataProcessor {

        public string DashlessInventory;

        public override void Reset() {
            DashlessInventory = null;
        }

        public override Dictionary<string, Action<BinaryPacker.Element>> Init() {
            return new Dictionary<string, Action<BinaryPacker.Element>> {
                { "meta", meta => {
                    foreach (BinaryPacker.Element el in meta.Children)
                        Context.Run(el.Name, el);
                }},
                { "mode", mode => {
                    mode.AttrIf("Inventory", val => {
                        if (val.StartsWith(DashlessDreamBlocksModule.INVENTORY_PREFIX)) {
                            DashlessInventory = val;
                        }
                    });
                }}
            };
        }

        public override void End() {
            if (!string.IsNullOrEmpty(DashlessInventory)) {
                new DynData<MapData>(MapData)[DashlessDreamBlocksModule.PROPERTY_KEY] = true;
            }
        }

    }
}
