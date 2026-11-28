ent-Wh40kNeurowareStick = data stick
    .desc = A small data stick with an unlabelled port and a sampling pin. This one looks blank.

ent-Wh40kNeurowareStickWhiteline = data stick (Whiteline)
    .desc = A data stick with a hand-written label. The port only fits a synthetic's or cybernetic's maintenance jack.

ent-Wh40kNeurowareStickNullLoop = data stick (Null Loop)
    .desc = A data stick with a hand-written label. The port only fits a synthetic's or cybernetic's maintenance jack.

ent-Wh40kNeurowareStickBurst = data stick (Burst)
    .desc = A data stick with a hand-written label. The port only fits a synthetic's or cybernetic's maintenance jack.

ent-Wh40kNeurowareStickRedline = data stick (Redline)
    .desc = A data stick with a hand-written label. The port only fits a synthetic's or cybernetic's maintenance jack.

ent-Wh40kNeurowareStickDamper = data stick (Damper)
    .desc = A data stick with a hand-written label. The port only fits a synthetic's or cybernetic's maintenance jack.

ent-Wh40kNeurowareStickChoir = data stick (Choir.sig)
    .desc = A data stick with a hand-written label. The port only fits a synthetic's or cybernetic's maintenance jack.

ent-Wh40kNeurowareStickHalo = data stick (Halo)
    .desc = A data stick with a hand-written label. The port only fits a synthetic's or cybernetic's maintenance jack.

ent-Wh40kNeurowareStickIdle = data stick (Idle)
    .desc = A data stick with a hand-written label. The port only fits a synthetic's or cybernetic's maintenance jack.

wh40k-neuroware-stick-blank = The stick is blank.
wh40k-neuroware-stick-spent = The stick has no loads left.
wh40k-neuroware-stick-examine = It holds [color=violet]{ $program }[/color] at { $integrity }% integrity, with { $loads ->
    [one] one load
   *[other] { $loads } loads
} left.
wh40k-neuroware-no-port = { CAPITALIZE(THE($target)) } has no port for this.
wh40k-neuroware-load-target = { CAPITALIZE(THE($user)) } is plugging something into your maintenance jack!
wh40k-neuroware-copy-done = You copy the program onto the blank stick.
wh40k-neuroware-copy-fail = You need a loaded stick and a blank one.
wh40k-neuroware-encode-done = The stick reads { THE($source) } in and writes it down as { $program }.
wh40k-neuroware-encode-short = There is too little in { THE($source) } for the stick to read.

## Synthetic / Cybernetic HUD lines

wh40k-neuroware-advice = A panel reboot clears running software.
wh40k-neuroware-crash-fault = POST-RUN THROTTLE ACTIVE
wh40k-neuroware-habit-loop = HABIT LOOP: process not found
wh40k-neuroware-false-fault-1 = SECTOR 0 CHECKSUM MISMATCH
wh40k-neuroware-false-fault-2 = COOLANT LOOP RUPTURE
wh40k-neuroware-false-fault-3 = MOTOR CORTEX UNRESPONSIVE
