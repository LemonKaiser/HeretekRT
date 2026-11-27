# WH40K Synthetics & Cybernetic Chassis Panel / HUD

ent-ActionWh40kChassisPanel = Chassis Diagnostics
    .desc = Open your internal cogitator chassis panel to inspect limbs, components, bay access, overclock, and reboot controls.

ent-WH40KOrganRadCollector = radiation collector
    .desc = A shielded Mechanicus capacitor stack installed inside a cybernetic torso. Absorbs ambient radiation up to 300 rads; remove via the chassis bay to vent or weld to restore integrity.

wh40k-synthetics-not-synthetic = This body does not have a cybernetic chassis.

# Chassis panel UI
wh40k-chassis-ui-title = COGITATOR CHASSIS DIAGNOSTICS
wh40k-chassis-ui-tab-parts = PARTS
wh40k-chassis-ui-tab-components = COMPONENTS
wh40k-chassis-ui-tab-augments = AUGMENTATION
wh40k-chassis-ui-controls = CHASSIS CONTROLS
wh40k-chassis-ui-doll-hint = Click a limb
wh40k-chassis-ui-none = --
wh40k-chassis-ui-empty = [EMPTY]
wh40k-chassis-ui-vital = VITAL
wh40k-chassis-ui-confirm = CONFIRM?

wh40k-chassis-ui-col-part = PART
wh40k-chassis-ui-col-state = STATE
wh40k-chassis-ui-col-integrity = INTEGRITY
wh40k-chassis-ui-col-faults = FAULTS
wh40k-chassis-ui-col-actions = ACTIONS
wh40k-chassis-ui-col-slot = SLOT
wh40k-chassis-ui-col-component = COMPONENT
wh40k-chassis-ui-col-health = CHARGE / HP
wh40k-chassis-ui-col-band = STATUS

wh40k-chassis-ui-state-ok = OK
wh40k-chassis-ui-state-isolated = ISOLATED
wh40k-chassis-ui-state-loose = LOOSE
wh40k-chassis-ui-state-unpowered = UNPOWERED
wh40k-chassis-ui-state-offline = OFFLINE
wh40k-chassis-ui-state-detached = DETACHED

wh40k-chassis-ui-isolate = ISOLATE
wh40k-chassis-ui-restore = RESTORE
wh40k-chassis-ui-detach = DETACH
wh40k-chassis-ui-disconnect = DISCONNECT
wh40k-chassis-ui-bay-open = OPEN BAY
wh40k-chassis-ui-bay-close = CLOSE BAY
wh40k-chassis-ui-reboot = REBOOT
wh40k-chassis-ui-overclock-on = OVERCLOCK: ON
wh40k-chassis-ui-overclock-off = OVERCLOCK: OFF

wh40k-chassis-ui-row-power = POWER
wh40k-chassis-ui-row-integrity = INTEGRITY
wh40k-chassis-ui-row-sensors = SENSORS
wh40k-chassis-ui-row-servos = SERVOS
wh40k-chassis-ui-row-core = CORE
wh40k-chassis-ui-row-core-temp = {$value} K
wh40k-chassis-ui-row-heat = HEAT
wh40k-chassis-ui-row-faults = FAULTS
wh40k-chassis-ui-rad-collector = RAD COLLECTOR
wh40k-chassis-ui-rad-collector-none = MISSING / BURNT
wh40k-chassis-ui-radiation = AMBIENT RAD: {$value} rad/s

wh40k-chassis-ui-bay-banner = CHASSIS BAY OPEN — ARMOR EXPOSED
wh40k-chassis-ui-overclock-tag = OVERCLOCK

