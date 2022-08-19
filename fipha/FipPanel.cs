using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Media.Imaging;
using HADotNet.Core;
using HADotNet.Core.Clients;
using HADotNet.Core.Models;
using TheArtOfDev.HtmlRenderer.WinForms;
using RazorEngine;
using RazorEngine.Templating;
using RazorEngine.Text;
using TheArtOfDev.HtmlRenderer.Core.Entities;
using Image = System.Drawing.Image;
using System.Globalization;

// For extension methods.


// ReSharper disable IdentifierTypo
// ReSharper disable StringLiteralTypo
// ReSharper disable CommentTypo

namespace fipha
{

    public enum LcdPage
    {
        Collapsed = -1,
        HomeMenu = 0,
        NowPlayingMenu = 1,
        SensorMenu = 2
        
    }

    public enum LcdTab
    {
        Init = -999,
        None = 0,

        NowPlayingMenu = 1,
        SensorMenu = 2, 

        //---------------

        NowPlayingBack = 7,
        NowPlayingPage1 = 8,
        NowPlayingPage2 = 9,
        NowPlayingPage3 = 10,
        NowPlayingPage4 = 11,
        NowPlayingPage5 = 12,

        //---------------

        SensorBack = 13,
        SensorPage1 = 14,
        SensorPage2 = 15,
        SensorPage3 = 16,
        SensorPage4 = 17,
        SensorPage5 = 18

    }

    public class MyHtmlHelper
    {
        public IEncodedString Raw(string rawString)
        {
            return new RawString(rawString);
        }
    }

    public abstract class HtmlSupportTemplateBase<T> : TemplateBase<T>
    {
        public HtmlSupportTemplateBase()
        {
            Html = new MyHtmlHelper();
        }

        public MyHtmlHelper Html { get; set; }
    }

    internal class FipPanel
    {
        const long PAUSE = 1;
        const long SEEK = 2;
        const long VOLUME_SET = 4;
        const long VOLUME_MUTE = 8;
        const long PREVIOUS_TRACK = 16;
        const long NEXT_TRACK = 32;

        const long TURN_ON = 128;
        const long TURN_OFF = 256;
        const long PLAY_MEDIA = 512;
        const long VOLUME_STEP = 1024;
        const long SELECT_SOURCE = 2048;
        const long STOP = 4096;
        const long CLEAR_PLAYLIST = 8192;
        const long PLAY = 16384;
        const long SHUFFLE_SET = 32768;
        const long SELECT_SOUND_MODE = 65536;
        const long BROWSE_MEDIA = 131072;
        const long REPEAT_SET = 262144;
        const long GROUPING = 524288;

        private readonly object _refreshDevicePageLock = new object();

        private bool _initOk;

        public LcdTab CurrentTab = LcdTab.None;
        public int[] CurrentCard = new int[100];

        private LcdPage _currentPage = LcdPage.Collapsed;
        private LcdTab _currentTabCursor = LcdTab.None;
        private LcdTab _lastTab = LcdTab.Init;
        private string _settingsPath;

        private const int DEFAULT_PAGE = 0;

        private int _currentLcdYOffset;
        private int _currentLcdHeight;

        public IntPtr FipDevicePointer;
        private string SerialNumber;

        private uint _prevButtons;

        private bool[] _ledState = new bool[7];

        private List<uint> _pageList = new List<uint>();

        private readonly Pen _scrollPen = new Pen(Color.FromArgb(0xff,0xFF,0xB0,0x00));
        private readonly Pen _whitePen = new Pen(Color.FromArgb(0xff, 0xFF, 0xFF, 0xFF),(float)0.1);
        private readonly Pen _grayPen = new Pen(Color.FromArgb(0xff, 0xd3, 0xd3, 0xd3), (float)0.1);


        private readonly SolidBrush _scrollBrush = new SolidBrush(Color.FromArgb(0xff, 0xFF, 0xB0, 0x00));
        private readonly SolidBrush _whiteBrush = new SolidBrush(Color.FromArgb(0xff, 0xFF, 0xFF, 0xFF));

        private readonly Font _drawFont = new Font("Arial", 13, GraphicsUnit.Pixel);

        private Image _htmlImage;
        private Image _menuHtmlImage;
        private Image _cardcaptionHtmlImage;

        private const int HtmlMenuWindowWidth = 110; //69;
        private const int HtmlMenuWindowHeight = 259;
        private const int HtmlWindowXOffset = 1;

        private const int HtmlWindowWidth = 320;
        private const int HtmlWindowHeight = 240;

        private int HtmlWindowUsableWidth => HtmlWindowWidth - 9 - HtmlWindowXOffset;

        private double ScrollBarHeight => HtmlWindowHeight -7.0;

        public static int ChartImageDisplayWidth => HtmlWindowWidth - 25;

        public const int ChartImageDisplayHeight = 60;

        private DirectOutputClass.PageCallback _pageCallbackDelegate;
        private DirectOutputClass.SoftButtonCallback _softButtonCallbackDelegate;

