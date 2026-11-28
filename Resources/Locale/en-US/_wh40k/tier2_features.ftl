## SuitTank
wh40k-suit-tank-switched = Your suit switches to a fresh air tank.

## Warmth
wh40k-options-warmth-overlay = Thermal edge vignette (frost / heat)
wh40k-options-warmth-overlay-tooltip = Show frost or heat screen-edge effects when dangerously cold or overheating.

wh40k-warmth-hud-title = THERMAL AUSPEX
wh40k-warmth-here = Here { $celsius }°C
wh40k-warmth-here-vacuum = Here: vacuum
wh40k-warmth-near = Near warmth
wh40k-warmth-badge-damp = Damp
wh40k-warmth-badge-soaked = Soaked

wh40k-warmth-feel-freezing = Freezing
wh40k-warmth-feel-cold = Cold
wh40k-warmth-feel-chilly = Chilly
wh40k-warmth-feel-comfortable = Comfortable
wh40k-warmth-feel-warm = Warm
wh40k-warmth-feel-hot = Hot
wh40k-warmth-feel-overheating = Overheating

wh40k-warmth-cold-start = You're getting dangerously cold. Get warm: a fire, shelter, or a coat.
wh40k-warmth-heat-start = Your body temperature is too high. Cool down.
wh40k-warmth-cold-sign-1 = You shiver uncontrollably.
wh40k-warmth-cold-sign-2 = Your fingers are numb with cold.
wh40k-warmth-cold-sign-3 = Your teeth chatter.
wh40k-warmth-heat-sign-1 = Sweat pours down your face.
wh40k-warmth-heat-sign-2 = You feel dizzy from the heat.
wh40k-warmth-heat-sign-3 = Your clothes cling to you, soaked with sweat.
wh40k-warmth-shiver-others = { CAPITALIZE(THE($target)) } shivers violently from the cold.
wh40k-warmth-sweat-others = { CAPITALIZE(THE($target)) } is drenched in sweat.

wh40k-warmth-examine-soaked = [color=lightblue]{ CAPITALIZE(SUBJECT($target)) } { CONJUGATE-BE($target) } soaking wet.[/color]
wh40k-warmth-examine-damp = [color=lightblue]{ CAPITALIZE(SUBJECT($target)) } { CONJUGATE-BE($target) } damp.[/color]

wh40k-warmth-clothing-warm-to = [color=lightblue]On its own, keeps you warm down to about { $celsius }°C in still, dry air.[/color]
wh40k-warmth-clothing-deep-cold = [color=lightblue]Keeps you warm even in deep cold.[/color]
wh40k-warmth-clothing-heat-shield = [color=orange]Shields you from heat.[/color]
wh40k-warmth-clothing-heat-trap = [color=orange]Holds heat in: you overheat sooner in hot air.[/color]

## MarkingGlow
wh40k-marking-glow-toggle = Glow
wh40k-marking-glow-tooltip = Draw this marking unshaded, so it stays bright in the dark.
wh40k-marking-glow-eyes = Glowing eyes
wh40k-marking-glow-eyes-tooltip = Draw the eyes unshaded, so they stay bright in the dark.
wh40k-marking-glow-hair = Glowing hair
wh40k-marking-glow-facial-hair = Glowing facial hair
wh40k-marking-glow-hair-tooltip = Draw this hair unshaded, so it stays bright in the dark.

## Construction repeat craft
wh40k-craft-repeat-count = x{ $count }
wh40k-craft-repeat-all = All
wh40k-craft-repeat-tooltip = Craft this { $count } times in a row. Moving stops it.
wh40k-craft-repeat-all-tooltip = Craft this until you run out of materials. Moving stops it.

## Lathe & Fabrication Silos
fabrication-silo-title = Fabrication silo
fabrication-silo-parts = Parts silo
fabrication-silo-chemicals = Chemical silo
fabrication-silo-machines = Supply connections
fabrication-silo-components = Stored components
fabrication-silo-reagents = Stored chemicals ({ NATURALFIXED($volume, 2) }/{ NATURALFIXED($max, 2) }u)
fabrication-silo-search = Search stored supplies
fabrication-silo-parts-hint = Insert components by using them on the silo, or dump a bag of them into it; only parts a linkable machine uses go in. Identical parts share one inventory row. Select a machine below to link or unlink it.
fabrication-silo-chemicals-hint = Pour chemicals in with a beaker, jug or jerry can; only chemicals a linkable machine uses go in. Stored chemicals do not react, so they only come out one at a time: alt-click with a beaker to insert it, then withdraw into it below. Select a machine below to link or unlink it.

