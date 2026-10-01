# seeedstudio — RocketWelder plugins for Seeed Jetson carriers

## GenericSeeedWelder — `ModelingEvolution.WeldingMachine.Seeed.Plugin`

A welding machine (e.g. a plasma cutter) whose **only control input is a start contact**, driven from one
isolated digital output of the station computer itself: a **Seeed reComputer Industrial J40/J30** or
**reServer Industrial J4012** on JetPack 6.

ArcOn = output ON, ArcOff = output OFF. No robot, network or controller sits between rw2 and the output —
the write is a local ioctl on `/dev/gpiochip0`, followed by a read-back.

### Configuration (rw2 → Devices → Add device → "Generic welder (Seeed digital output)")

| Property | Value |
|---|---|
| `SeeedDoOutput` | **1–4** = terminal **DO1–DO4**. Required, no default — read it off the cabinet wiring. |

### Terminals → Jetson pins

Both carriers wire the DO terminals to the same pins (Seeed wiki, "Hardware and Interfaces Usage" for each
product). The plugin finds the line **by name**, so it does not depend on JetPack's GPIO numbering.

| Terminal | Line (BGA) | JetPack 6 | JetPack 5 sysfs |
|---|---|---|---|
| DO1 | `PI.00` | gpiochip0 51 | 399 |
| DO2 | `PI.01` | gpiochip0 52 | 400 |
| DO3 | `PI.02` | gpiochip0 53 | 401 |
| DO4 | `PH.07` | gpiochip0 50 | 398 |

### Wiring

Each DO is an optocoupled **sink**, **40 V / 40 mA max** per pin: load between +V and DOx, return on
**GND_DO** (pins 8/10). Seeed recommends a series resistor. 40 mA is a signal, not a contactor coil — use an
**interface relay or SSR with a low input current** (Fairino's control-box guide recommends < 20 mA for the
same job). Verify the relay's coil/input current on its datasheet.

### Safe state — what software can and cannot do

| Event | Output |
|---|---|
| Device attaches (boot, app start) | requested **LOW** |
| ArcOff, run STOP | **LOW**, confirmed by read-back, retried once |
| Device disposed | **LOW**, then released |
| SIGTERM / SIGHUP (`docker stop`, app restart) | **LOW** (signal hook) — measured on a reServer J4012 |
| **SIGKILL, crash, power loss of the Jetson only** | **UNCHANGED** — the kernel releases the line and leaves the pin as it was (measured) |

The last row is why the start contact should also go **through the E-stop / safety chain in hardware**.
A Fairino control-box DO differs here: the controller clears its own outputs on a stop.

### What the read-back proves

It reads the GPIO controller's **output register** — the SoC pin is driven. It does not see the
optocoupler, the terminal, the relay or an arc. Write + read-back takes < 1 ms.

### Host requirements

- rw2 with RocketWelder.SDK ≥ 2.18.0.
- The app container must see `/dev/gpiochip*` — the standard station compose runs `app` privileged, which
  is enough; otherwise add `devices: ["/dev/gpiochip0"]`.
- One device per terminal: a second device on the same DO fails with "already held".

### Install

Loopback-only on the station: `POST http://localhost/api/plugins/install` with
`{"packageId":"ModelingEvolution.WeldingMachine.Seeed.Plugin","version":"x.y.z"}`, then restart the app.

### Release

`./release.sh` cuts a `vX.Y.Z` tag; `.github/workflows/publish-nuget.yml` builds, tests and pushes to the
ModelingEvolution feed on a self-hosted runner. Never pack from a workstation.

### Bench probe

`src/SeeedDoProbe` drives the real welder code on a board:

```bash
dotnet publish src/SeeedDoProbe -c Release -r linux-arm64 --self-contained -p:PublishSingleFile=true -o out
scp out/SeeedDoProbe <board>:/tmp/
sudo /tmp/SeeedDoProbe cycle 1 300 3     # DO1 on/off 3x
sudo /tmp/SeeedDoProbe hold 1 on         # hold ON until SIGTERM
sudo cat /sys/kernel/debug/gpio | grep PI.00   # independent view: "rw2 DO1 ) out hi"
```

Do not run it while rw2 holds the same terminal — the request fails with "already held", by design.