        private bool _blockNextUpState;

        public FipPanel(IntPtr devicePtr) 
        {
            FipDevicePointer = devicePtr;
        }

        private void InitFipPanelSerialNumber()
        {
            App.Log.Info("FipPanel Serial Number : " + SerialNumber);

            _settingsPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) +
                            "\\mhwlng\\fip-ha\\" + SerialNumber;

            if (File.Exists(_settingsPath))
            {
                try
                {
                    CurrentTab = (LcdTab)uint.Parse(File.ReadAllText(_settingsPath));
                }
                catch
                {
                    CurrentTab = LcdTab.None;
                }
            }
            else
            {
                new FileInfo(_settingsPath).Directory?.Create();

                File.WriteAllText(_settingsPath, ((int)CurrentTab).ToString());
            }
        }

        public void Initalize()
        {
            // FIP = 3e083cd8-6a37-4a58-80a8-3d6a2c07513e

            // https://github.com/Raptor007/Falcon4toSaitek/blob/master/Raptor007's%20Falcon%204%20to%20Saitek%20Utility/DirectOutput.h
            //https://github.com/poiuqwer78/fip4j-core/tree/master/src/main/java/ch/poiuqwer/saitek/fip4j

            _pageCallbackDelegate = PageCallback;
            _softButtonCallbackDelegate = SoftButtonCallback;

            var returnValues1 = DirectOutputClass.RegisterPageCallback(FipDevicePointer, _pageCallbackDelegate);
            if (returnValues1 != ReturnValues.S_OK)
            {
                App.Log.Error("FipPanel failed to init RegisterPageCallback. " + returnValues1);
            }
            var returnValues2 = DirectOutputClass.RegisterSoftButtonCallback(FipDevicePointer, _softButtonCallbackDelegate);
            if (returnValues2 != ReturnValues.S_OK)
            {
                App.Log.Error("FipPanel failed to init RegisterSoftButtonCallback. " + returnValues1);
            }

            var returnValues3 = DirectOutputClass.GetSerialNumber(FipDevicePointer, out SerialNumber);
            if (returnValues3 != ReturnValues.S_OK)
            {
                App.Log.Error("FipPanel failed to get Serial Number. " + returnValues1);
            }
            else
            {
                InitFipPanelSerialNumber();

                _initOk = true;

                AddPage(DEFAULT_PAGE, true);

                RefreshDevicePage();
            }

        }

        public void Shutdown()
        {
            try
            {
                if (_pageList.Count > 0)
                {
                    do
                    {
                        if (_initOk)
                        {
                            DirectOutputClass.RemovePage(FipDevicePointer, _pageList[0]);
                        }

                        _pageList.Remove(_pageList[0]);


                    } while (_pageList.Count > 0);
                }
            }
            catch (Exception ex)
            {
                App.Log.Error(ex);
            }

        }

        private bool SetTab(LcdTab tab)
        {
            if (CurrentTab != tab)
            {
                _lastTab = CurrentTab;
                CurrentTab = tab;


                _currentLcdYOffset = 0;


                File.WriteAllText(_settingsPath, ((int)CurrentTab).ToString());

            }

            _currentPage = LcdPage.Collapsed;
            _currentTabCursor = LcdTab.None;

            return true;
        }
        private void PageCallback(IntPtr device, IntPtr page, byte bActivated, IntPtr context)
        {
            if (device == FipDevicePointer)
            {
                if (bActivated != 0)
                {
                    RefreshDevicePage();
                }
            }
        }