fabrication-silo-part-stock = { $name } ×{ $count }
fabrication-silo-reagent-stock = { $reagent }: { NATURALFIXED($amount, 2) }u

fabrication-silo-part-rejected = This item is not a component used in a fabrication recipe.
fabrication-silo-full = The parts silo is full.
fabrication-silo-insertion-failed = The silo could not accept these contents.
fabrication-silo-dump-verb = Load components into { $silo }
fabrication-silo-parts-left = Items that are not fabrication components stay in the container.
fabrication-silo-reagents-refused = The silo does not accept { $reagents }.
fabrication-silo-fill-partial = You transfer { $amount }u; the rest stays behind because the silo does not accept { $reagents }.

fabrication-silo-no-machines = No machines available. Machines must be on the same grid, within { $range } metres, and not linked to another silo of this type.
fabrication-silo-eject = Eject
fabrication-silo-eject-hint = Eject one stored item or physical stack from this group.
fabrication-silo-empty = No matching supplies stored.

fabrication-silo-container = Container: { $name } ({ NATURALFIXED($volume, 2) }/{ NATURALFIXED($max, 2) }u)
fabrication-silo-no-container-inserted = No container inserted.
fabrication-silo-insert = Insert
fabrication-silo-insert-hint = Insert the beaker or jug in your hand.
fabrication-silo-eject-container-hint = Eject the container into your hand.
fabrication-silo-withdraw-amount = { $amount }u
fabrication-silo-withdraw-all = All
fabrication-silo-withdraw-hint = Move this chemical, and only this chemical, into the inserted container.
fabrication-silo-withdraw-blocked = Insert a container with free space to withdraw.
fabrication-silo-discard = Discard
fabrication-silo-discard-hint = Delete all of this chemical from the silo. Click again to confirm.

fabrication-silo-no-container = Insert a beaker to withdraw into.
fabrication-silo-container-full = { CAPITALIZE(THE($container)) } is full.
fabrication-silo-no-draw = Stored chemicals can only be withdrawn through the silo's console.

lathe-menu-recipes-title = Recipes
lathe-menu-quantity-hint = items per job
lathe-menu-supplies-title = Linked supplies

lathe-menu-queue-amount-tooltip = Requested total (up to { $max }). Press Enter or leave the field to apply.
lathe-menu-queue-empty = Queue is clear. Choose a recipe to get started.
lathe-menu-queue-index = { $index }
lathe-menu-queue-printed = { $printed }/
lathe-menu-queue-cancel = ×
lathe-menu-cancel-job = Cancel job
lathe-menu-job-progress = Completed / requested

lathe-menu-parts-silo-online = Parts silo connected
lathe-menu-parts-silo-offline = Parts silo: not connected or unavailable
lathe-menu-chemical-silo-online = Chemical silo connected
lathe-menu-chemical-silo-offline = Chemical silo: not connected or unavailable
lathe-menu-silo-link-hint = Open a silo to connect this machine.

lathe-menu-status-printing = Printing
lathe-menu-status-ready = Ready
lathe-menu-status-waiting = Waiting for supplies
lathe-menu-status-design-unavailable = Design unavailable

lathe-menu-queue-missing-line = • { $supply }
lathe-menu-queue-missing-material = { $amount ->
    [1] { NATURALFIXED($amount, 2) } { $unit } of { $material }
    *[other] { NATURALFIXED($amount, 2) } { MAKEPLURAL($unit) } of { $material }
}
lathe-menu-queue-missing-entity = { $amount } × { $material }
lathe-menu-queue-missing-reagent = { NATURALFIXED($amount, 2) }u { $material }

lathe-menu-recipe-ready = Ready to start
lathe-menu-recipe-missing = Queue and wait for supplies
lathe-menu-insert-parts-description = Insert recipe components by using them on the lathe.

## ShipFacing
ui-options-function-wh40k-shuttle-face-cursor = Turn ship toward cursor (hold)

## RadarLabels
wh40k-options-general-radar = Auspex & Radar
wh40k-radar-tags-more = +{ $count } more
wh40k-radar-tags-crowd = ×{ $count }
wh40k-radar-all-names-tooltip = Name every contact (the cursor shows details)
ui-options-wh40k-radar-smart-tags = Smart radar tags: stack overlapping labels and show details under cursor
ui-options-wh40k-radar-icons-only = Radar shows only icons: hide names until hovering over a contact
ui-options-wh40k-radar-hover-reveal = Hover over radar contacts to reveal their names

## Waypoints
wh40k-radar-cursor-coordinates = Cursor: { $x }, { $y }
wh40k-radar-cursor-unknown = Cursor: -
wh40k-radar-scanner-coordinates = Auspex: { $x }, { $y }
wh40k-radar-scanner-unknown = Auspex: -

