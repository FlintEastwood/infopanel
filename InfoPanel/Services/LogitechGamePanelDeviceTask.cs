using InfoPanel.Drawing;
using InfoPanel.Extensions;
using InfoPanel.LogitechGamePanel;
using InfoPanel.Models;
using InfoPanel.Utils;
using Serilog;
using SkiaSharp;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Vortice.Mathematics;

namespace InfoPanel.Services
{
    public sealed class LogitechGamePanelDeviceTask : BackgroundTask
    {
        private static readonly ILogger Logger = Log.ForContext<LogitechGamePanelDeviceTask>();

        private readonly LogitechGamePanelDevice _device;
        private int _panelWidth;
        private int _panelHeight;
        private int _lastBrightness;

        public LogitechGamePanelDeviceTask(LogitechGamePanelDevice device)
        {
            _device = device;
        }

        protected override async Task DoWorkAsync(CancellationToken token)
        {
            var modelInfo = _device.ModelInfo;
            if (modelInfo == null)
            {
                _device.UpdateRuntimeProperties(errorMessage: "Unknown model");
                return;
            }

            _panelWidth = modelInfo.Width;
            _panelHeight = modelInfo.Height;
            _lastBrightness = _device.Brightness;

            _device.UpdateRuntimeProperties(isRunning: false, errorMessage: string.Empty);
            _device.RuntimeProperties.Name = $"{modelInfo.Name} ({_panelWidth}x{_panelHeight})";

            // Retry loop with backoff
            int retryCount = 0;
            while (!token.IsCancellationRequested)
            {
                //LogitechGamePanelHidDevice? hidDevice = null;
                try
                {
                    Logger.Information("LogitechGamePanelDevice {Device}: Init (attempt {Retry})", modelInfo.LogiModel, retryCount + 1);
                    if (!LogitechGSDK.LogiLcdInit("InfoPanel", modelInfo.LogiModel))
                    {
                        _device.UpdateRuntimeProperties(errorMessage: "LogitechGSDK Init Error!!!");
                        await Task.Delay(retryCount < 3 ? 1000 : 5000, token);
                        retryCount++;
                        continue;
                    }
                    //Logger.Information("LogitechGamePanelDevice {Device}: LogiLcdInit succeeded", _device.DeviceId);
                    if (!LogitechGSDK.LogiLcdIsConnected(modelInfo.LogiModel))
                    {
                        Logger.Warning("LogitechGamePanelDevice {Device}: LogiLcd Is NOT Connected", _device.DeviceId);
                        await Task.Delay(2000, token);
                        retryCount++;
                        continue;
                    }
                    //LogitechGSDK.LogiLcdShutdown(); // Shutdown the SDK to avoid conflicts with HID device
                    
                    //Logger.Information("LogitechGamePanelDevice {Device}: Opening (attempt {Retry})", _device, retryCount + 1);
                    //hidDevice = LogitechGamePanelHidDevice.Open(modelInfo.VendorId, modelInfo.ProductId);
                    /*
                    if (hidDevice == null)
                    {
                        _device.UpdateRuntimeProperties(errorMessage: "Device not found");
                        await Task.Delay(retryCount < 3 ? 1000 : 5000, token);
                        retryCount++;
                        continue;
                    }

                    // Handshake
                    Logger.Information("LogitechGamePanelDevice {Device}: Handshake", _device);
                    if (!hidDevice.Handshake())
                    {
                        _device.UpdateRuntimeProperties(errorMessage: "Handshake failed");
                        hidDevice.Dispose();
                        await Task.Delay(2000, token);
                        retryCount++;
                        continue;
                    }
                    */
                    retryCount = 0;

                    // Render and send loop
                    //await RunRenderSendLoop(hidDevice, token);
                    await RunRenderSendLoop(modelInfo.LogiModel, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "LogitechGamePanelDevice {Device}: Error", _device);
                    _device.UpdateRuntimeProperties(errorMessage: ex.Message);
                    retryCount++;
                }
                finally
                {
                    //hidDevice?.Dispose();
                    LogitechGSDK.LogiLcdShutdown();
                    _device.UpdateRuntimeProperties(isRunning: false);
                }

                if (!token.IsCancellationRequested)
                    await Task.Delay(retryCount < 3 ? 1000 : 5000, token);
            }
        }

