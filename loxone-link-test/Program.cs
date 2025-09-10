using loxonelinkdotnet;
using loxonelinkdotnet.Devices;
using loxonelinkdotnet.Devices.Extensions;
using loxonelinkdotnet.Devices.TreeDevices;
using Microsoft.Extensions.Logging;
using loxonelinkdotnet.NatProtocol;
using loxonelinkdotnet.NatProtocol.Constants;
using loxonelinkdotnet.Tools;
using System.Text.Json;

namespace loxonelinktest;

class Program
{
    static async Task Main(string[] args)
    {
        var logger = new BufferedLogger(); // buffer logs by default; only show in logs view

        // Load configuration
        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        Console.WriteLine($"Configuration file: {configPath}");

        // Check if configuration file exists
        if (!File.Exists(configPath))
        {
            Console.WriteLine("Configuration file not found! Generating default configuration...");
            var defaultConfig = AppConfig.CreateDefault();
            defaultConfig.Save(configPath);
            Console.WriteLine($"Default configuration saved to: {configPath}");
            Console.WriteLine("");
            Console.WriteLine("Please review and modify the configuration file as needed:");
            Console.WriteLine($"- CAN Interface Type: {defaultConfig.CanInterface.Type}");
            Console.WriteLine($"- COM Port: {defaultConfig.CanInterface.ComPort}");
            Console.WriteLine($"- Baud Rate: {defaultConfig.CanInterface.BaudRate}");
            Console.WriteLine($"- CAN Bit Rate: {defaultConfig.CanInterface.CanBitRate}");
            Console.WriteLine($"- Extension Serial: 0x{defaultConfig.TreeExtension.SerialNumber:X8}");
            Console.WriteLine("");
            Console.WriteLine("Run the application again after configuring your hardware settings.");
            return; // Exit the application
        }

        var config = AppConfig.Load(configPath);
        if (config == null)
        {
            Console.WriteLine("Failed to load configuration file. Please check the JSON format.");
            return; // Exit the application
        }

        Console.WriteLine("Configuration loaded successfully");
        Console.WriteLine($"CAN Interface: {config.CanInterface.Type} ({config.CanInterface.ComPort})");
        Console.WriteLine($"Extension Serial: 0x{config.TreeExtension.SerialNumber:X8}");
        Console.WriteLine("");

        // Create CAN interface shared by routines
        try
        {
            using var canBus = CanInterfaceFactory.CreateCanInterface(config.CanInterface, logger);
            canBus.StartReceiving();

            await ShowMainMenuAsync(config, canBus, logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize CAN interface");
            Console.WriteLine("Failed to initialize CAN interface. See logs view for details.");
            return; // Exit the application
        }
    }

    static async Task ShowMainMenuAsync(AppConfig config, ICanInterface canBus, BufferedLogger logger)
    {
        while (true)
        {
            Console.WriteLine("=== Loxone Link Test Menu ===");
            Console.WriteLine("1) CAN Sniffer");
            Console.WriteLine("2) Tree Extension + RGBW Devices");
            Console.WriteLine("3) DI Extension Test");
            Console.WriteLine("4) View Logs");
            Console.WriteLine("q) Quit");
            Console.Write("Select option: ");

            var key = Console.ReadKey(intercept: true);
            Console.WriteLine();
            switch (char.ToLower(key.KeyChar))
            {
                case '1':
                    await RunSnifferAsync(config, canBus, logger);
                    break;
                case '2':
                    await RunExtensionAsync(config, canBus, logger);
                    break;
                case '3':
                    await RunDiExtensionTestAsync(config, canBus, logger);
                    break;
                case '4':
                    await ShowLogsViewAsync(logger);
                    break;
                case 'q':
                    Console.WriteLine("Bye.");
                    return;
            }

            Console.WriteLine();
        }
    }

    static async Task RunSnifferAsync(AppConfig config, ICanInterface canBus, ILogger? logger)
    {
        Console.WriteLine("=== Loxone CAN Bus Sniffer ===");
        Console.WriteLine("This tool captures and analyzes Loxone device startup sequences");
        Console.WriteLine("Press 'q' to return to menu, 's' to save, 'c' to clear, 'a' to analyze");
        Console.WriteLine("");

        try
        {

            logger?.LogInformation($"Using CAN interface: {config.CanInterface.Type} ({config.CanInterface.ComPort})");
            Console.WriteLine($"Using CAN interface: {config.CanInterface.Type} ({config.CanInterface.ComPort})");
            Console.WriteLine("");


            // Create and start sniffer
            using var sniffer = new CanSniffer(canBus, logger);
            await sniffer.StartAsync();

            Console.WriteLine("Sniffer started. Power on/restart a device to capture.");
            Console.WriteLine("");

            // Handle user input for sniffer commands
            await HandleSnifferInputAsync(sniffer, logger);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Fatal error in sniffer");
        }
    }

    static async Task HandleSnifferInputAsync(CanSniffer sniffer, ILogger? logger)
    {
        await Task.Run(() =>
        {
            while (true)
            {
                try
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true);
                        switch (char.ToLower(key.KeyChar))
                        {
                            case 'q':
                                logger?.LogInformation("Stopping sniffer...");
                                sniffer.Stop();
                                return;
                            case 's':
                                // Save capture - handled by sniffer internally
                                break;
                            case 'c':
                                // Clear capture - handled by sniffer internally  
                                break;
                            case 'a':
                                // Analyze - handled by sniffer internally
                                break;
                        }
                    }
                    Thread.Sleep(100);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Error in sniffer input handler");
                    break;
                }
            }
        });
    }

    static async Task RunExtensionAsync(AppConfig config, ICanInterface canBus, BufferedLogger logger)
    {
        Console.WriteLine("=== Loxone Tree Extension Emulator ===");
        Console.WriteLine("Starting Tree Extension with RGBW Tree devices");
        Console.WriteLine("Commands: q=quit, t=test, r/g/b/w/o=set, s=send state, c=config, i=status, d=dashboard, l=logs, f=cycle log level, x=clear logs, 0-9 toggle DI");
        Console.WriteLine("");

        try
        {

            // Log the parsed serial numbers for debugging
            logger?.LogInformation($"Extension Serial Number: 0x{config.TreeExtension.SerialNumber:X8}");
            

            // Create extension with a configured serial number
            var extension = new TreeExtension(
                config.TreeExtension.SerialNumber,
                config.TreeExtension.HardwareVersion,
                config.TreeExtension.FirmwareVersion,
                new List<TreeDevice>(),
                canBus,
                logger);

            DIExtension dIextension = new DIExtension(
                config.TreeExtension.SerialNumber,
                canBus,
                logger);

            var rgbwDevices = new List<RgbwTreeDevice>();

            foreach (var deviceConfig in config.TreeExtension.RgwDimmerDevices)
            {
                if (deviceConfig != null)
                {
                    logger?.LogInformation($"Device Serial Number: 0x{deviceConfig.Serial:X8}");
                }

                // Create RGBW Tree device from configuration
                var rgbwDeviceSerial = deviceConfig?.Serial ?? 0x87654321u;
                var rgbwDevice = new LedSpotWwTreeDevice(rgbwDeviceSerial, deviceConfig.HardwareVersion, deviceConfig.FirmwareVersion, logger);
                rgbwDevices.Add(rgbwDevice);
                extension.AddDevice(rgbwDevice, deviceConfig.Branch);

                // Handle RGBW value changes (you can connect this to actual hardware)
                rgbwDevice.ValueChanged += (sender, value) =>
                {
                    logger?.LogInformation($"RGBW {((RgbwTreeDevice)sender!).SerialNumber:X8} -> {value}");
                };
            }

            // Add device to extension

            // Start the extension
            await extension.StartAsync();

            Console.WriteLine("Extension started. Press 'i' for status, 'l' to view logs.");

            // Handle console input for testing
            var exitRequested = false;
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    var key = Console.ReadKey(true);

                    switch (char.ToLower(key.KeyChar))
                    {
                        case 'q':
                            Console.WriteLine("Shutting down RGBW routine...");
                            await extension.StopAsync();
                            exitRequested = true;
                            return;
                            break;

                        case 't':
                            foreach (var d in rgbwDevices)
                                await TestRgbwValues(d, logger);
                            break;

                        case 'h':
                            ShowHelp(logger);
                            break;

                        case 's':
                            foreach (var d in rgbwDevices) await d.SendStateUpdateAsync();
                            Console.WriteLine("Sent current state to Miniserver (all RGBW devices)");
                            break;

                        case 'r':
                            foreach (var d in rgbwDevices) await d.SetRgbwAsync(100, 0, 0, 0); // Red
                            break;

                        case 'g':
                            foreach (var d in rgbwDevices) await d.SetRgbwAsync(0, 100, 0, 0); // Green
                            break;

                        case 'b':
                            foreach (var d in rgbwDevices) await d.SetRgbwAsync(0, 0, 100, 0); // Blue
                            break;

                        case 'w':
                            foreach (var d in rgbwDevices) await d.SetRgbwAsync(0, 0, 0, 100); // White
                            break;

                        case 'o':
                            foreach (var d in rgbwDevices) await d.SetRgbwAsync(0, 0, 0, 0); // Off
                            break;

                        case 'c':
                            // Display current configuration
                            Console.WriteLine("Current configuration:");
                            Console.WriteLine($"CAN Interface: {config.CanInterface.Type} ({config.CanInterface.ComPort})");
                            Console.WriteLine($"CAN Baud Rate: {config.CanInterface.BaudRate}, CAN Bit Rate: {config.CanInterface.CanBitRate}");
                            Console.WriteLine($"Extension Serial: 0x{config.TreeExtension.SerialNumber:X8}");
                            foreach (var d in rgbwDevices)
                                Console.WriteLine($"RGBW Device Serial: 0x{d.SerialNumber:X8}, DeviceNAT: {d.DeviceNat:X2}");
                            break;

                        case 'i':
                            // Show extension + RGBW + DI status
                            PrintExtensionAndRgbwStatus(extension, rgbwDevices);
                            PrintDiStatus(dIextension);
                            break;

                        case 'd':
                            await ShowStatusBoardAsync(extension, rgbwDevices, dIextension, logger);
                            Console.WriteLine("Returned from dashboard.");
                            break;

                        case 'l':
                            await ShowLogsViewAsync(logger);
                            Console.WriteLine("Returned from logs view.");
                            break;

                        case 'f':
                            var lvl = logger.CycleMinimumLevel();
                            Console.WriteLine($"Log level now: {lvl}");
                            break;

                        case 'x':
                            logger.Clear();
                            Console.WriteLine("Log buffer cleared.");
                            break;

                        case '0':
                        case '1':
                        case '2':
                        case '3':
                        case '4':
                        case '5':
                        case '6':
                        case '7':
                        case '8':
                        case '9':
                            // Toggle DI input
                            int inputNum = key.KeyChar - '0';
                            bool currentState = dIextension.GetDigitalInputState(inputNum);
                            bool newState = !currentState;
                            await dIextension.SetDigitalInputAsync(inputNum, newState);
                            Console.WriteLine($"DI {inputNum} toggled: {(currentState ? "HIGH" : "LOW")} -> {(newState ? "HIGH" : "LOW")}");
                            break;
                    }
                }
            });

            // Run until the input loop requests exit (on 'q')
            while (!exitRequested) await Task.Delay(250);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Fatal error in main loop");
        }
    }

    static async Task RunDiExtensionTestAsync(AppConfig config, ICanInterface canBus, BufferedLogger logger)
    {
        Console.WriteLine("=== DI Extension Test ===");
        Console.WriteLine("Commands: q=quit, i=status, d=dashboard, l=logs, f=cycle log level, x=clear logs, 0-9 toggle DI");

        try
        {
            DIExtension dIextension = new DIExtension(
                config.TreeExtension.SerialNumber,
                canBus,
                logger);

            // Minimal start/stop lifecycle if needed in future
            // For now, just interact via CAN handlers

            while (true)
            {
                var key = Console.ReadKey(true);
                switch (char.ToLower(key.KeyChar))
                {
                    case 'q':
                        Console.WriteLine("Returning to main menu...");
                        return;
                    case 'i':
                        PrintDiStatus(dIextension);
                        break;
                    case 'd':
                        await ShowStatusBoardAsync(null, new List<RgbwTreeDevice>(), dIextension, logger);
                        Console.WriteLine("Returned from dashboard.");
                        break;
                    case 'l':
                        await ShowLogsViewAsync(logger);
                        Console.WriteLine("Returned from logs view.");
                        break;
                    case 'f':
                        var lvl = logger.CycleMinimumLevel();
                        Console.WriteLine($"Log level now: {lvl}");
                        break;
                    case 'x':
                        logger.Clear();
                        Console.WriteLine("Log buffer cleared.");
                        break;
                    case '0':
                    case '1':
                    case '2':
                    case '3':
                    case '4':
                    case '5':
                    case '6':
                    case '7':
                    case '8':
                    case '9':
                        int inputNum = key.KeyChar - '0';
                        bool currentState = dIextension.GetDigitalInputState(inputNum);
                        bool newState = !currentState;
                        await dIextension.SetDigitalInputAsync(inputNum, newState);
                        Console.WriteLine($"DI {inputNum} toggled: {(currentState ? "HIGH" : "LOW")} -> {(newState ? "HIGH" : "LOW")}");
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Fatal error in DI Extension test");
        }
    }

    static async Task TestRgbwValues(RgbwTreeDevice device, ILogger? logger)
    {
        logger?.LogInformation("=== Testing RGBW Values ===");

        // Test sequence: Red -> Green -> Blue -> White -> Rainbow -> Off
        var testValues = new[]
        {
            (name: "Red", r: (byte)100, g: (byte)0, b: (byte)0, w: (byte)0),
            (name: "Green", r: (byte)0, g: (byte)100, b: (byte)0, w: (byte)0),
            (name: "Blue", r: (byte)0, g: (byte)0, b: (byte)100, w: (byte)0),
            (name: "White", r: (byte)0, g: (byte)0, b: (byte)0, w: (byte)100),
            (name: "Yellow", r: (byte)100, g: (byte)100, b: (byte)0, w: (byte)0),
            (name: "Magenta", r: (byte)100, g: (byte)0, b: (byte)100, w: (byte)0),
            (name: "Cyan", r: (byte)0, g: (byte)100, b: (byte)100, w: (byte)0),
            (name: "Warm White", r: (byte)50, g: (byte)30, b: (byte)0, w: (byte)70),
            (name: "Off", r: (byte)0, g: (byte)0, b: (byte)0, w: (byte)0)
        };

        foreach (var (name, r, g, b, w) in testValues)
        {
            logger.LogInformation($"Setting {name}: RGBW({r}%, {g}%, {b}%, {w}%)");
            await device.SetRgbwAsync(r, g, b, w);
            await Task.Delay(1000); // 1 second between changes
        }

        logger?.LogInformation("=== Test Complete ===");
    }

    static void ShowHelp(ILogger? logger)
    {
        Console.WriteLine("");
        Console.WriteLine("=== Available Commands ===");
        Console.WriteLine("q - Quit routine");
        Console.WriteLine("t - Run RGBW test sequence");
        Console.WriteLine("s - Send current state to Miniserver (all RGBW)");
        Console.WriteLine("r/g/b/w/o - Set color or off (all RGBW)");
        Console.WriteLine("c - Show current configuration");
        Console.WriteLine("i - Show status (extension, RGBW, DI)");
        Console.WriteLine("l - Open logs view");
        Console.WriteLine("0-9 - Toggle digital input 0-9");
        Console.WriteLine("");
    }

    static void PrintExtensionAndRgbwStatus(TreeExtension extension, List<RgbwTreeDevice> rgbwDevices)
    {
        Console.WriteLine("=== Extension Status ===");
        Console.WriteLine($"Serial: 0x{extension.SerialNumber:X8}");
        Console.WriteLine($"ExtensionNAT: 0x{extension.ExtensionNat:X2}");
        Console.WriteLine($"State: {extension.CurrentState} (Online={extension.IsOnline}, Parked={extension.IsParked})");
        Console.WriteLine($"Config CRC: 0x{extension.ConfigurationCrc:X8}");
        Console.WriteLine("");

        Console.WriteLine("=== RGBW Devices ===");
        if (rgbwDevices.Count == 0)
        {
            Console.WriteLine("No RGBW devices configured.");
        }
        else
        {
            foreach (var d in rgbwDevices)
            {
                Console.WriteLine($"- Serial 0x{d.SerialNumber:X8} | DeviceNAT: 0x{d.DeviceNat:X2} | State: {d.CurrentState} | Value: {d.CurrentValue}");
            }
        }
        Console.WriteLine("");
    }

    static void PrintDiStatus(DIExtension dIextension)
    {
        var currentBitmask = dIextension.GetDigitalInputBitmask();
        Console.WriteLine("=== Digital Input Status ===");
        Console.WriteLine($"Current bitmask: 0x{currentBitmask:X8}");
        Console.WriteLine("Individual inputs:");
        for (int i = 0; i < 32; i++)
        {
            bool state = dIextension.GetDigitalInputState(i);
            if (i < 10 || state) // Show first 10 inputs or any active input
            {
                Console.WriteLine($"  DI {i:D2}: {(state ? "HIGH" : "LOW ")}");
            }
        }
    }

    static async Task ShowLogsViewAsync(BufferedLogger logger)
    {
        Console.WriteLine("=== Logs View ===");
        Console.WriteLine("Keys: ESC/q=exit, f=cycle level, x=clear buffer");
        Console.WriteLine($"Current level: {logger.MinimumLevel}");
        logger.LiveOutputEnabled = true;

        // Print a snapshot first
        foreach (var entry in logger.Snapshot(200))
        {
            Console.WriteLine(entry.ToString());
        }

        // Now follow until ESC is pressed
        while (true)
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true);
                switch (char.ToLower(key.KeyChar))
                {
                    case (char)27:
                    case 'q':
                        logger.LiveOutputEnabled = false;
                        return;
                    case 'f':
                        var lvl = logger.CycleMinimumLevel();
                        Console.WriteLine($"Filter level: {lvl}");
                        break;
                    case 'x':
                        logger.Clear();
                        Console.WriteLine("Log buffer cleared.");
                        break;
                }
            }
            await Task.Delay(100);
        }
    }

    static async Task ShowStatusBoardAsync(TreeExtension? extension, List<RgbwTreeDevice> rgbwDevices, DIExtension? diExtension, BufferedLogger logger)
    {
        ConsoleKey exitKey = ConsoleKey.Escape;
        while (true)
        {
            Console.Clear();
            Console.WriteLine("=== Status Board (ESC/q to exit, l=logs, f=cycle level, x=clear logs) ===");
            Console.WriteLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"Log level: {logger.MinimumLevel}");
            Console.WriteLine("");

            if (extension != null)
            {
                Console.WriteLine("[Extension]");
                Console.WriteLine($"  Serial       : 0x{extension.SerialNumber:X8}");
                Console.WriteLine($"  Extension NAT: 0x{extension.ExtensionNat:X2}");
                Console.WriteLine($"  State        : {extension.CurrentState}");
                Console.WriteLine($"  Online       : {extension.IsOnline}");
                Console.WriteLine($"  Parked       : {extension.IsParked}");
                Console.WriteLine($"  Config CRC   : 0x{extension.ConfigurationCrc:X8}");
                Console.WriteLine("");
            }

            if (rgbwDevices != null && rgbwDevices.Count > 0)
            {
                Console.WriteLine("[RGBW Devices]");
                foreach (var d in rgbwDevices)
                {
                    Console.WriteLine($"  0x{d.SerialNumber:X8} NAT=0x{d.DeviceNat:X2} State={d.CurrentState} Value={d.CurrentValue}");
                }
                Console.WriteLine("");
            }

            if (diExtension != null)
            {
                Console.WriteLine("[DI Extension]");
                var bitmask = diExtension.GetDigitalInputBitmask();
                Console.WriteLine($"  Bitmask: 0x{bitmask:X8}");
                Console.Write("  Inputs : ");
                for (int i = 0; i < 16; i++)
                {
                    var state = diExtension.GetDigitalInputState(i) ? '1' : '0';
                    Console.Write(state);
                }
                Console.WriteLine();
                Console.WriteLine("");
            }

            // Non-blocking key handling with short sleep
            var t0 = DateTime.UtcNow;
            while ((DateTime.UtcNow - t0).TotalMilliseconds < 1000)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    switch (char.ToLower(key.KeyChar))
                    {
                        case (char)27:
                        case 'q':
                            return;
                        case 'l':
                            await ShowLogsViewAsync(logger);
                            Console.WriteLine("(resumed dashboard)");
                            break;
                        case 'f':
                            var lvl = logger.CycleMinimumLevel();
                            Console.WriteLine($"Filter level: {lvl}");
                            break;
                        case 'x':
                            logger.Clear();
                            Console.WriteLine("Log buffer cleared.");
                            break;
                    }
                }
                await Task.Delay(50);
            }
        }
    }
}
