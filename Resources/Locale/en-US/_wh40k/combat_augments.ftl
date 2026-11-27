# WH40K Stage 5 Combat, Tactical, Stealth, and Survival Augments

wh40k-smartlink-ammo = {$count}/{$capacity}
wh40k-smartlink-range = {$meters}m

wh40k-augment-deployable-emp = The implant does not respond due to EMP interference.
wh40k-burst-launcher-recharged = Corona charge ready ({$charges}/{$max}).
wh40k-shoulder-cannon-cooling = The plasma servo-turret is still cooling down.
wh40k-hand-shield-collapse = Your refractor shield flickers out!
wh40k-hand-shield-recharging = Your refractor shield is still recharging.
wh40k-hand-shield-other = Your other refractor shield is already active.
wh40k-optical-camo-off = You shimmer back into view.
wh40k-optical-camo-blocked = Your cameleoline shroud cannot activate while another stealth effect is active.
wh40k-holo-projector-cramped = There is not enough room here to project holographic doubles.
wh40k-holo-projector-buckled = Unbuckle before using the holo-projector.
wh40k-adrenal-booster-surge = Adrenaline floods your veins and you surge back to your feet!
wh40k-adrenal-booster-surge-others = {CAPITALIZE(THE($target))} shakes off the shock and surges back up!
wh40k-adrenal-booster-ready = Your adrenal combat injector has recharged.
wh40k-reflex-booster-dodge = {CAPITALIZE(THE($target))} twists cleanly out of the projectile's path!
wh40k-augment-nutriment-pump-feed = Your nutrient micro-pump delivers an emergency ration...
wh40k-augment-reviver-charging = Something inside your chest whines as capacitors charge.
wh40k-augment-reviver-shock = {CAPITALIZE(THE($target))} jolts violently as an internal defibrillator fires!

ent-WH40KAugmentFingerLighterFlame = finger piezo-igniter
    .desc = A miniature piezo-igniter deployed from a fingertip implant.

ent-WH40KArmLaserBeam = forearm las-emitter beam

ent-WH40KAugmentArmLaserGun = integrated forearm las-emitter
    .desc = A compact Mechanicus las-emitter extended from a cybernetic forearm. Fires rapid pulses until its internal capacitor depletes.

ent-WH40KAugmentHandShieldProjector = refractor palm shield
    .desc = A projected hard-light refractor barrier emitted from a cybernetic palm. Blocks frontal attacks and deflects beams until depleted.

ent-WH40KShoulderCannonBolt = servo-turret plasma bolt

ent-WH40KHaloOrbLethal = Corona fragmentation charge
    .desc = A hovering fragmentation charge just before detonation.

ent-WH40KHaloOrbStun = Corona concussion charge
    .desc = A hovering concussion charge just before detonation.

ent-WH40KHaloShield = corona stasis shield

ent-WH40KHaloBlast = corona blast

ent-WH40KHoloDecoy = holographic decoy

ent-WH40KHoloDecoyPuff = holo-field distortion

ent-WH40KActionIatricScan = Medicae Auspex Readout
    .desc = Silently inspect your own vital signs via the internal medicae cogitator.

ent-WH40KActionCycleThermalTint = Cycle Multispectral Optics
    .desc = Cycle Tapetum ocular implants through thermal, green, cyan, and white night vision, then off.

ent-WH40KActionToggleLeftFingerLighter = Left Finger Igniter
    .desc = Extend or retract the piezo-igniter in your left fingertip.

ent-WH40KActionToggleRightFingerLighter = Right Finger Igniter
    .desc = Extend or retract the piezo-igniter in your right fingertip.

ent-WH40KActionToggleLeftArmLaser = Left Forearm Las-Emitter
    .desc = Deploy or retract the las-emitter housed in your left forearm.

ent-WH40KActionToggleRightArmLaser = Right Forearm Las-Emitter
    .desc = Deploy or retract the las-emitter housed in your right forearm.

ent-WH40KActionToggleLeftHandShield = Left Refractor Shield
    .desc = Project or lower the hard-light refractor shield from your left palm.

ent-WH40KActionToggleRightHandShield = Right Refractor Shield
    .desc = Project or lower the hard-light refractor shield from your right palm.

ent-WH40KActionDeployShoulderCannon = Plasma Servo-Turret
    .desc = Deploy or stow your shoulder-mounted plasma turret. While deployed, middle-click to aim and fire.

ent-WH40KActionHaloBurst = Corona Frag Ring
    .desc = Launch a ring of eight fragmentation charges around yourself while anchored in a brief shield. Holds 2 charges.

ent-WH40KActionHaloStun = Corona Concussion Ring
    .desc = Launch a ring of eight concussion charges around yourself while anchored in a brief shield. Holds 2 charges.