        private void SoftButtonCallback(IntPtr device, IntPtr buttons, IntPtr context)
        {
            if (device == FipDevicePointer & (uint) buttons != _prevButtons)
            {
                var button = (uint) buttons ^ _prevButtons;
                var state = ((uint) buttons & button) == button;
                _prevButtons = (uint) buttons;

                //Console.WriteLine($"button {button}  state {state}");

                var mustRefresh = false;

                var mustRender = true;

                var serviceClient = ClientFactory.GetClient<ServiceClient>();

                var currentMediaPlayerState = string.Empty;
                var currentMediaPlayerKey = string.Empty;

                var pause = false;
                var previousTrack = false;
                var nextTrack = false;
                var play = false;

                switch (CurrentTab)
                {
                    case LcdTab.NowPlayingPage1:
                    case LcdTab.NowPlayingPage2:
                    case LcdTab.NowPlayingPage3:
                    case LcdTab.NowPlayingPage4:
                    case LcdTab.NowPlayingPage5:

                        if ((int)CurrentTab - (int)LcdTab.NowPlayingPage1 < App.NowPlayingPages.Count)
                        {
                            var currentMediaPlayer =
                                ((StateObject)(App.NowPlayingPages[(int)CurrentTab - (int)LcdTab.NowPlayingPage1])?.State);

                            if (currentMediaPlayer != null)
                            {
                                currentMediaPlayerState = currentMediaPlayer.State;
                                currentMediaPlayerKey = currentMediaPlayer.EntityId;

                                if (currentMediaPlayerState != "idle" &&
                                    currentMediaPlayer.Attributes.ContainsKey("supported_features"))
                                {
                                    var supportedFeatures = (long)currentMediaPlayer.Attributes["supported_features"];

                                    pause = (supportedFeatures & PAUSE) > 0;
                                    previousTrack = (supportedFeatures & PREVIOUS_TRACK) > 0;
                                    nextTrack = (supportedFeatures & NEXT_TRACK) > 0;
                                    play = (supportedFeatures & PLAY) > 0;

                                }

                            }
                        }

                        break;
                }

                switch (button)
                {
                    case 8: // scroll clockwise
                        if (state)
                        {
                          

                        }

                        break;
                    case 16: // scroll anti-clockwise

                        if (state)
                        {
                         
                        }

                        break;
                    case 2: // scroll clockwise
                        _currentLcdYOffset += 50;

                        mustRender = false;

                        mustRefresh = true;

                        break;
                    case 4: // scroll anti-clockwise

                        if (_currentLcdYOffset == 0) return;

                        _currentLcdYOffset -= 50;
                        if (_currentLcdYOffset < 0)
                        {
                            _currentLcdYOffset = 0;
                        }

                        mustRender = false;

                        mustRefresh = true;

                        break;
                }

                if (!mustRefresh)
                {
                    switch (_currentPage)
                    {
                        case LcdPage.Collapsed:
                            if (state || !_blockNextUpState)
                            {
                                switch (button)
                                {
                                    case 32:
                                        mustRefresh = true;
                                        _currentPage = (LcdPage)(((uint)CurrentTab - 1) / 6);
                                        if (CurrentTab == LcdTab.None)
                                        {
                                            _currentPage = LcdPage.HomeMenu;
                                        }

                                        _lastTab = LcdTab.Init;

                                        break;


                                    case 64:
                                        switch (CurrentTab)
                                        {
                                            case LcdTab.NowPlayingPage1:
                                            case LcdTab.NowPlayingPage2:
                                            case LcdTab.NowPlayingPage3:
                                            case LcdTab.NowPlayingPage4:
                                            case LcdTab.NowPlayingPage5:

                                                if (play && pause && !string.IsNullOrEmpty(currentMediaPlayerKey))
                                                {
                                                    try
                                                    {
                                                        serviceClient.CallService("media_player", "media_play_pause",
                                                                new { entity_id = currentMediaPlayerKey }).GetAwaiter()
                                                            .GetResult();
                                                    }
                                                    catch
                                                    {
                                                        //
                                                    }

                                                    mustRefresh = true;
                                                    
                                                }

                                                break;

                                        }

                                        break;
                                    case 128:

                                        switch (CurrentTab)
                                        {
                                            case LcdTab.NowPlayingPage1:
                                            case LcdTab.NowPlayingPage2:
                                            case LcdTab.NowPlayingPage3:
                                            case LcdTab.NowPlayingPage4:
                                            case LcdTab.NowPlayingPage5:

                                                if (nextTrack && !string.IsNullOrEmpty(currentMediaPlayerKey))
                                                {
                                                    try
                                                    {
                                                        serviceClient.CallService("media_player", "media_next_track",
                                                                new { entity_id = currentMediaPlayerKey }).GetAwaiter()
                                                            .GetResult();
                                                    }
                                                    catch
                                                    {
                                                        //
                                                    }

                                                    mustRefresh = true;
                                                    
                                                }

                                                break;

                                        }

                                        break;
                                    case 256:

                                        switch (CurrentTab)
                                        {
                                            case LcdTab.NowPlayingPage1:
                                            case LcdTab.NowPlayingPage2:
                                            case LcdTab.NowPlayingPage3:
                                            case LcdTab.NowPlayingPage4:
                                            case LcdTab.NowPlayingPage5:

                                                if (previousTrack && !string.IsNullOrEmpty(currentMediaPlayerKey))
                                                {
                                                    try
                                                    {
                                                        serviceClient.CallService("media_player",
                                                                "media_previous_track",
                                                                new { entity_id = currentMediaPlayerKey }).GetAwaiter()
                                                            .GetResult();
                                                    }
                                                    catch
                                                    {
                                                        //
                                                    }

                                                    mustRefresh = true;
                                                    
                                                }

                                                break;

                                        }

                                        break;
                                    case 512:

                                         break;
                                    case 1024:
                                        
                                        break;
                                }
                            }

                            break;
                        case LcdPage.HomeMenu:
                            if (state)
                            {
                                switch (button)
                                {
                                    case 32:
                                        mustRefresh = true;
                                        _currentPage = LcdPage.NowPlayingMenu;
                                        _lastTab = LcdTab.Init;
                                        break;
                                    case 64:
                                        mustRefresh = true;
                                        _currentPage = LcdPage.SensorMenu;
                                        _lastTab = LcdTab.Init;
                                        break;
                                }
                            }

                            break;


                        case LcdPage.NowPlayingMenu:
                            if (state)
                            {
                                switch (button)
                                {
                                    case 32:
                                        mustRefresh = true;
                                        _currentPage = LcdPage.HomeMenu;
                                        _lastTab = LcdTab.Init;

                                        break;
                                    default:

                                        int i = 1, pos = 1;
                                        while ((i & button) == 0)
                                        {
                                            i = i << 1;
                                            ++pos;
                                        }
                                        
                                        if (pos - 7 < App.NowPlayingPages.Count)
                                        {
                                            mustRefresh = SetTab((LcdTab)(pos - 7 + LcdTab.NowPlayingPage1));
                                        }
                                 
                                        break;
                                }
                            }

                            break;
                        case LcdPage.SensorMenu:
                            if (state)
                            {
                                switch (button)
                                {
                                    case 32:
                                        mustRefresh = true;
                                        _currentPage = LcdPage.HomeMenu;
                                        _lastTab = LcdTab.Init;
                                      
                                        break;
                                    default:

                                        int i = 1, pos = 1;
                                        while ((i & button) == 0)
                                        {
                                            i = i << 1;
                                            ++pos;
                                        }

                                        if (pos - 7 < App.SensorPages.Count)
                                        {
                                            mustRefresh = SetTab((LcdTab)(pos - 7 + LcdTab.SensorPage1));
                                        }
                                        break;
                                   
                                }
                            }

                            break;
                    }
                }

                _blockNextUpState = state;

                if (mustRefresh)
                {
                    RefreshDevicePage(mustRender);
                }

            }
        }

