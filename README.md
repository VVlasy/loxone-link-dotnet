<div align="center">

# loxone-link-dotnet

High-level .NET 8 library to emulate and talk to Loxone Link devices (Extensions and Tree devices) over the NAT protocol on CAN. Includes a menu‑driven test harness with a CAN sniffer, a Tree extension + RGBW device emulator, and a DI extension simulator.

</div>

## Highlights

- Robust NAT protocol implementation (29‑bit CAN IDs, fragmented payloads, state machine).
- Built‑in message handlers: Alive, Version, Ping, TimeSync, Identify, ExtensionsOffline, SearchDevices, WebServices text, Firmware Update, Crypto/Challenge.
- Tree bus support via `TreeExtension` with multiple Tree devices.
- Included Tree devices: `RgbwDimmerTreeDevice`, `LedSpotRgbwTreeDevice`, `LedSpotWwTreeDevice`.
- DI extension simulator for digital input bitmasks and frequency values.
- CAN adapters:
  - `SocketCan` (Linux) – simplified wrapper/scaffold.
  - `WaveshareSerialCan` (USB‑CAN on Serial/COM) – full variable‑length frame protocol.
- Test harness (`loxone-link-test`) with a menu, logs view, and live status dashboard.

## Repository Layout

```
loxone-link-dotnet/           # Reusable library (net8.0)
  Can/                        # CAN interfaces and sniffer
  Devices/                    # Extensions and Tree devices base + implementations
  Logging/                    # Simple logging abstraction
  NatProtocol/                # NAT frames, state machine, handlers, crypto

loxone-link-test/             # Interactive test harness (console)
  Program.cs                  # Menu: Sniffer, Tree+RGBW, DI test, Logs
  AppConfig.cs                # JSON config, default generation
  BufferedLogger.cs           # In‑memory log buffer + level filter
  CanInterfaceFactory.cs      # Adapter selection from config
```

## Install

This project is currently source‑based. Add the library as a project reference:

```bash
dotnet add <your-project>.csproj reference loxone-link-dotnet/loxone-link-dotnet.csproj
```

Target framework: `net8.0`.

## Quickstart (Library)

Create a Tree Extension with an RGBW device and start the lifecycle:

```csharp
using loxonelinkdotnet.Can.Adapters;
using loxonelinkdotnet.Devices.Extensions;
using loxonelinkdotnet.Devices.TreeDevices;
using loxonelinkdotnet.Logging;

var logger = new ConsoleLogger();

// Choose your CAN adapter
// var can = new SocketCan("can0", logger);              // Linux SocketCAN (scaffold)
var can = new WaveshareSerialCan("COM3", 2_000_000, 125_000, logger); // USB‑CAN on Serial

// Create a Tree Extension
var extension = new TreeExtension(
    serialNumber: 0x12345678,
    hardwareVersion: 2,
    firmwareVersion: 13030124,
    devices: Array.Empty<RgbwTreeDevice>(),
    canBus: can,
    logger: logger);

// Add one or more RGBW devices
var d1 = new RgbwDimmerTreeDevice(0x12AB34CD, 1, 13030124, logger);
extension.AddDevice(d1, loxonelinkdotnet.Devices.TreeBranches.Right);

await extension.StartAsync();    // Starts offer → assignment → runtime
// Respond to Miniserver commands now; control devices via SetRgbwAsync(...)
```

DI extension simulator:

```csharp
using loxonelinkdotnet.Devices.Extensions;
var di = new DIExtension(0x12345678, can, logger);
await di.StartAsync();
await di.SetDigitalInputAsync(0, true); // Toggle inputs and send bitmask
```

## Test Harness (Console)

The `loxone-link-test` app provides a convenient way to run end‑to‑end:

```bash
dotnet run --project loxone-link-test
```

Menu options:

- 1) CAN Sniffer
- 2) Tree Extension + RGBW Devices
- 3) DI Extension Test
- 4) View Logs

Inside routines:

- RGBW: `t` test sequence, `r/g/b/w/o` set color/off, `s` send state, `i` status, `d` dashboard, `l` logs, `f` cycle log level, `x` clear buffer, `0–9` toggle DI, `q` back.
- DI: `i` status, `d` dashboard, `l` logs, `f` cycle log level, `x` clear buffer, `0–9` toggle inputs, `q` back.
- Logs view: `f` filter level, `x` clear, `ESC/q` exit.

On first run the app will create `config.json` next to the executable with sensible defaults (Serial adapter on `COM3`, 2,000,000 bps, CAN 125,000 bps). You can enter serial numbers as hex (e.g. `"0x12345678"`) or MAC‑like (`"0C:DD:22:01"`).

## Features (Library)

- NAT frames: encoding/decoding (`NatFrame`), STM32‑compatible CRC (`Stm32Crc`).
- State machine: `DeviceStateMachine` with Offer/Assignment/Parked/Online and keep‑alive handling.
- Message handlers (non‑exhaustive):
  - Alive, VersionRequest, Ping, TimeSync, Identify, IdentifyUnknownExtension
  - SearchDevices, ExtensionsOffline, WebServices text requests
  - Fragmented messages: SendConfig, CryptChallenge, FirmwareUpdate
- Tree devices and routing via `TreeExtension` (forwards/broadcasts, parked handling).
- RGBW devices: standard (0x84) and composite RGBW (0x88, fade/jump) handlers.
- DI extension: bitmask and frequency value updates, input toggling.

## CAN Adapters

- `SocketCan` (Linux): simplified wrapper and background receive loop scaffold. Replace with real P/Invoke for production.
- `WaveshareSerialCan` (USB‑CAN): variable‑length frame protocol, sequence numbers, background parsing, send/receive events.

## Sniffer

`CanSniffer` captures CAN frames, serializes to JSON, and supports basic analysis controls. It hooks into `ICanInterface` events and timestamps frames with a sequence number for ordering.

## Security & Keys

Crypto helpers (`LoxoneCryptoCanAuthentication`) include hashing and AES‑CBC routines used during challenges and legacy device flows. For real devices you must obtain the proper AES keys and related material from your own hardware.

- To extract the necessary keys, use the script maintained by sarnau: [downloadLoxoneAESKeys.py](https://github.com/sarnau/Inside-The-Loxone-Miniserver/blob/master/Code/downloadLoxoneAESKeys.py)
- Store the keys securely and load/use them according to your jurisdiction and device ownership.
- Values in this repository are for development only. Ensure you understand the legal and security implications before using any keys in production or distributing them.

## Building

```bash
dotnet build -c Release
```

Run test harness:

```bash
dotnet run --project loxone-link-test
```

On Linux with SocketCAN you may need elevated privileges to access CAN sockets.

## Roadmap

- SocketCAN P/Invoke implementation and filters.
- More Tree device types (Touch, Motion, etc.).
- Better WebServices examples and config management.
- Extended sniffer analysis and export tools.

## Acknowledgements

Massive thanks to sarnau and the Inside‑The‑Loxone‑Miniserver project:

- https://github.com/sarnau/Inside-The-Loxone-Miniserver

Without that research and tooling, this project would not have been possible.

## Contributing

Bug reports and PRs are welcome. Please keep changes minimal and focused. For larger features, open an issue first to discuss design and scope.

## License

Licensed under the terms in `LICENSE`.

Loxone is a trademark of Loxone Electronics GmbH. This project is independent and for interoperability, testing, and research purposes.
