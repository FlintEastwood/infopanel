using HidSharp;
using Sentry.Protocol;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Devices.HumanInterfaceDevice;
using Windows.Media.Core;
//using LogitechLcdEnginesWrapper;

namespace InfoPanel.LogitechGamePanel
{
    public class LogitechGamePanelDiscoveryInfo 
    {
        public string DeviceId { get; set; } = "";
        public string DeviceLocation { get; set; } = "";
        public string DevicePath { get; set; } = "";
        public int VendorId { get; set; }
        public int ProductId { get; set; }
        public LogitechGamePanelModel Model { get; set; }
        public LogitechGamePanelModelInfo? ModelInfo { get; set; }
        public int LogiModel { get; set; }
    }

    public static class LogitechGamePanelHelper 
    {
        private static readonly ILogger Logger = Log.ForContext(typeof(LogitechGamePanelHelper));

        //private const int MIN_OUTPUT_REPORT_LENGTH = 1025; // 1024 data + 1 null report ID
        //private const int MIN_OUTPUT_REPORT_LENGTH = 0; // 1024 data + 1 null report ID

        /// <summary>
        /// Scans for connected Logitech Game panels via HidSharp.
        /// </summary>
        //public static List<LogitechGamePanelDiscoveryInfo> ScanDevices()
        public static List<LogitechGamePanelDiscoveryInfo> ScanDevices()
        {
            var devices = new List<LogitechGamePanelDiscoveryInfo>();
            /*
            foreach (var (vendorId, productId) in LogitechGamePanelModelDatabase.SupportedDevices)
            {
                Logger.Information("LogitechGamePanelHelper: Scanning VID={Vid:X4} PID={Pid:X4}", vendorId, productId);

                var deviceList = DeviceList.Local;
                var hidDevices = deviceList.GetHidDevices(vendorId, productId).ToList();

                foreach (var hidDevice in hidDevices)
                {
                    try
                    {
                        if (hidDevice.GetMaxOutputReportLength() < MIN_OUTPUT_REPORT_LENGTH)
                        {
                            Logger.Debug("LogitechGamePanelHelper: Skipping {Path}, MaxOut={MaxOut}",
                                hidDevice.DevicePath, hidDevice.GetMaxOutputReportLength());
                            continue;
                        }
                        
                        var modelInfo = LogitechGamePanelModelDatabase.GetModelByVidPid(vendorId, productId);
                        if (modelInfo == null) continue;

                        // Extract DeviceId from path (e.g., VID_264A&PID_2347\SERIAL)
                        string deviceId = ExtractDeviceId(hidDevice.DevicePath);
                        string deviceLocation = ExtractDeviceLocation(hidDevice.DevicePath);

                        Logger.Information("LogitechGamePanelHelper: Found {Model} at {Path}",
                            modelInfo.Name, hidDevice.DevicePath);

                        devices.Add(new LogitechGamePanelDiscoveryInfo
                        {
                            DeviceId = deviceId,
                            DeviceLocation = deviceLocation,
                            DevicePath = hidDevice.DevicePath,
                            VendorId = vendorId,
                            ProductId = productId,
                            Model = modelInfo.Model,
                            ModelInfo = modelInfo,
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning(ex, "LogitechGamePanelHelper: Error scanning {Path}", hidDevice.DevicePath);
                    }
                }
            }
            */
            if (LogitechGSDK.LogiLcdInit("InfoPanel", LogitechGSDK.LOGI_LCD_TYPE_MONO | LogitechGSDK.LOGI_LCD_TYPE_COLOR))
            {
                Logger.Information("LogitechGamePanelHelper: Logitech GSDK initialized Logitech Game Panel LCD");
                if (LogitechGSDK.LogiLcdIsConnected(LogitechGSDK.LOGI_LCD_TYPE_MONO))
                {
                    Logger.Information("LogitechGamePanelHelper: Logitech GSDK reports a connected monochrome LCD");
                    devices.Add(new LogitechGamePanelDiscoveryInfo
                    {
                        DeviceId = "Monochrome",
                        DeviceLocation = "LogitechGSDK",
                        DevicePath = "LogitechGSDKPath",
                        VendorId = 0x046D,
                        ProductId = 0xC226,
                        Model = LogitechGamePanelModel.Monochrome,
                        ModelInfo = LogitechGamePanelModelDatabase.GetModelByVidPid(0x046D, 0xC226),
                        LogiModel = LogitechGSDK.LOGI_LCD_TYPE_MONO,
                    });
                }
                if (LogitechGSDK.LogiLcdIsConnected(LogitechGSDK.LOGI_LCD_TYPE_COLOR))
                {
                    Logger.Information("LogitechGamePanelHelper: Logitech GSDK reports a connected color LCD");
                    devices.Add(new LogitechGamePanelDiscoveryInfo
                    {
                        DeviceId = "Color",
                        DeviceLocation = "LogitechGSDK",
                        DevicePath = "LogitechGSDKPath",
                        VendorId = 0x046D,
                        ProductId = 0xC229,
                        Model = LogitechGamePanelModel.Color,
                        ModelInfo = LogitechGamePanelModelDatabase.GetModelByVidPid(0x046D, 0xC229),
                        LogiModel = LogitechGSDK.LOGI_LCD_TYPE_COLOR,
                    });
                }
                LogitechGSDK.LogiLcdShutdown();
            }
            else
            {
                Logger.Warning("LogitechGamePanelHelper: Logitech GSDK failed to initialize Logitech Game Panel LCD");
            }

            Logger.Information("LogitechGamePanelHelper: Found {Count} device(s)", devices.Count);
            return devices;
        }

        private static string ExtractDeviceId(string devicePath)
        {
            // HID path: \\?\hid#vid_264a&pid_2347#serial#{guid}
            try
            {
                var parts = devicePath.Split('#');
                if (parts.Length >= 3)
                    return $"{parts[1]}\\{parts[2]}";
            }
            catch { }
            return devicePath;
        }

        private static string ExtractDeviceLocation(string devicePath)
        {
            // Extract hub/port info from path
            try
            {
                var parts = devicePath.Split('#');
                if (parts.Length >= 3)
                    return parts[2];
            }
            catch { }
            return "";
        }
    }
}