wh40k-chassis-ui-tip-bay-open = Open the torso service bay (3s / 6s self). In the field only power cell swaps are allowed; dock at a powered augmentation operating table to swap other internal modules. Slows movement and exposes torso armor.
wh40k-chassis-ui-tip-bay-close = Close and latch the torso service bay plating.
wh40k-chassis-ui-tip-reboot = Reboot chassis (4s paralysis, 10% charge, 2m cooldown). Clears stun, stamina, jitter, blindness, and disables Overclock. Click twice to confirm.
wh40k-chassis-ui-tip-overclock = Overclock servos for +25% speed. Drains battery x3, builds chassis heat, risks sparks, shorts, and heat damage. Auto-stops below 15% charge or on overheat.
wh40k-chassis-ui-tip-rad-collector = Internal radiation capacitor stack. Vent outside the body when full; weld to repair integrity.
wh40k-chassis-ui-tip-radiation = Ambient radiation currently reaching the chassis.
wh40k-chassis-ui-tip-disconnect-closed = Open the chassis bay first to disconnect internal components.
wh40k-chassis-ui-tip-disconnect-table = Only power cells can be swapped in the field. Dock at a powered augmentation operating table to extract this module.
wh40k-chassis-ui-tip-disconnect-vital = VITAL component! Click twice within 3 seconds to confirm removal.
wh40k-chassis-ui-tip-requires-table = Detaching limbs requires a powered augmentation operating table.

# Augmentation Operating Table dock integration
wh40k-chassis-ui-dock-ready = ● AUGMENTATION TABLE LINKED — FULL MODULE & IMPLANT ACCESS
wh40k-chassis-ui-dock-offline = ▲ AUGMENTATION TABLE UNPOWERED — MODULE SWAPS LOCKED
wh40k-chassis-ui-dock-field = ○ FIELD MODE — POWER CELL SWAP & FIELD REPAIRS ONLY
wh40k-chassis-ui-aug-banner-ready = OPERATING TABLE LINKED // SELECT AN IMPLANT OR PROSTHESIS FROM INVENTORY TO INSTALL
wh40k-chassis-ui-aug-banner-offline = OPERATING TABLE HAS NO POWER // RESTORE POWER TO INSTALL OR REMOVE MODULES
wh40k-chassis-ui-aug-banner-field = FIELD MODE // BUCKLE ONTO A POWERED AUGMENTATION OPERATING TABLE TO INSTALL OR SWAP MODULES
wh40k-chassis-ui-aug-available = COMPATIBLE EQUIPMENT IN INVENTORY
wh40k-chassis-ui-aug-installed = INSTALLED AUGMENTATIONS
wh40k-chassis-ui-aug-install = INSTALL
wh40k-chassis-ui-aug-need-table = Lie on a powered augmentation operating table to scan inventory and fit implants or prostheses.
wh40k-chassis-ui-aug-none-installed = No augmentations or prostheses detected.
wh40k-chassis-ui-col-zone = ZONE
wh40k-chassis-ui-col-augment = MODULE / IMPLANT
wh40k-chassis-ui-col-replaces = REPLACES
wh40k-chassis-table-open-panel = Open chassis augmentation panel
wh40k-chassis-table-linked = Chassis cogitator linked to the augmentation operating table.
wh40k-chassis-table-linked-unpowered = Augmentation operating table is unpowered; module access remains locked.
wh40k-chassis-table-required-organ = Extracting internal modules requires a powered augmentation operating table (only power cells can be swapped in the field).
wh40k-chassis-table-required-install = Installing internal modules or implants requires a powered augmentation operating table (only power cells can be swapped in the field).
wh40k-chassis-table-required-detach = Detaching limbs requires a powered augmentation operating table.

# Status strings
wh40k-chassis-status-nominal = STATUS: ONLINE
wh40k-chassis-status-overclocked = STATUS: OVERCLOCKED
wh40k-chassis-status-bay-open = STATUS: BAY OPEN
wh40k-chassis-status-low-power = STATUS: LOW POWER
wh40k-chassis-status-reboot = STATUS: REBOOTING
wh40k-chassis-status-critical = STATUS: CRITICAL
wh40k-chassis-status-dead = STATUS: OFFLINE

