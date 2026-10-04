using System.Collections.Generic;
using System.Linq;

namespace InfoPanel.LogitechGamePanel
{
    public static class LogitechGamePanelModelDatabase
    {
        public const int LOGITECH_VENDOR_ID = 0x046D;
        public const int LOGITECH_PRODUCT_ID_MONOCHROME = 0xC226;

        //public const int ASROCK_VENDOR_ID = 0x26CE;
        public const int LOGITECH_PRODUCT_ID_COLOR = 0xC229;

        public static readonly (int Vid, int Pid)[] SupportedDevices =
        [
            (LOGITECH_VENDOR_ID, LOGITECH_PRODUCT_ID_MONOCHROME),
            (LOGITECH_VENDOR_ID, LOGITECH_PRODUCT_ID_COLOR),
        ];

        public static readonly Dictionary<LogitechGamePanelModel, LogitechGamePanelModelInfo> Models = new()
        {
            [LogitechGamePanelModel.Monochrome] = new LogitechGamePanelModelInfo
            {
                Model = LogitechGamePanelModel.Monochrome,
                Name = "Logitech Game Panel Monochrome",
                Width = 160,
                Height = 43,
                VendorId = LOGITECH_VENDOR_ID,
                ProductId = LOGITECH_PRODUCT_ID_MONOCHROME,
                LogiModel = LogitechGSDK.LOGI_LCD_TYPE_MONO,
            },
            [LogitechGamePanelModel.Color] = new LogitechGamePanelModelInfo
            {
                Model = LogitechGamePanelModel.Color,
                Name = "Logitech Game Panel Color",
                Width = 320,
                Height = 240,
                VendorId = LOGITECH_VENDOR_ID,
                ProductId = LOGITECH_PRODUCT_ID_COLOR,
                LogiModel = LogitechGSDK.LOGI_LCD_TYPE_COLOR,
            },
        };

        public static LogitechGamePanelModelInfo? GetModelByVidPid(int vid, int pid)
        {
            return Models.Values.FirstOrDefault(m => m.VendorId == vid && m.ProductId == pid);
        }
    }
}
