armor-plate-break = Your {$plateName} has shattered!
armor-plate-examine-with-plate = Has a [color=yellow]{$plateName}[/color] installed. Durability: [color={$durabilityColor}]{$percent}%[/color]
armor-plate-examine-with-plate-simple = Has a [color=yellow]{$plateName}[/color] installed.
armor-plate-examine-no-plate = No armor plate installed.
armor-plate-examine-no-storage = No storage compartment for armor plates.
armor-plate-examinable-verb-text = Plate attributes
armor-plate-examinable-verb-message = Examine the armor wear buffer and movement effects.
armor-plate-attributes-examine = This armor plate:
armor-plate-initial-durability = Provides [color=yellow]{$durability}[/color] units of replaceable armor durability.
armor-plate-wear-buffer = Extends armor durability. It does not add damage resistance.
armor-plate-item-durability = Durability: [color={$durabilityColor}]{$percent}%[/color]
armor-plate-gait-speed = speed
armor-plate-gait-walk = walking speed
armor-plate-gait-sprint = running speed
armor-plate-speed-display =
    { $stringClause ->
        [1] Increases your {$gait} by [color=yellow]{$speedPercent}%[/color].
        [-1] Decreases your {$gait} by [color=yellow]{$speedPercent}%[/color].
       *[other] Does not affect your {$gait}.
    }
armor-plate-ratios-display = Takes [color=yellow]{$ratioPercent}%[/color] of armor wear caused by [color=yellow]{$dmgType}[/color]
armor-plate-multiplier-display = and spends [color=yellow]{$multiplier}%[/color] of transferred wear from plate durability.
armor-plate-multiplier-none = and does not damage the plate.

ent-ArmorPlateBlunt_Slash = trauma armor plate
    .desc = A replaceable wear buffer for compatible armor. Takes blunt and slash wear instead of the armor, without adding damage resistance.
ent-ArmorPlatePierce = ballistic armor plate
    .desc = A replaceable wear buffer for compatible armor. Takes piercing wear instead of the armor, without adding damage resistance.
ent-ArmorPlateHeat = diffusion armor plate
    .desc = A replaceable wear buffer for compatible armor. Takes thermal wear and part of radiation and electrical wear, without adding damage resistance.
ent-ArmorPlateSpeed = speed armor plate
    .desc = A light replacement plate that increases movement speed and takes a quarter of physical and thermal armor wear.