# Organ slots & health bands
wh40k-chassis-slot-brain = BRAIN (ORGANIC)
wh40k-chassis-slot-posbrain = POSITRONIC CORE
wh40k-chassis-slot-eyes = OPTICS
wh40k-chassis-slot-leftEye = LEFT OPTIC
wh40k-chassis-slot-rightEye = RIGHT OPTIC
wh40k-chassis-slot-pump = COOLANT PUMP
wh40k-chassis-slot-radcollector = RAD COLLECTOR
wh40k-chassis-slot-battery = POWER CELL
wh40k-chassis-slot-powercell = AUGMENT CELL
wh40k-chassis-slot-armImplant = ARM AUGMENT

wh40k-chassis-band-charge = CHARGE
wh40k-chassis-band-normal = NOMINAL
wh40k-chassis-band-impaired = WORN
wh40k-chassis-band-failed = CRITICAL

# Body part names
wh40k-chassis-part-head = HEAD
wh40k-chassis-part-torso = TORSO
wh40k-chassis-part-left-arm = L-ARM
wh40k-chassis-part-right-arm = R-ARM
wh40k-chassis-part-left-hand = L-HAND
wh40k-chassis-part-right-hand = R-HAND
wh40k-chassis-part-left-leg = L-LEG
wh40k-chassis-part-right-leg = R-LEG
wh40k-chassis-part-left-foot = L-FOOT
wh40k-chassis-part-right-foot = R-FOOT
wh40k-chassis-part-chassis = CHASSIS

# Panel actions / popups
wh40k-chassis-panel-refuse-dead = Chassis cogitator is offline.
wh40k-chassis-link-start-user = Linking multitool to {THE($target)}'s diagnostic port...
wh40k-chassis-link-start-target = {CAPITALIZE(THE($user))} is linking into your diagnostic port!
wh40k-chassis-link-done-target = {CAPITALIZE(THE($user))} connected to your chassis panel.