wh40k-waypoint-header = SHIP WAYPOINTS
wh40k-waypoint-key = WPT
wh40k-waypoint-key-tooltip = Waypoints and autopilot coordinates.
wh40k-waypoint-name = Name (optional)
wh40k-waypoint-x = X
wh40k-waypoint-y = Y
wh40k-waypoint-pick = Pick
wh40k-waypoint-pick-tooltip = Click the radar to fill in X and Y.
wh40k-waypoint-add = Add
wh40k-waypoint-add-tooltip = Save these coordinates as a waypoint for this ship.
wh40k-waypoint-fly = Fly
wh40k-waypoint-fly-tooltip = Send the autopilot here.
wh40k-waypoint-go = Go
wh40k-waypoint-remove = X
wh40k-waypoint-remove-tooltip = Delete this waypoint.
wh40k-waypoint-empty = No waypoints.
wh40k-waypoint-position = X { $x } · Y { $y }
wh40k-waypoint-other-map = In another sector.
wh40k-waypoint-default-name = WP-{ $id }
wh40k-waypoint-status-invalid = Enter a valid X and Y.
wh40k-waypoint-status-full = Waypoint list full ({ $max }).
wh40k-waypoint-status-flying = Autopilot set: X { $x } · Y { $y }

## ShipPreview
wh40k-ship-preview-title = Vessel Schematic Preview
wh40k-ship-preview-no-vessel = No vessel selected
wh40k-ship-preview-loading = Loading schematic...
wh40k-ship-preview-failed = Failed to load this vessel
wh40k-ship-preview-info = { $width } × { $height } tiles, { $count } tiles filled
wh40k-ship-preview-planet-lift = Planetary lift: up to { $gravity } g (estimate)
wh40k-ship-preview-planet-none = Planetary lift: no linear thrusters detected
wh40k-ship-preview-rated-load = Cargo rating: ~{ $tonnes }t before thrust drops a quarter
wh40k-ship-preview-reset-view = Reset view
wh40k-ship-preview-hint = Drag to pan, scroll to zoom

cmd-wh40k_shippreview-desc = Opens a client-side schematic preview window for a vessel prototype.
cmd-wh40k_shippreview-help = Usage: { $command } <vessel id>
cmd-wh40k_shippreview-hint-vessel = <vessel id>
cmd-wh40k_shippreview-unknown = No vessel prototype with id "{ $id }".
cmd-wh40k_shippreview-session-required = Connect to a server to open the vessel preview.

## ShipArmor
wh40k-armor-backed-examine = Protected by hull armor, which absorbs [color=yellow]{ $share }%[/color] of every blast, shell, and beam hit (armor-piercing rounds included).
wh40k-armor-backed-examine-none = Has no hull armor behind it and takes every hit in full.
wh40k-armor-backed-verb-text = Hull armor
wh40k-armor-backed-verb-message = Inspect how much adjacent hull armor protects this mount.

## Ship Vox-PA
wh40k-shuttle-console-vox-tab = Vox / Access
wh40k-shuttle-console-ship-alarms-label = Ship vox and alerts
wh40k-shuttle-console-ship-condition = Condition:
wh40k-shuttle-console-ship-general-quarters-sound = SOUND GENERAL QUARTERS
wh40k-shuttle-console-ship-general-quarters-secure = SECURE FROM GENERAL QUARTERS
wh40k-shuttle-console-ship-distress-send = SEND SOS
wh40k-shuttle-console-ship-distress-cancel = CANCEL SOS
wh40k-shuttle-console-ship-distress-tooltip = Broadcast a distress signal on the common channel and mark this vessel on all sector radars.
wh40k-shuttle-console-ship-speakers = Speakers
wh40k-shuttle-console-ship-speakers-fallback = Air alarms (PA)
wh40k-shuttle-console-ship-speakers-count = { $online }/{ $total } online
wh40k-shuttle-console-ship-speakers-none = NO SPEAKERS ONLINE
wh40k-shuttle-console-ship-volume = Volume
wh40k-shuttle-console-ship-volume-value = { $percent }%
wh40k-shuttle-console-ship-announce-placeholder = Type a bridge announcement...
wh40k-shuttle-console-ship-announce = Broadcast

wh40k-ship-alert-code-green = Code Green
wh40k-ship-alert-code-green-desc = Normal operations. No known threats.
wh40k-ship-alert-code-green-announcement = All hands, condition green. Resume normal duties.