        //private async Task RunRenderSendLoop(LogitechGamePanelHidDevice hidDevice, CancellationToken token)
        private async Task RunRenderSendLoop(int LogiModel, CancellationToken token)
        {
            FpsCounter fpsCounter = new(60);
            byte[]? latestFrame = null;
            AutoResetEvent frameAvailable = new(false);

            var renderCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var renderToken = renderCts.Token;

            _device.UpdateRuntimeProperties(isRunning: true, errorMessage: string.Empty);

            var renderTask = Task.Run(async () =>
            {
                Thread.CurrentThread.Name ??= $"LogitechGamePanel-Render-{_device.DeviceLocation}";
                try
                {
                    var stopwatch = new Stopwatch();
                    while (!renderToken.IsCancellationRequested)
                    {
                        stopwatch.Restart();

                        var frame = GenerateJpegBuffer();
                        Interlocked.Exchange(ref latestFrame, frame);
                        frameAvailable.Set();

                        var targetFrameTime = 1000 / Math.Max(1, _device.TargetFrameRate);
                        var elapsedMs = (int)stopwatch.ElapsedMilliseconds;
                        var adaptiveFrameTime = targetFrameTime - elapsedMs;
                        if (adaptiveFrameTime > 0)
                            await Task.Delay(adaptiveFrameTime, token);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception e)
                {
                    Logger.Error(e, "LogitechGamePanelDevice {Device}: Render error", _device);
                    _device.UpdateRuntimeProperties(errorMessage: e.Message);
                    renderCts.Cancel();
                }
            }, renderToken);

            var sendTask = Task.Run(() =>
            {
                Thread.CurrentThread.Name ??= $"LogitechGamePanel-Send-{_device.DeviceLocation}";
                try
                {
                    var stopwatch = new Stopwatch();
                    while (!token.IsCancellationRequested)
                    {
                        if (frameAvailable.WaitOne(100))
                        {
                            var jpegData = Interlocked.Exchange(ref latestFrame, null);
                            if (jpegData != null)
                            {
                                stopwatch.Restart();

                                //hidDevice.SendJpegFrame(jpegData);
                                LogitechGSDK.LogiLcdMonoSetBackground(jpegData);
                                LogitechGSDK.LogiLcdUpdate();

                                fpsCounter.Update(stopwatch.ElapsedMilliseconds);
                                _device.UpdateRuntimeProperties(frameRate: fpsCounter.FramesPerSecond, frameTime: fpsCounter.FrameTime);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.Error(e, "LogitechGamePanelDevice {Device}: Send error", _device);
                    _device.UpdateRuntimeProperties(errorMessage: e.Message);
                }
                finally
                {
                    renderCts.Cancel();
                }
            }, token);

            await Task.WhenAll(renderTask, sendTask);

            frameAvailable.Dispose();
            renderCts.Dispose();
        }

        private byte[] GenerateJpegBuffer()
        {
            var profileGuid = _device.ProfileGuid;

            if (ConfigModel.Instance.GetProfile(profileGuid) is Profile profile)
            {
                var rotation = _device.Rotation;

                using var bitmap = PanelDrawTask.RenderSK(profile, false,
                    colorType: SKColorType.Rgba8888,
                    //colorType: SKColorType.Gray8);
                    alphaType: SKAlphaType.Opaque);

                using var resizedBitmap = SKBitmapExtensions.EnsureBitmapSize(bitmap, _panelWidth, _panelHeight, rotation);

                SKBitmap encodeBitmap = resizedBitmap;
                SKBitmap? dimmed = null;
                try
                {
                    if (_device.Brightness < 100)
                    {
                        dimmed = ApplyBrightness(resizedBitmap);
                        encodeBitmap = dimmed;
                    }
                    // Convert to Gray8 for Logitech Monochrome GamePanel
                    dimmed = encodeBitmap.Copy(SKColorType.Gray8);
                    encodeBitmap = dimmed;
                    // Convert to Black and White with threshold for Logitech Monochrome GamePanel
                    dimmed = ConvertToBW(encodeBitmap,128);
                    encodeBitmap = dimmed;

                    if (_device.Invert)
                    {
                        dimmed = InvertBitmap(encodeBitmap);
                        encodeBitmap = dimmed;
                    }

                    int quality = _device.JpegQuality;
                //using var image = SKImage.FromBitmap(encodeBitmap);
                //using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
                    //return data.ToArray();
                    return encodeBitmap.Bytes;

                }
                finally
                {
                    dimmed?.Dispose();
                }
            }

            // No profile selected: return a black JPEG
            return GenerateBlackJpeg();
        }

        private SKBitmap ApplyBrightness(SKBitmap source)
        {
            //float scale = Math.Clamp(_device.Brightness, 0, 100) / 100f;
            float scale = _device.Brightness / 100f;
            var result = new SKBitmap(source.Width, source.Height, source.ColorType, source.AlphaType);
            using var canvas = new SKCanvas(result);
            using var paint = new SKPaint();
            paint.ColorFilter = SKColorFilter.CreateColorMatrix(
            [
                scale, 0,     0,     0, 0,
                0,     scale, 0,     0, 0,
                0,     0,     scale, 0, 0,
                0,     0,     0,     1, 0
            ]);
            canvas.DrawBitmap(source, 0, 0, paint);
            return result;
        }

        private SKBitmap ConvertToBW(SKBitmap sourceBitmap, byte threshold = 128)
        {
            // Ensure the source is Gray8
            if (sourceBitmap.ColorType != SKColorType.Gray8)
            {
                throw new ArgumentException("Bitmap must be of type Gray8.");
            }

            // Create a new bitmap with the same dimensions and Gray8 color type
            var bwBitmap = new SKBitmap(sourceBitmap.Width, sourceBitmap.Height, SKColorType.Gray8, sourceBitmap.AlphaType);

            // Get pointer to the pixel data safely using Span or unsafe pointers
            unsafe
            {
                byte* srcPtr = (byte*)sourceBitmap.GetPixels();
                byte* dstPtr = (byte*)bwBitmap.GetPixels();
                int totalBytes = sourceBitmap.RowBytes * sourceBitmap.Height;

                for (int i = 0; i < totalBytes; i++)
                {
                    // Apply threshold: if pixel value is greater than threshold, make it white (255), else black (0)
                    dstPtr[i] = srcPtr[i] >= threshold ? (byte)255 : (byte)0;
                }
            }

            return bwBitmap;
        }
        private SKBitmap InvertBitmap(SKBitmap source)
        {
            float scale = Math.Clamp(_device.Brightness, 0, 100) / 100f;
            var result = new SKBitmap(source.Width, source.Height, source.ColorType, source.AlphaType);
            using var canvas = new SKCanvas(result);
            using var paint = new SKPaint();
            paint.ColorFilter = SKColorFilter.CreateColorMatrix(
            [
               -1,     0,     0,     0, 1,
                0,    -1,     0,     0, 1,
                0,     0,    -1,     0, 1,
                0,     0,     0,     1, 0
            ]);
            canvas.DrawBitmap(source, 0, 0, paint);
            return result;
        }

        private byte[]? _cachedBlackJpeg;

        private byte[] GenerateBlackJpeg()
        {
            if (_cachedBlackJpeg != null) return _cachedBlackJpeg;

            //using var bitmap = new SKBitmap(_panelWidth, _panelHeight, SKColorType.Rgba8888, SKAlphaType.Opaque);
            using var bitmap = new SKBitmap(_panelWidth, _panelHeight, SKColorType.Gray8, SKAlphaType.Opaque);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Black);
            //using var image = SKImage.FromBitmap(bitmap);
            //using var data = image.Encode(SKEncodedImageFormat.Jpeg, 50);
            //_cachedBlackJpeg = data.ToArray();
            //return _cachedBlackJpeg;
            return bitmap.Bytes;
        }
    }
}