# Bay popups & verbs
wh40k-chassis-bay-no-torso = This chassis has no torso bay.
wh40k-chassis-bay-opening-self = Unlatching your chassis service bay...
wh40k-chassis-bay-closing-self = Latching your chassis service bay...
wh40k-chassis-bay-opening = Unlatching {THE($target)}'s chassis service bay...
wh40k-chassis-bay-closing = Latching {THE($target)}'s chassis service bay...
wh40k-chassis-bay-opening-other = {CAPITALIZE(THE($user))} is opening your chassis bay!
wh40k-chassis-bay-closing-other = {CAPITALIZE(THE($user))} is closing your chassis bay.
wh40k-chassis-bay-opened = The chassis service bay snaps open.
wh40k-chassis-bay-closed = The chassis service bay latches shut.
wh40k-chassis-bay-closed-refusal = Open the chassis bay first.
wh40k-chassis-bay-pulling = Disconnecting {THE($item)}...
wh40k-chassis-bay-pulling-other = {CAPITALIZE(THE($user))} is disconnecting your {$item}!
wh40k-chassis-bay-pulled = Disconnected {THE($item)}.
wh40k-chassis-bay-pulled-other = Your {$item} was disconnected!
wh40k-chassis-bay-installing = Installing {THE($item)} into the chassis bay...
wh40k-chassis-bay-installed = Installed {THE($item)}.
wh40k-chassis-bay-installed-other = {CAPITALIZE(THE($item))} was installed in your chassis bay.
wh40k-chassis-bay-no-slot = No compatible slot for this component.
wh40k-chassis-bay-slot-full = That component slot is already occupied.
wh40k-chassis-bay-verb-open = Open chassis bay
wh40k-chassis-bay-verb-close = Close chassis bay
wh40k-chassis-bay-verb-pull = Pull {$item}
wh40k-chassis-bay-examine-open = [color=#ffb000]{CAPITALIZE(THE($target))}'s torso service bay is open![/color]

# Isolation & Detach / Reattach (Lego Limbs)
wh40k-chassis-isolate-refuse = Only limbs can be isolated.
wh40k-chassis-isolate-refuse-cuffed = Cannot isolate arms while restrained!
wh40k-chassis-isolate-done = Isolated {$part}.
wh40k-chassis-restore-done = Restored {$part}.
wh40k-chassis-restore-low-power = {CAPITALIZE($part)} queued for restore when power returns.
wh40k-chassis-detach-refuse = Cannot detach that part.
wh40k-chassis-detach-done = Detached {$part} from {THE($target)}.
wh40k-chassis-detach-done-owner = Your {$part} detached!

wh40k-synthetic-limb-not-chassis = Only mechanical or prosthetic limbs can snap into this chassis.
wh40k-synthetic-limb-no-socket = No matching open socket for this limb.
wh40k-synthetic-limb-reattaching = Snapping {THE($limb)} into place...
wh40k-synthetic-limb-reattaching-other = {CAPITALIZE(THE($user))} is snapping {THE($limb)} onto your chassis...
wh40k-synthetic-limb-reattached = {CAPITALIZE(THE($limb))} snapped into place with a loose coupling.
wh40k-synthetic-limb-coupling-tightened = Tightened {$limb} coupling ({$passes} cable passes left).
wh40k-synthetic-limb-coupling-seated = {CAPITALIZE($limb)} coupling is fully seated!
wh40k-synthetic-limb-examine-loose = [color=#ffb000]{CAPITALIZE(THE($target))}'s {$part} has a loose coupling ({$passes} cable passes left).[/color]

# Overclock & Reboot
wh40k-chassis-reboot-refuse-dead = Chassis is offline and cannot reboot.
wh40k-chassis-reboot-refuse-busy = Reboot sequence already in progress.
wh40k-chassis-reboot-refuse-cooldown = Reboot available in {$seconds}s.
wh40k-chassis-reboot-start = {CAPITALIZE(THE($target))} initiates a chassis reboot!
wh40k-chassis-reboot-done = Chassis reboot complete. Systems stabilized.
wh40k-chassis-overclock-on = Servo overclock engaged (+25% speed).
wh40k-chassis-overclock-off = Servo overclock disengaged.
wh40k-chassis-overclock-refuse-dead = Chassis is offline.
wh40k-chassis-overclock-refuse-low-power = Insufficient power for overclock.
wh40k-chassis-overclock-refuse-charge = Battery below 15% — overclock blocked.
wh40k-chassis-overclock-refuse-heat = Chassis thermal limit reached!
wh40k-chassis-overclock-auto-off = Overclock disengaged: {$reason}
wh40k-chassis-overclock-short = Servo arc! Overclock triggered a short-circuit!

# Low Power
wh40k-low-power-enter = POWER DEPLETED — EMERGENCY CRAWL MODE (1 ARM ACTIVE)
wh40k-low-power-exit = Power restored. Full servo output resumed.
wh40k-low-power-switch-cooldown = Power rerouting still settling...
wh40k-low-power-switched = Rerouted reserve power to {$part}.
wh40k-low-power-verb-switch = Reroute reserve power to {$part}

# Rad Collector
wh40k-rad-collector-saturated = Radiation collector saturated! Overflowing into chassis!
wh40k-rad-collector-burnt-out = Radiation collector burnt out and destroyed!
wh40k-rad-collector-missing = No radiation collector installed! Radiation striking chassis directly!
wh40k-rad-collector-examine-charge = Absorbed radiation: [color=#ffb000]{$percent}%[/color]
wh40k-rad-collector-examine-integrity = Capacitor integrity: [color=#7fdc6a]{$percent}%[/color]
wh40k-rad-collector-examine-venting = [color=#6fd6ff]Passively venting absorbed radiation.[/color]
wh40k-rad-collector-weld-full = The radiation collector is already at full integrity.
wh40k-rad-collector-welded = Repaired radiation collector ({$percent}% integrity).

# Helmet HUD Overlay
wh40k-hud-system = COGITATOR :: SYSTEM
wh40k-hud-diagnostics = DIAGNOSTICS
wh40k-hud-row-rad = RAD-CAP
wh40k-hud-tag-info = INFO
wh40k-hud-tag-warn = WARN
wh40k-hud-tag-crit = CRIT
wh40k-hud-tag-fail = FAIL
wh40k-hud-advice = ADVICE: {$advice}
wh40k-hud-idle-1 = OMNISSIAH TELEMETRY NOMINAL
wh40k-hud-idle-2 = SERVO BUS SYNCHRONIZED
wh40k-hud-idle-3 = THERMAL REGULATORS STABLE
wh40k-hud-idle-4 = POWER GRID BALANCED
wh40k-hud-banner-standby = STANDBY :: CHASSIS CRITICAL
wh40k-hud-banner-rebooting = REBOOTING CHASSIS...
wh40k-hud-banner-reboot-hint = OPEN CHASSIS PANEL OR AWAIT TECH-PRIEST
wh40k-hud-banner-panic = KERNEL PANIC — FATAL CHASSIS FAILURE
wh40k-hud-panic-dump = 0x{$address} FAULT::SERVOBUS_HALT [STACK CORRUPT]
wh40k-hud-death-banner = CHASSIS OFFLINE
wh40k-hud-death-sub = MACHINE SPIRIT TERMINATED — REQUIRES REPAIR & RESTART
wh40k-hud-banner-low-power = LOW POWER — CRAWL MODE
wh40k-hud-banner-overheat = THERMAL WARNING — OVERHEATING
wh40k-hud-banner-integrity = CHASSIS INTEGRITY CRITICAL ({$value})

# HUD Fault lines & advice
wh40k-hud-fault-detached = LIMB DETACHED
wh40k-hud-advice-detached = REATTACH LIMB
wh40k-hud-fault-isolated = SERVO ISOLATED
wh40k-hud-advice-isolated = RESTORE IN PANEL
wh40k-hud-fault-loose = LOOSE COUPLING
wh40k-hud-advice-loose = TIGHTEN WITH CABLE
wh40k-hud-fault-unpowered = ARM UNPOWERED
wh40k-hud-advice-unpowered = RECHARGE BATTERY
wh40k-hud-fault-offline = ACTUATOR OFFLINE
wh40k-hud-advice-offline = WELD OR REPLACE
wh40k-hud-fault-low-power = CELL DEPLETED
wh40k-hud-advice-low-power = DRINK APC / SWAP CELL
wh40k-hud-fault-low-charge = LOW BATTERY
wh40k-hud-advice-low-charge = RECHARGE AT APC
wh40k-hud-fault-overheat = SERVO OVERHEAT
wh40k-hud-advice-overheat = DISENGAGE OVERCLOCK
wh40k-hud-fault-cold = CHASSIS FREEZING
wh40k-hud-advice-cold = SEEK WARMTH / SUIT
wh40k-hud-fault-rad-saturated = RAD COLLECTOR FULL
wh40k-hud-advice-rad-saturated = PULL & VENT COLLECTOR
wh40k-hud-fault-rad-missing = NO RAD COLLECTOR
wh40k-hud-advice-rad-missing = INSTALL RAD COLLECTOR
wh40k-hud-fault-brute = HULL IMPACT DAMAGE
wh40k-hud-advice-brute = WELD CHASSIS PLATING
wh40k-hud-fault-heat = THERMAL BURN DAMAGE
wh40k-hud-advice-heat = REPAIR WITH CABLE
wh40k-hud-fault-shock = ELECTRICAL ARC DAMAGE
wh40k-hud-advice-shock = REPAIR WITH CABLE
wh40k-hud-fault-bay-open = SERVICE BAY OPEN
wh40k-hud-advice-bay-open = CLOSE CHASSIS BAY
