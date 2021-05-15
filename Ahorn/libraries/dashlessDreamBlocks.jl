module DashlessDreamBlocks

using ..Ahorn, Maple

# Add DashlessDreaming option to Map Metadata and Change Inventory Trigger
if "DashlessDreaming" ∉ Maple.inventories
    push!(Maple.inventories, "DashlessDreaming")
end

if "DashlessDreaming (No Backpack)" ∉ Maple.inventories
    push!(Maple.inventories, "DashlessDreaming (No Backpack)")
end

end