ent-WH40KActionOpticalCamo = Cameleoline Shroud
    .desc = Activate subdermal optical camouflage for 20 seconds. Combat, taking damage, or EMP disrupts it early.

ent-WH40KActionHoloProject = Mirage Holo-Doubles
    .desc = Project two holographic duplicates across the room and swap positions with one of them.

ent-WH40KAugmentSmartlink = Shojun targeter-link implant
    .desc = A cybernetic palm interface that synchronizes with held firearms, taming recoil, accelerating rounds, and projecting an optical ammo/target-tracking overlay up to 12 tiles. Requires a cybernetic hand.

ent-WH40KAugmentTapetumEyes = Tapetum multispectral augmetic eyes
    .desc = Mechanicus scout optics that cycle with one command between thermal imaging and green, cyan, and white night-vision phosphors. Protects against welding glare, though flashes still blind.

ent-WH40KAugmentIatricMonitor = Kanshi internal medicae auspex
    .desc = A cranial micro-auspex wired behind the optic nerve that silently displays your own medical telemetry on demand.

ent-WH40KAugmentArmLaser = Lux-VI forearm las-emitter implant
    .desc = A rapid-fire las-emitter built into a cybernetic forearm. Retains charge while stowed and automatically fires in tandem with a second emitter in the opposite arm. Requires a cybernetic arm.

ent-WH40KAugmentHandShield = Zaslon refractor palm shield implant
    .desc = A palm-mounted hard-light refractor generator projecting a 135-degree barrier (200 capacity) that stops bullets, melee blows, and stun strikes while deflecting lasers. Recharges only while lowered. Requires a cybernetic hand.

ent-WH40KAugmentShoulderCannon = Grom plasma servo-turret implant
    .desc = A folding shoulder-mounted plasma turret bolted to a cybernetic torso frame, leaving both hands free. Aim and fire with middle-click after a 0.8s charge. Requires a cybernetic chassis.

ent-WH40KAugmentHaloBurst = Corona fragmentation ring implant
    .desc = Sub-thoracic launchers that disperse eight amber fragmentation orbs around the wearer while anchoring them in a 2-second stasis shield. Holds 2 charges with a 3-minute recharge.

ent-WH40KAugmentHaloStun = Corona concussion ring implant
    .desc = Sub-thoracic launchers that disperse eight non-lethal concussion orbs dealing heavy stamina shock and knockback while shielding the wearer for 2 seconds. Holds 2 charges with a 3-minute recharge.

ent-WH40KAugmentOpticalCamo = Kasumi cameleoline subdermal array
    .desc = A mesh of light-bending cameleoline cells threaded beneath the skin. Renders the wearer nearly invisible for 20 seconds (60s cooldown; 120s EMP lockout). Broken by attacking, shooting, or taking hostile damage.

ent-WH40KAugmentHoloProjector = Mirage holo-decoy projector implant
    .desc = Subdermal holo-emitters that project two wandering holographic doubles across the current room and swap you to a safe tile (90s cooldown). Doubles have no thermal signature and vanish on any hit.

ent-WH40KAugmentAdrenalBooster = Ryvok adrenal combat injector
    .desc = Wired directly into organic adrenal glands: increases stamina pool and recovery, and triggers a 1.5-second second-wind surge to shake off stamina knockdowns (2-minute cooldown). Requires an organic body.

ent-WH40KAugmentReflexBooster = Strizh synaptic reflex booster
    .desc = Accelerates spinal nerve pathways: improves piloted vessel thrust and turn rate by 15% and gives a 5% chance to evade incoming projectiles, at the cost of 5% higher damage sensitivity.

ent-WH40KAugmentThreatSole = Sapyor threat-sense sole implant
    .desc = Sub-dermal pressure and magnetic sensors fitted into the sole of a foot that prevent mines, tripwires, and floor traps from triggering underfoot.

ent-WH40KAugmentScrubberLiver = Mechanicus filtration liver implant
    .desc = A synthetic bio-filtration liver that clears alcohol, poisons, and narcotics from the bloodstream four times faster than natural tissue. Requires an organic body.

ent-WH40KAugmentNutrimentPump = Paek nutrient micro-pump implant
    .desc = A compact nutrient dispenser grafted beside the stomach that automatically feeds you when starving. Requires an organic body.

ent-WH40KAugmentFingerLighter = Iskra fingertip piezo-igniter implant
    .desc = A miniature piezo-electric igniter embedded in a fingertip, deployable into your hand at will.

ent-WH40KAugmentReviver = Phoenix cardiac auto-reviver implant
    .desc = An internal Mechanicus defibrillator wired directly to the heart. Upon death, arrests bleeding, infuses a 60u blood reserve, and delivers up to three revival shocks (10-minute recharge; 2-minute EMP lockout). Requires an organic body.