        private void CheckLcdOffset()
        {
            if (_currentLcdHeight <= HtmlWindowHeight)
            {
                _currentLcdYOffset = 0;
            }

            if (_currentLcdYOffset + HtmlWindowHeight > _currentLcdHeight )
            {
                _currentLcdYOffset = _currentLcdHeight - HtmlWindowHeight + 4;
            }

            if (_currentLcdYOffset < 0) _currentLcdYOffset = 0;
        }

        private ReturnValues AddPage(uint pageNumber, bool setActive)
        {
            var result = ReturnValues.E_FAIL;

            if (_initOk)
            {
                try
                {
                    if (_pageList.Contains(pageNumber))
                    {
                        return ReturnValues.S_OK;
                    }

                    result = DirectOutputClass.AddPage(FipDevicePointer, (IntPtr) pageNumber, string.Concat("0x", FipDevicePointer.ToString(), " PageNo: ", pageNumber), setActive);
                    if (result == ReturnValues.S_OK)
                    {
                        App.Log.Info("Page: " + pageNumber + " added");

                        _pageList.Add(pageNumber);
                    }
                }
                catch (Exception ex)
                {
                    App.Log.Error(ex);
                }
            }

            return result;
        }

        private ReturnValues SendImageToFip(uint page, Bitmap fipImage)
        {

            if (_initOk)
            {
                if (fipImage == null)
                {
                    return ReturnValues.E_INVALIDARG;
                }

                try
                {
                    fipImage.RotateFlip(RotateFlipType.Rotate180FlipX);

                    var bitmapData =
                        fipImage.LockBits(new Rectangle(0, 0, fipImage.Width, fipImage.Height),
                            ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
                    var intPtr = bitmapData.Scan0;
                    var local3 = bitmapData.Stride * fipImage.Height;
                    DirectOutputClass.SetImage(FipDevicePointer, page, 0, local3, intPtr);
                    fipImage.UnlockBits(bitmapData);
                    return ReturnValues.S_OK;
                }
                catch (Exception ex)
                {
                    App.Log.Error(ex);
                }
            }

            return ReturnValues.E_FAIL;
        }

        private void OnImageLoad(object sender, HtmlImageLoadEventArgs e)
        {
            if (e.Src.StartsWith("chart"))
            {
                try
                {
                    var image = new Bitmap(ChartImageDisplayWidth, ChartImageDisplayHeight);

                    var sensorinfo = e.Src.Replace("chart","").Split('~');

                    var page = App.SensorPages[Convert.ToInt32(sensorinfo[0])];

                    foreach (var s in page.Sections)
                    {
                        var sensor = s.Sensors?.FirstOrDefault(x => x.EntityId == sensorinfo[1]);

                        if (sensor != null)
                        {
                            using (var graphics = Graphics.FromImage(image))
                            {
                                if (sensor.Points.Length > 0)
                                {
                                    graphics.DrawLines(_scrollPen, sensor.Points);
                                }

                                graphics.DrawRectangle(_grayPen,
                                    new Rectangle(0, 0, ChartImageDisplayWidth - 1, ChartImageDisplayHeight - 1));

                                if (sensor.MinVString != sensor.MaxVString)
                                {
                                    graphics.DrawString(sensor.MaxVString, _drawFont, _whiteBrush, (float)1, (float)1);


                                    graphics.DrawString(sensor.MinVString, _drawFont, _whiteBrush, (float)1,
                                        (float)ChartImageDisplayHeight - 17);
                                }
                            }

                            break;
                        }
                    }

                    e.Callback(image);

                }
                catch
                {
                    var image = new Bitmap(ChartImageDisplayWidth, ChartImageDisplayHeight);

                    using (var graphics = Graphics.FromImage(image))
                    {
                       graphics.DrawRectangle(_grayPen,
                            new Rectangle(0, 0, ChartImageDisplayWidth - 1, ChartImageDisplayHeight - 1));
                    }
                    e.Callback(image);
                }
            } 
            else if (e.Src.StartsWith("background."))
            {
                try
                {
                    var image = Image.FromFile(Path.Combine(App.ExePath, "Templates\\images\\") + e.Src);

                    var tempBitmap = new Bitmap(320, 240);
                    
                    using (var graphics = Graphics.FromImage(tempBitmap))
                    {
                        graphics.DrawImage(image, 0, 0);

                        graphics.DrawRectangle(_whitePen,
                            new Rectangle(0, 0, 100, 100));

                    }

                    e.Callback(tempBitmap);

                }
                catch//(Exception ex)
                {
                    var image = new Bitmap(1, 1);

                    e.Callback(image);
                }
            }
            else if (!e.Src.StartsWith("http"))
            {
                try
                {
                    var image = Image.FromFile(Path.Combine(App.ExePath, "Templates\\images\\") + e.Src);

                    e.Callback(image);

                }
                catch//(Exception ex)
                {
                    var image = new Bitmap(1, 1);

                    e.Callback(image);
                }
            }

        }


        private void SetLed(uint ledNumber, bool state)
        {
            if (_ledState[ledNumber] != state)
            {
                DirectOutputClass.SetLed(FipDevicePointer, DEFAULT_PAGE,
                    ledNumber, state);
                _ledState[ledNumber] = state;
            }
        }

        public void CheckCardSelectionLimits(int limit)
        {
            if (CurrentCard[(int)CurrentTab] < 0)
            {
                CurrentCard[(int)CurrentTab] = limit;
            }
            else
            if (CurrentCard[(int)CurrentTab] > limit)
            {
                CurrentCard[(int)CurrentTab] = 0;
            }
        }

        public void RefreshDevicePage(bool mustRender = true)
        {
         

            lock (_refreshDevicePageLock)
            {
                using (var fipImage = new Bitmap(HtmlWindowWidth, HtmlWindowHeight))
                {
                    using (var graphics = Graphics.FromImage(fipImage))
                    {
                        var currentMediaPlayerName = string.Empty;
                        var currentMediaPlayerTitle = string.Empty;
                        var currentMediaPlayerAlbumName = string.Empty;
                        var currentMediaPlayerSeriesName = string.Empty;
                        var currentMediaPlayerSeason= string.Empty;
                        var currentMediaPlayerEpisode = string.Empty;

                        var currentMediaPlayerArtist = string.Empty;
                        var currentMediaPlayerState = string.Empty;
                        var currentMediaPlayerPicture = string.Empty;

                        var pause = false;
                        var previousTrack = false;
                        var nextTrack = false;
                        var play = false;
                        
                        var str = "";

                        switch (CurrentTab)
                        {
                            case LcdTab.SensorPage1:
                            case LcdTab.SensorPage2:
                            case LcdTab.SensorPage3:
                            case LcdTab.SensorPage4:
                            case LcdTab.SensorPage5:
                                if ((int)CurrentTab - (int)LcdTab.SensorPage1 >= App.SensorPages.Count)
                                {
                                    CurrentTab = LcdTab.SensorPage1;
                                }
                                break;
                            case LcdTab.NowPlayingPage1:
                            case LcdTab.NowPlayingPage2:
                            case LcdTab.NowPlayingPage3:
                            case LcdTab.NowPlayingPage4:
                            case LcdTab.NowPlayingPage5:

                                if ((int)CurrentTab - (int)LcdTab.NowPlayingPage1 >= App.NowPlayingPages.Count)
                                {
                                    CurrentTab = LcdTab.NowPlayingPage1;
                                }

                                if ((int)CurrentTab - (int)LcdTab.NowPlayingPage1 < App.NowPlayingPages.Count)
                                {
                                    var currentMediaPlayer =
                                        ((StateObject)(App.NowPlayingPages[(int)CurrentTab - (int)LcdTab.NowPlayingPage1])?.State);

                                    if (currentMediaPlayer != null)
                                    {

                                        if (currentMediaPlayer.Attributes.ContainsKey("friendly_name"))
                                        {
                                            currentMediaPlayerName =
                                                (string)currentMediaPlayer.Attributes["friendly_name"];
                                        }

                                        if (currentMediaPlayer.Attributes.ContainsKey("media_title"))
                                        {
                                            currentMediaPlayerTitle =
                                                (string)currentMediaPlayer.Attributes["media_title"];
                                        }

                                        if (currentMediaPlayer.Attributes.ContainsKey("media_album_name"))
                                        {
                                            currentMediaPlayerAlbumName =
                                                (string)currentMediaPlayer.Attributes["media_album_name"];
                                        }

                                        if (currentMediaPlayer.Attributes.ContainsKey("media_series_title"))
                                        {
                                            currentMediaPlayerSeriesName =
                                                (string)currentMediaPlayer.Attributes["media_series_title"];
                                        }

                                        if (currentMediaPlayer.Attributes.ContainsKey("media_season"))
                                        {
                                            currentMediaPlayerSeason =
                                                currentMediaPlayer.Attributes["media_season"].ToString();
                                        }

                                        if (currentMediaPlayer.Attributes.ContainsKey("media_episode"))
                                        {
                                            currentMediaPlayerEpisode =
                                                (string)currentMediaPlayer.Attributes["media_episode"].ToString();
                                        }

                                        if (currentMediaPlayer.Attributes.ContainsKey("media_artist"))
                                        {
                                            currentMediaPlayerArtist =
                                                (string)currentMediaPlayer.Attributes["media_artist"];
                                        }
                                        else if (currentMediaPlayer.Attributes.ContainsKey("media_album_artist"))
                                        {
                                            currentMediaPlayerArtist =
                                                (string)currentMediaPlayer.Attributes["media_album_artist"];
                                        }

                                        if (currentMediaPlayer.Attributes.ContainsKey("entity_picture"))
                                        {
                                            currentMediaPlayerPicture = App.HaUrl.TrimEnd(new[] { '/', '\\' }) +
                                                                        (string)currentMediaPlayer.Attributes[
                                                                            "entity_picture"];
                                        }

                                        currentMediaPlayerState = currentMediaPlayer.State;

                                        if (currentMediaPlayerState != "idle" &&
                                            currentMediaPlayer.Attributes.ContainsKey("supported_features"))
                                        {
                                            var supportedFeatures =
                                                (long)currentMediaPlayer.Attributes["supported_features"];

                                            pause = (supportedFeatures & PAUSE) > 0;
                                            previousTrack = (supportedFeatures & PREVIOUS_TRACK) > 0;
                                            nextTrack = (supportedFeatures & NEXT_TRACK) > 0;
                                            play = (supportedFeatures & PLAY) > 0;

                                        }
                                    }
                                }

                                break;
                        }

                        /*

                        volume_level
                        is_volume_muted
                        media_content_id
                        media_content_type
                        media_duration
                        media_position
                        media_position_updated_at
                        media_title
                        media_content_rating
                        media_library_title
                        player_source
                        media_summary
                        username
                        entity_picture
                        friendly_name
                        supported_features


                        volume_level
                        is_volume_muted
                        media_content_id
                        media_duration
                        media_position
                        media_position_updated_at
                        media_title
                        app_id
                        app_name
                        entity_picture_local
                        friendly_name
                        supported_features

                        adb_response
                        app_id
                        app_name
                        entity_picture
                        friendly_name
                        hdmi_input
                        is_volume_muted
                        source
                        source_list
                        supported_features
                        volume_level


                        volume_level
                        is_volume_muted
                        media_content_id
                        media_content_type
                        media_duration
                        media_position
                        media_position_updated_at
                        media_title
                        media_series_title
                        media_season
                        media_episode
                        media_content_rating
                        media_library_title
                        player_source
                        media_summary
                        username
                        entity_picture
                        friendly_name
                        supported_features


                        volume_level
                        is_volume_muted
                        media_content_id
                        media_duration
                        media_position
                        media_position_updated_at
                        media_title
                        media_artist
                        app_id
                        app_name
                        entity_picture_local
                        friendly_name
                        supported_features


                         is_volume_muted
                         media_content_id
                         media_content_type
                         media_duration
                         media_position
                         media_position_updated_at
                         media_title
                         media_artist
                         media_album_name
                         media_album_artist
                         media_track
                         media_library_title
                         player_source
                         username
                         entity_picture
                         friendly_name
                         supported_features

                         */

                        if (mustRender)
                        {
                            try
                            {
                                switch (CurrentTab)
                                {
                                    case LcdTab.None:

                                        str =
                                            Engine.Razor.Run("init.cshtml", null, new
                                            {
                                                CurrentTab = CurrentTab,
                                                CurrentPage = _currentPage
                                            });

                                        break;
                                    case LcdTab.NowPlayingPage1:
                                    case LcdTab.NowPlayingPage2:
                                    case LcdTab.NowPlayingPage3:
                                    case LcdTab.NowPlayingPage4:
                                    case LcdTab.NowPlayingPage5:

                                        str =
                                            Engine.Razor.Run("nowplaying.cshtml", null, new
                                            {
                                                CurrentTab = CurrentTab,
                                                CurrentPage = _currentPage,
                                                CurrentCard = CurrentCard[(int)CurrentTab],

                                                State = currentMediaPlayerState,

                                                Title = currentMediaPlayerTitle,
                                                AlbumName = currentMediaPlayerAlbumName,
                                                SeriesName = currentMediaPlayerSeriesName,
                                                Artist = currentMediaPlayerArtist,
                                                PictureUrl = currentMediaPlayerPicture,
                                                Season = currentMediaPlayerSeason,
                                                Episode = currentMediaPlayerEpisode

                                            });
                                        break;
                                    case LcdTab.SensorPage1:
                                    case LcdTab.SensorPage2:
                                    case LcdTab.SensorPage3:
                                    case LcdTab.SensorPage4:
                                    case LcdTab.SensorPage5:

                                        str =
                                            Engine.Razor.Run("sensors.cshtml", null, new
                                            {
                                                CurrentTab = CurrentTab,
                                                CurrentPage = _currentPage,
                                                CurrentCard = CurrentCard[(int)CurrentTab],

                                                ChartImageDisplayWidth = ChartImageDisplayWidth,
                                                ChartImageDisplayHeight = ChartImageDisplayHeight,

                                                PageIndex = (int)CurrentTab - (int)LcdTab.SensorPage1,

                                                Page = App.SensorPages[(int)CurrentTab - (int)LcdTab.SensorPage1]

                                            });
                                        break;
                                }

                            }
                            catch (Exception ex)
                            {
                                App.Log.Error(ex);
                            }
                        }

                        graphics.Clear(Color.Black);

                        if (mustRender)
                        {
                            var measureData =HtmlRender.Measure(graphics, str, HtmlWindowUsableWidth, App.CssData,null, OnImageLoad);

                            _currentLcdHeight = (int)measureData.Height;
                        }

                        CheckLcdOffset();

                        if (_currentLcdHeight > 0)
                        {

                            if (mustRender)
                            {
                                _htmlImage = HtmlRender.RenderToImage(str,
                                    new Size(HtmlWindowUsableWidth, _currentLcdHeight + 20), Color.Black, App.CssData,
                                    null, OnImageLoad);
                            }

                            if (_htmlImage != null)
                            {
                                graphics.DrawImage(_htmlImage, new Rectangle(new Point(HtmlWindowXOffset, 0),
                                        new Size(HtmlWindowUsableWidth, HtmlWindowHeight + 20)),
                                    new Rectangle(new Point(0, _currentLcdYOffset),
                                        new Size(HtmlWindowUsableWidth, HtmlWindowHeight + 20)),
                                    GraphicsUnit.Pixel);
                            }
                        }

                        if (_currentLcdHeight > HtmlWindowHeight)
                        {
                            var scrollThumbHeight = HtmlWindowHeight / (double)_currentLcdHeight * ScrollBarHeight;
                            var scrollThumbYOffset = _currentLcdYOffset / (double)_currentLcdHeight * ScrollBarHeight;

                            graphics.DrawRectangle(_scrollPen, new Rectangle(new Point(HtmlWindowWidth - 9, 2),
                                                               new Size(5, (int)ScrollBarHeight)));

                            graphics.FillRectangle(_scrollBrush, new Rectangle(new Point(HtmlWindowWidth - 9, 2 + (int)scrollThumbYOffset),
                                new Size(5, 1 + (int)scrollThumbHeight)));

                        }
                        


                        if (mustRender &&  CurrentTab != LcdTab.None)
                        {
                            var cardcaptionstr =
                                Engine.Razor.Run("cardcaption.cshtml", null, new
                                {
                                    CurrentTab = CurrentTab,
                                    CurrentPage = _currentPage,
                                    CurrentCard = CurrentCard[(int)CurrentTab],

                                    PlayerName = currentMediaPlayerName
                                });

                            _cardcaptionHtmlImage = HtmlRender.RenderToImage(cardcaptionstr,
                                new Size(HtmlWindowUsableWidth, 26), Color.Black, App.CssData, null,
                                null);
                        }

                        if (_cardcaptionHtmlImage != null)
                        {
                            graphics.DrawImage(_cardcaptionHtmlImage, HtmlWindowXOffset, 0);
                        }
                        
                        switch (CurrentTab)
                        {
                            case LcdTab.SensorPage1:
                            case LcdTab.SensorPage2:
                            case LcdTab.SensorPage3:
                            case LcdTab.SensorPage4:
                            case LcdTab.SensorPage5:

                                var imageMenu = Image.FromFile(
                                    Path.Combine(App.ExePath, "Templates\\images\\") +
                                    "menu.png");
                                graphics.DrawImage(imageMenu, HtmlWindowXOffset, 0);

                                break;

                            case LcdTab.NowPlayingPage1:
                            case LcdTab.NowPlayingPage2:
                            case LcdTab.NowPlayingPage3:
                            case LcdTab.NowPlayingPage4:
                            case LcdTab.NowPlayingPage5:

                                var imageMenu2 = Image.FromFile(
                                    Path.Combine(App.ExePath, "Templates\\images\\") +
                                    "menu.png");
                                graphics.DrawImage(imageMenu2, HtmlWindowXOffset, 0);

                                if (play && pause)
                                {
                                    if (currentMediaPlayerState != "playing")
                                    {
                                        var imagePlay = Image.FromFile(
                                            Path.Combine(App.ExePath, "Templates\\images\\") +
                                            "play-3-24.png");
                                        graphics.DrawImage(imagePlay, HtmlWindowXOffset, 42);
                                    }
                                    else
                                    {
                                        var imagePause = Image.FromFile(
                                            Path.Combine(App.ExePath, "Templates\\images\\") +
                                            "pause-3-24.png");
                                        graphics.DrawImage(imagePause, HtmlWindowXOffset, 42);
                                    }
                                }

                                if (nextTrack)
                                {
                                    var imageNext = Image.FromFile(Path.Combine(App.ExePath, "Templates\\images\\") +
                                                                   "arrow-43-24.png");
                                    graphics.DrawImage(imageNext, HtmlWindowXOffset, 86);
                                }

                                if (previousTrack)
                                {
                                    var imagePrevious = Image.FromFile(
                                        Path.Combine(App.ExePath, "Templates\\images\\") +
                                        "arrow-68-24.png");
                                    graphics.DrawImage(imagePrevious, HtmlWindowXOffset, 129);
                                }
                                /*
                                if (App.MediaPlayerStates.Count > 1)
                                {
                                    var imageNextPlayer = Image.FromFile(
                                        Path.Combine(App.ExePath, "Templates\\images\\") +
                                        "arrow-28-24.png");
                                    graphics.DrawImage(imageNextPlayer, HtmlWindowXOffset, 172);

                                    var imagePreviousPlayer = Image.FromFile(
                                        Path.Combine(App.ExePath, "Templates\\images\\") +
                                        "arrow-92-24.png");
                                    graphics.DrawImage(imagePreviousPlayer, HtmlWindowXOffset, 212);
                                }*/

                                break;
                        }


                        if (_currentPage != LcdPage.Collapsed)
                        {
                            if (mustRender)
                            {
                                var menustr =
                                    Engine.Razor.Run("menu.cshtml", null, new
                                    {
                                        CurrentTab = CurrentTab,
                                        CurrentPage = _currentPage,
                                        Cursor = _currentTabCursor,
                                        
                                    });

                                _menuHtmlImage = HtmlRender.RenderToImage(menustr,
                                    new Size(HtmlMenuWindowWidth, HtmlMenuWindowHeight), Color.Black, App.CssData, null,
                                    OnImageLoad);
                            }

                            if (_menuHtmlImage != null)
                            {
                                graphics.DrawImage(_menuHtmlImage, 0, 0);
                            }
                        }

#if DEBUG
                        //fipImage.Save("screenshot" + SerialNumber + "_" + (int)CurrentTab + "_" + CurrentCard[(int)CurrentTab] + ".png", ImageFormat.Png);
#endif
                        SendImageToFip(DEFAULT_PAGE, fipImage);

                        if (_initOk)
                        {
                            if (_currentPage == LcdPage.Collapsed)
                            {
                                SetLed(1, true);

                                switch (CurrentTab)
                                {
                                    case LcdTab.SensorPage1:
                                    case LcdTab.SensorPage2:
                                    case LcdTab.SensorPage3:
                                    case LcdTab.SensorPage4:
                                    case LcdTab.SensorPage5:
                                        SetLed(2, false);
                                        SetLed(3, false);
                                        SetLed(4, false);
                                        SetLed(5, false);
                                        SetLed(6, false);

                                        break;

                                    case LcdTab.NowPlayingPage1:
                                    case LcdTab.NowPlayingPage2:
                                    case LcdTab.NowPlayingPage3:
                                    case LcdTab.NowPlayingPage4:
                                    case LcdTab.NowPlayingPage5:

                                        SetLed(2, play && pause);
                                        SetLed(3, nextTrack);
                                        SetLed(4, previousTrack);

                                        //if (App.MediaPlayerStates.Count <= 1)
                                        //{
                                            SetLed(5, false);
                                            SetLed(6, false);

                                        /*}
                                        else
                                        {
                                            SetLed(5, true);
                                            SetLed(6, true);
                                            break;
                                        }*/

                                        break;
                                }
                            }
                            else
                            {
                                for (uint i = 1; i <= 6; i++)
                                {
                                    if (_currentPage == LcdPage.SensorMenu && i > 1+ App.SensorPages.Count)
                                        SetLed(i, false);
                                    if (_currentPage == LcdPage.NowPlayingMenu && i > 1 + App.NowPlayingPages.Count)
                                        SetLed(i, false);
                                    else if (_currentPage == LcdPage.HomeMenu && i > 2)
                                        SetLed(i, false);
                                   
                                    else
                                        SetLed(i, true);
                                }
                            }

                        }

                        _lastTab = CurrentTab;

                    }
                }
            }
        }


    }
}
