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

// For extension methods.


// ReSharper disable IdentifierTypo
// ReSharper disable StringLiteralTypo
// ReSharper disable CommentTypo

namespace fipha
{

   
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

        private int CurrentCard = 0;

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
        
        
        private readonly SolidBrush _scrollBrush = new SolidBrush(Color.FromArgb(0xff, 0xFF, 0xB0, 0x00));
        private readonly SolidBrush _whiteBrush = new SolidBrush(Color.FromArgb(0xff, 0xFF, 0xFF, 0xFF));

        private readonly Font _drawFont = new Font("Arial", 13, GraphicsUnit.Pixel);

        private Image _htmlImage;
        private Image _cardcaptionHtmlImage;

        private const int HtmlWindowXOffset = 1;

        private int _htmlWindowWidth = 320;
        private int _htmlWindowHeight = 240;

        private int HtmlWindowUsableWidth => _htmlWindowWidth - 9 - HtmlWindowXOffset;

        private double ScrollBarHeight => _htmlWindowHeight -7.0;

        private DirectOutputClass.PageCallback _pageCallbackDelegate;
        private DirectOutputClass.SoftButtonCallback _softButtonCallbackDelegate;

        private bool _blockNextUpState;


        public FipPanel(IntPtr devicePtr) 
        {
            FipDevicePointer = devicePtr;
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
                App.Log.Info("FipPanel Serial Number : " + SerialNumber);

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

                if (App.MediaPlayerStates.Count > 0)
                {
                    var currentMediaPlayer = ((StateObject)(App.MediaPlayerStates[CurrentCard]));

                    currentMediaPlayerState = currentMediaPlayer.State;
                    currentMediaPlayerKey = currentMediaPlayer.EntityId;

                    if (currentMediaPlayerState != "idle" && currentMediaPlayer.Attributes.ContainsKey("supported_features"))
                    {
                        var supportedFeatures = (long)currentMediaPlayer.Attributes["supported_features"];

                        pause = (supportedFeatures & PAUSE) > 0;
                        previousTrack = (supportedFeatures & PREVIOUS_TRACK) > 0;
                        nextTrack = (supportedFeatures & NEXT_TRACK) > 0;
                        play = (supportedFeatures & PLAY) > 0;

                    }

                }

                switch (button)
                {
                    case 8: // scroll clockwise
                        if (state)
                        {

                            CurrentCard++;
                            _currentLcdYOffset = 0;

                            mustRefresh = true;
                            
                        }

                        break;
                    case 16: // scroll anti-clockwise

                        if (state)
                        {
                            CurrentCard--;
                            _currentLcdYOffset = 0;

                            mustRefresh = true;
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
                    if (state || !_blockNextUpState)
                    {
                        switch (button)
                        {
                            case 64:
                                if (play && pause && !string.IsNullOrEmpty(currentMediaPlayerKey))
                                {
                                    try
                                    {
                                        serviceClient.CallService("media_player", "media_play_pause", new { entity_id = currentMediaPlayerKey }).GetAwaiter().GetResult();
                                    }
                                    catch
                                    {
                                      //
                                    }
                                    
                                    mustRefresh = true;

                                }

                                break;
                            case 128:

                                if (nextTrack && !string.IsNullOrEmpty(currentMediaPlayerKey))
                                {
                                    try
                                    {
                                        serviceClient.CallService("media_player", "media_next_track", new { entity_id = currentMediaPlayerKey }).GetAwaiter().GetResult();
                                    }
                                    catch
                                    {
                                        //
                                    }

                                    mustRefresh = true;

                                }

                                break;
                            case 256:

                                if (previousTrack && !string.IsNullOrEmpty(currentMediaPlayerKey))
                                {
                                    try
                                    {
                                        serviceClient.CallService("media_player", "media_previous_track", new { entity_id = currentMediaPlayerKey }).GetAwaiter().GetResult();
                                    }
                                    catch
                                    {
                                        //
                                    }

                                    mustRefresh = true;

                                }
                                break;
                            case 512:

                                CurrentCard++;
                                _currentLcdYOffset = 0;

                                mustRefresh = true;

                                break;
                            case 1024:

                                CurrentCard--;
                                _currentLcdYOffset = 0;

                                mustRefresh = true;

                                break;
                        }
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
            if (_currentLcdHeight <= _htmlWindowHeight)
            {
                _currentLcdYOffset = 0;
            }

            if (_currentLcdYOffset + _htmlWindowHeight > _currentLcdHeight )
            {
                _currentLcdYOffset = _currentLcdHeight - _htmlWindowHeight + 4;
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
            if (e.Src.StartsWith("background."))
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

        public void RefreshDevicePage(bool mustRender = true)
        {
         

            lock (_refreshDevicePageLock)
            {
                using (var fipImage = new Bitmap(_htmlWindowWidth, _htmlWindowHeight))
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

                        if (CurrentCard < 0)
                        {
                            CurrentCard = App.MediaPlayerStates.Count-1;
                        }
                        else
                        if (CurrentCard > App.MediaPlayerStates.Count-1)
                        {
                            CurrentCard = 0;
                        }

                        if (App.MediaPlayerStates.Count > 0)
                        {
                            var currentMediaPlayer = ((StateObject)(App.MediaPlayerStates[CurrentCard]));

                            if (currentMediaPlayer.Attributes.ContainsKey("friendly_name"))
                            {
                                currentMediaPlayerName = (string)currentMediaPlayer.Attributes["friendly_name"];
                            }

                            if (currentMediaPlayer.Attributes.ContainsKey("media_title"))
                            {
                                currentMediaPlayerTitle = (string)currentMediaPlayer.Attributes["media_title"];
                            }

                            if (currentMediaPlayer.Attributes.ContainsKey("media_album_name"))
                            {
                                currentMediaPlayerAlbumName = (string)currentMediaPlayer.Attributes["media_album_name"];
                            }

                            if (currentMediaPlayer.Attributes.ContainsKey("media_series_title"))
                            {
                                currentMediaPlayerSeriesName = (string)currentMediaPlayer.Attributes["media_series_title"];
                            }

                            if (currentMediaPlayer.Attributes.ContainsKey("media_season"))
                            {
                                currentMediaPlayerSeason = currentMediaPlayer.Attributes["media_season"].ToString();
                            }

                            if (currentMediaPlayer.Attributes.ContainsKey("media_episode"))
                            {
                                currentMediaPlayerEpisode = (string)currentMediaPlayer.Attributes["media_episode"].ToString();
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
                                currentMediaPlayerPicture = App.haUrl.TrimEnd(new[] { '/', '\\' }) +
                                                            (string)currentMediaPlayer.Attributes[
                                                                "entity_picture"];
                            }

                            currentMediaPlayerState = currentMediaPlayer.State;

                            if (currentMediaPlayerState != "idle" && currentMediaPlayer.Attributes.ContainsKey("supported_features"))
                            {
                                var supportedFeatures = (long)currentMediaPlayer.Attributes["supported_features"];
                                
                                pause = (supportedFeatures & PAUSE) > 0;
                                previousTrack = (supportedFeatures & PREVIOUS_TRACK) > 0;
                                nextTrack = (supportedFeatures & NEXT_TRACK) > 0;
                                play = (supportedFeatures & PLAY) > 0;

                            }
                           
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
                                    str =
                                        Engine.Razor.Run("nowplaying.cshtml", null, new
                                        {
                                            CurrentCard = CurrentCard,
                                            State = currentMediaPlayerState,
                                           
                                            Title = currentMediaPlayerTitle,
                                            AlbumName= currentMediaPlayerAlbumName ,
                                            SeriesName = currentMediaPlayerSeriesName,
                                            Artist =currentMediaPlayerArtist ,
                                            PictureUrl = currentMediaPlayerPicture,
                                            Season = currentMediaPlayerSeason,
                                            Episode = currentMediaPlayerEpisode

                                        });
                                
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
                                        new Size(HtmlWindowUsableWidth, _htmlWindowHeight + 20)),
                                    new Rectangle(new Point(0, _currentLcdYOffset),
                                        new Size(HtmlWindowUsableWidth, _htmlWindowHeight + 20)),
                                    GraphicsUnit.Pixel);
                            }
                        }

                        if (_currentLcdHeight > _htmlWindowHeight)
                        {
                            var scrollThumbHeight = _htmlWindowHeight / (double)_currentLcdHeight * ScrollBarHeight;
                            var scrollThumbYOffset = _currentLcdYOffset / (double)_currentLcdHeight * ScrollBarHeight;

                            graphics.DrawRectangle(_scrollPen, new Rectangle(new Point(_htmlWindowWidth - 9, 2),
                                                               new Size(5, (int)ScrollBarHeight)));

                            graphics.FillRectangle(_scrollBrush, new Rectangle(new Point(_htmlWindowWidth - 9, 2 + (int)scrollThumbYOffset),
                                new Size(5, 1 + (int)scrollThumbHeight)));

                        }
                        


                        if (mustRender)
                        {
                            var cardcaptionstr =
                                Engine.Razor.Run("cardcaption.cshtml", null, new
                                {
                                    CurrentCard = CurrentCard,
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

                        if (play && pause)
                        {
                            if (currentMediaPlayerState != "playing")
                            {
                                var imagePlay = Image.FromFile(Path.Combine(App.ExePath, "Templates\\images\\") +
                                                               "play-3-24.png");
                                graphics.DrawImage(imagePlay, HtmlWindowXOffset, 42);
                            }
                            else
                            {
                                var imagePause = Image.FromFile(Path.Combine(App.ExePath, "Templates\\images\\") +
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
                            var imagePrevious = Image.FromFile(Path.Combine(App.ExePath, "Templates\\images\\") +
                                                               "arrow-68-24.png");
                            graphics.DrawImage(imagePrevious, HtmlWindowXOffset, 129);
                        }

                        if (App.MediaPlayerStates.Count > 1)
                        {
                            var imageNextPlayer = Image.FromFile(Path.Combine(App.ExePath, "Templates\\images\\") +
                                                               "arrow-28-24.png");
                            graphics.DrawImage(imageNextPlayer, HtmlWindowXOffset, 172);

                            var imagePreviousPlayer = Image.FromFile(Path.Combine(App.ExePath, "Templates\\images\\") +
                                                               "arrow-92-24.png");
                            graphics.DrawImage(imagePreviousPlayer, HtmlWindowXOffset, 212);
                        }

                        SendImageToFip(DEFAULT_PAGE, fipImage);

                        if (_initOk)
                        {
                            SetLed(2, play && pause);
                            SetLed(3, nextTrack);
                            SetLed(4, previousTrack);

                            if (App.MediaPlayerStates.Count <= 1)
                            {
                                SetLed(5, false);
                                SetLed(6, false);

                            }
                            else
                            {
                                SetLed(5, true);
                                SetLed(6, true);
                            }

                        }

                    }
                }
            }
        }


    }
}