wh40k-ship-alert-code-yellow = Code Yellow
wh40k-ship-alert-code-yellow-desc = Elevated readiness. Stand by for orders.
wh40k-ship-alert-code-yellow-announcement = All hands, condition yellow. Secure loose gear and stand by at your stations.

wh40k-ship-alert-code-red = Code Red
wh40k-ship-alert-code-red-desc = Hostile action underway. General quarters.
wh40k-ship-alert-code-red-announcement = All hands, condition red. Man battle stations and seal compartments. This is not a drill.

wh40k-ship-alert-code-black = Code Black
wh40k-ship-alert-code-black-desc = Vessel loss imminent. Abandon ship.
wh40k-ship-alert-code-black-announcement = All hands, abandon ship. Proceed immediately to the nearest escape pod or docking bay. This is not a drill.

wh40k-ship-pa-general-quarters-announcement = General quarters, general quarters. All hands man your battle stations.
wh40k-ship-pa-general-quarters-secure = Secure from general quarters. All hands stand down.

wh40k-ship-pa-sender = { $ship } Vox-PA
wh40k-ship-pa-unknown-ship = Unknown vessel
wh40k-ship-pa-announce-cooldown = The Vox-PA network is still cycling. Wait a moment before broadcasting again.
wh40k-ship-pa-announce-no-speakers = No functioning Vox-PA speakers aboard to carry that.
wh40k-ship-pa-subtitle-title = { $ship } · VOX-PA

wh40k-ship-pa-speaker-examine-ok = The status indicator glows steady green. Ready to carry ship announcements.
wh40k-ship-pa-speaker-examine-damaged = The status indicator flickers amber, and faint static crackles from the grille.
wh40k-ship-pa-speaker-examine-broken = The grille is shattered and silent. It won't carry a signal like this.
wh40k-ship-pa-speaker-examine-unpowered = The status indicator is dark. It has no power.

wh40k-ship-distress-radio = MAYDAY, MAYDAY, MAYDAY. This is { $ship }, requesting immediate assistance at { $x }, { $y }.
wh40k-ship-distress-radio-cancel = This is { $ship }. Cancel our distress call.
wh40k-ship-distress-announcement = Distress call transmitted. This vessel is now marked on every radar.
wh40k-ship-distress-announcement-cancel = Distress call cancelled.
wh40k-ship-distress-announcement-lapsed = Distress call lapsed. Transmit again if assistance is still needed.
wh40k-ship-distress-cooldown = Wait { $seconds } seconds before sending another distress call.
wh40k-ship-distress-marker = SOS: { $ship }

ui-options-wh40k-ship-pa-volume = Ship Vox-PA

cmd-wh40k_shippa_announce-desc = Broadcasts a Vox-PA announcement on a ship.
cmd-wh40k_shippa_announce-help = Usage: { $command } <grid> <text...>
cmd-wh40k_shippa_code-desc = Sets a ship's alert code.
cmd-wh40k_shippa_code-help = Usage: { $command } <grid> <Wh40kShipCodeX>
cmd-wh40k_shippa_gq-desc = Toggles general quarters on a ship.
cmd-wh40k_shippa_gq-help = Usage: { $command } <grid> <true|false>
cmd-wh40k_shippa-invalid-grid = '{ $grid }' is not a grid entity.
cmd-wh40k_shippa-invalid-code = '{ $code }' is not a ship alert code.
cmd-wh40k_shippa-no-speakers = No functioning Vox-PA speakers on that grid; nothing was heard.
cmd-wh40k_shippa-hint-code = <ship alert code>
cmd-wh40k_shippa-hint-active = <true|false>

ent-Wh40kMachinePartsSilo = parts silo
    .desc = Stores components and supplies them to linked fabrication machines. Use a component on it or empty a bag into it to add stock.

ent-Wh40kMachineChemicalSilo = chemical silo
    .desc = Stores up to 1500u of chemicals without a reaction and supplies them to linked fabrication machines. Withdraw individual reagents into an inserted container.

ent-Wh40kPartsSiloMachineCircuitboard = parts silo machine board
    .desc = A machine board for a parts silo.

ent-Wh40kChemicalSiloMachineCircuitboard = chemical silo machine board
    .desc = A machine board for a chemical silo.

ent-Wh40kPartsSiloFlatpack = parts silo flatpack
    .desc = A flatpack used for constructing a parts silo.

ent-Wh40kChemicalSiloFlatpack = chemical silo flatpack
    .desc = A flatpack used for constructing a chemical silo.

ent-Wh40kWallmountShipPaSpeaker = ship Vox-PA speaker
    .desc = A wall-mounted speaker that carries bridge announcements and battle-station klaxons.
