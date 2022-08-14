using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
using log4net;
using RazorEngine;
using RazorEngine.Configuration;
using RazorEngine.Templating;
using TheArtOfDev.HtmlRenderer.Core;
using SharpDX.DirectInput;
using System.Collections.Specialized;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Documents;
using HADotNet.Core;
using HADotNet.Core.Clients;
using HADotNet.Core.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using static fipha.SensorData;
using System.Windows.Media;


// ReSharper disable StringLiteralTypo

namespace fipha
{

    /// <summary>
    /// Simple application. Check the XAML for comments.
    /// </summary>
    public partial class App : Application
    {

        public static string HaUrl { get; set; }
        public static string HaToken { get; set; }
        public static HashSet<string> ExcludePlayers { get; set; }

        public static bool IsShuttingDown { get; set; }

        public static EntityClient EntityClient { get; set; }
        public static StatesClient StatesClient { get; set; }
        public static HistoryClient HistoryClient { get; set; }

        public static List<string> MediaPlayers { get; set; }

        public static List<string> Sensors { get; set; }
        
        public static OrderedDictionary MediaPlayerStates = new OrderedDictionary();

        public static Task HaMediaPlayerTask;
        private static CancellationTokenSource _haMediaPlayerTokenSource = new CancellationTokenSource();

        public static Task HaSensorTask;
        private static CancellationTokenSource _haSensorTokenSource = new CancellationTokenSource();

        public static Task HWInfoTask;
        private static CancellationTokenSource _hwInfoTokenSource = new CancellationTokenSource();

        private static Mutex _mutex;

        private TaskbarIcon _notifyIcon;

        public static readonly FipHandler FipHandler = new FipHandler();

        public static List<SensorPage> SensorPages = new List<SensorPage>();


        public static readonly ILog Log =
            LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public static CssData CssData;
        
        public static string ExePath;

        private static void GetExePath()
        {
            var strExeFilePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            ExePath = Path.GetDirectoryName(strExeFilePath);
        }

        private static void RunProcess(string fileName)
        {
            var process = new Process();
            // Configure the process using the StartInfo properties.
            process.StartInfo.FileName = fileName;
            process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            process.Start();
            process.WaitForExit();
        }
        
        protected override void OnStartup(StartupEventArgs evtArgs)
        {
            const string appName = "fipha";

            _mutex = new Mutex(true, appName, out var createdNew);

            if (!createdNew)
            {
                //app is already running! Exiting the application  
                Current.Shutdown();
            }

            GetExePath();

            base.OnStartup(evtArgs);

            log4net.Config.XmlConfigurator.Configure();

            try
            {

                if (File.Exists(Path.Combine(ExePath, "appSettings.config")) &&
                    ConfigurationManager.GetSection("appSettings") is NameValueCollection appSection)
                {
                    HaUrl = appSection["haUrl"];
                    HaToken = appSection["haToken"];
                    ExcludePlayers = new HashSet<string>(appSection["excludePlayers"].Split(',')
                        .Select(p => p.ToLower().Trim()).ToList());
                }

                if (!string.IsNullOrEmpty(HaUrl) && !string.IsNullOrEmpty(HaToken) &&
                    HaUrl.ToLower().StartsWith("http") && !HaToken.Contains("..."))
                {

                    ClientFactory.Initialize($"{HaUrl}", HaToken);

                    EntityClient = ClientFactory.GetClient<EntityClient>();

                    StatesClient = ClientFactory.GetClient<StatesClient>();

                    HistoryClient = ClientFactory.GetClient<HistoryClient>();

                    Log.Info("Connected to Home Assistant");
                }

            }
            catch(Exception ex)
            {
                Log.Error("Connecting to Home Assistant Failed", ex);
            }

            SensorPages = SensorData.GetSensors(@"Data\sensors.json");

            //create the notifyicon (it's a resource declared in NotifyIconResources.xaml
            _notifyIcon = (TaskbarIcon)FindResource("NotifyIcon");

            _notifyIcon.IconSource = new BitmapImage(new Uri("pack://application:,,,/fipha;component/fipha.ico"));
            _notifyIcon.ToolTipText = "fipha";

            var splashScreen = new SplashScreenWindow();
            splashScreen.Show();

            Task.Run(async () =>
            {
               
                if (EntityClient != null)
                {
                    var config = new TemplateServiceConfiguration
                    {
                        TemplateManager = new ResolvePathTemplateManager(new[] { Path.Combine(ExePath, "Templates") }),
                        DisableTempFileLocking = true,
                        BaseTemplateType = typeof(HtmlSupportTemplateBase<>) /*,
                    Namespaces = new HashSet<string>(){
                        "System",
                        "System.Linq",
                        "System.Collections",
                        "System.Collections.Generic"
                        }*/
                    };
                    splashScreen.Dispatcher.Invoke(() => splashScreen.ProgressText.Text = "Loading cshtml templates...");

                    Engine.Razor = RazorEngineService.Create(config);

                    Engine.Razor.Compile("init.cshtml", null);
                    Engine.Razor.Compile("menu.cshtml", null);
                    Engine.Razor.Compile("cardcaption.cshtml", null);
                    Engine.Razor.Compile("layout.cshtml", null);

                    Engine.Razor.Compile("nowplaying.cshtml", null);
                    Engine.Razor.Compile("sensors.cshtml", null);

                    CssData = TheArtOfDev.HtmlRenderer.WinForms.HtmlRender.ParseStyleSheet(
                        File.ReadAllText(Path.Combine(ExePath, "Templates\\styles.css")), true);

                    splashScreen.Dispatcher.Invoke(() => splashScreen.ProgressText.Text = "Getting data from HA...");

                    try
                    {
                        Sensors = (await EntityClient.GetEntities("sensor")).ToList();
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Finding Sensors", ex);
                    }

                    try
                    {
                        MediaPlayers = (await EntityClient.GetEntities("media_player")).ToList();

                        foreach (var mediaPlayer in MediaPlayers)
                        {
                            Log.Info($"{mediaPlayer}");
                        }
                    }
                    catch(Exception ex )
                    {
                        Log.Error("Finding Media Players",ex );
                    }
                }
                else
                {
                    Log.Error("No Home Assistant Connection");
                }

                splashScreen.Dispatcher.Invoke(() => splashScreen.ProgressText.Text = "Getting sensor data from HWInfo...");

                HWInfo.ReadMem("HWINFO.INC");
 
                if (HWInfo.SensorData.Any())
                {
                    Log.Info($"Writing { HWInfo.SensorData.Count} HWINFO Sensors to hwinfo.json");

                    HWInfo.SaveDataToFile(@"Data\hwinfo.json");
                }
                else
                {
                    Log.Error("No HWINFO Sensors Found");
                }

                Dispatcher.Invoke(() =>
                {
                    var window = Current.MainWindow = new MainWindow();
                    window.ShowActivated = false;
                });

                if (EntityClient != null)
                {
                    splashScreen.Dispatcher.Invoke(() => splashScreen.ProgressText.Text = "Initializing FIP...");
                    if (!FipHandler.Initialize())
                    {
                        Current.Shutdown();
                    }
                }

                Log.Info("fipha started");

                Dispatcher.Invoke(() =>
                {
                    var window = Current.MainWindow;
                    window?.Hide();
                });

                Dispatcher.Invoke(() => { splashScreen.Close(); });
                
                var haMediaPlayerToken = _haMediaPlayerTokenSource.Token;


                if (EntityClient != null && MediaPlayers != null)
                {
                    HaMediaPlayerTask = Task.Run(async () =>
                    {

                        Log.Info("HA Media Player task started");

                        while (true)
                        {
                            if (haMediaPlayerToken.IsCancellationRequested)
                            {
                                haMediaPlayerToken.ThrowIfCancellationRequested();
                            }

                            for (var index = 0; index < App.MediaPlayers.Count; index++)
                            {
                                var mediaPlayer = App.MediaPlayers[index];

                                var state = await App.StatesClient.GetState(mediaPlayer);

                                if (!ExcludePlayers.Contains(mediaPlayer) && state != null && state.State != "off" &&
                                    state.State != "standby" &&
                                    state.State != "unavailable" && state.Attributes?.Any() == true /*&&
                                state.Attributes.ContainsKey("media_content_id") && state.Attributes["media_content_id"] is long*/
                                   )
                                {
                                    if (MediaPlayerStates.Contains(mediaPlayer.ToLower()))
                                    {
                                        MediaPlayerStates[mediaPlayer] = state;
                                    }
                                    else
                                    {
                                        MediaPlayerStates.Add(mediaPlayer, state);
                                    }
                                }
                                else
                                {
                                    if (MediaPlayerStates.Contains(mediaPlayer))
                                    {
                                        MediaPlayerStates.Remove(mediaPlayer);
                                    }
                                }
                            }

                            FipHandler.RefreshHAMediaPlayerPages();

                            await Task.Delay(1000, _haMediaPlayerTokenSource.Token); // repeat every 2 seconds
                        }

                    }, haMediaPlayerToken);
                }

                var haSensorToken = _haSensorTokenSource.Token;

                if (EntityClient != null && Sensors?.Any() == true && SensorPages?.Any() == true)
                {
                    HaSensorTask = Task.Run(async () =>
                    {

                        Log.Info("HA Sensor task started");

                        while (true)
                        {
                            if (haSensorToken.IsCancellationRequested)
                            {
                                haSensorToken.ThrowIfCancellationRequested();
                            }

                            foreach (var page in SensorPages)
                            {
                                foreach (var section in page.Sections)
                                {
                                    foreach (var sensor in section.Sensors)
                                    {
                                        if (Sensors.Contains(sensor.EntityId))
                                        {
                                            try
                                            {
                                                sensor.State = await App.StatesClient.GetState(sensor.EntityId);
                                                if (sensor.State.Attributes?.Any() == true)
                                                {
                                                    sensor.Value = sensor.State.State;

                                                    if (sensor.State.Attributes.ContainsKey("unit_of_measurement"))
                                                    {
                                                        sensor.Value += " " + (string)sensor.State.Attributes["unit_of_measurement"];
                                                    }

                                                    if (string.IsNullOrEmpty(sensor.Name) && sensor.State.Attributes.ContainsKey("friendly_name"))
                                                    {
                                                        sensor.Name = (string)sensor.State.Attributes["friendly_name"];
                                                    }

                                                    if (HistoryClient != null && sensor.Chart)
                                                    {
                                                        sensor.HistoryList = await HistoryClient.GetHistory(sensor.EntityId,
                                                            DateTime.Now.AddMinutes(-sensor.ChartMinutes), DateTime.Now);

                                                        (sensor.Points, sensor.MinVString, sensor.MaxVString) = HistoryToChart(sensor, FipPanel.ChartImageDisplayWidth, FipPanel.ChartImageDisplayHeight);

                                                    }
                                                }

                                            }
                                            catch
                                            {
                                                sensor.Value = "-";
                                            }
                                           
                                        }
                                    }
                                }
                            }

                            FipHandler.RefreshHASensorPages();

                            await Task.Delay(60000, _haSensorTokenSource.Token); // repeat every 60 seconds
                        }

                    }, haSensorToken);
                }

                var hwInfoToken = _hwInfoTokenSource.Token;

                if (File.Exists(Path.Combine(App.ExePath, "mqtt.config")) &&  HWInfo.SensorData.Any())
                {

                    HWInfoTask = Task.Run(async () =>
                    {
                        var result = await MQTT.Connect();
                        if (!result)
                        {
                            Log.Info("Failed to connect to MQTT server");
                        }
                        else
                        {



                            Log.Info("HWInfo task started");

                            if (HWInfo.SensorData.Any())
                            {

                                foreach (var sensor in HWInfo.SensorData)
                                {
                                    foreach (var element in sensor.Value.Elements)
                                    {
                                        var mqttValue = JsonConvert.SerializeObject(new HWInfo.MQTTDiscoveryObj
                                        {
                                            device_class = element.Value.DeviceClass,
                                            name = element.Value.Name,
                                            state_topic =
                                                $"homeassistant/{element.Value.Component}/{element.Value.Node}/state",
                                            unit_of_measurement = element.Value.Unit,
                                            value_template = "{{ value_json.value}}",
                                            unique_id = element.Value.Node,
                                            state_class = "measurement"
                                        }, new JsonSerializerSettings
                                        {
                                            NullValueHandling = NullValueHandling.Ignore
                                        });

                                        var task = Task.Run<bool>(async () =>
                                            await MQTT.Publish(
                                                $"homeassistant/{element.Value.Component}/{element.Value.Node}/config",
                                                mqttValue));

                                    }
                                }

                                while (true)
                                {
                                    if (hwInfoToken.IsCancellationRequested)
                                    {
                                        hwInfoToken.ThrowIfCancellationRequested();
                                    }

                                    HWInfo.ReadMem("HWINFO.INC");

                                    foreach (var sensor in HWInfo.SensorData)
                                    {
                                        foreach (var element in sensor.Value.Elements)
                                        {
                                            var mqttValue = JsonConvert.SerializeObject(new HWInfo.MQTTStateObj
                                            {
                                                value = element.Value.NumericValue
                                            });

                                            var task = Task.Run<bool>(async () =>
                                                await MQTT.Publish(
                                                    $"homeassistant/{element.Value.Component}/{element.Value.Node}/state",
                                                    mqttValue));
                                        }

                                    }

                                    //!!!FipHandler.RefreshHWInfoPages();

                                    await Task.Delay(5 * 1000, _hwInfoTokenSource.Token); // repeat every 5 seconds
                                }
                            }
                        }

                    }, hwInfoToken);
                }

            });

        }
      

        protected override void OnExit(ExitEventArgs e)
        {
            FipHandler.Close();

            _notifyIcon.Dispose(); //the icon would clean up automatically, but this is cleaner

            _haMediaPlayerTokenSource.Cancel();

            var haMediaPlayerToken = _haMediaPlayerTokenSource.Token;

            try
            {
                HaMediaPlayerTask?.Wait(haMediaPlayerToken);
            }
            catch (OperationCanceledException)
            {
                Log.Info("HA Media Player background task ended");
            }
            finally
            {
                _haMediaPlayerTokenSource.Dispose();
            }

            _haSensorTokenSource.Cancel();

            var haSensorToken = _haSensorTokenSource.Token;

            try
            {
                HaSensorTask?.Wait(haSensorToken);
            }
            catch (OperationCanceledException)
            {
                Log.Info("HA Sensor background task ended");
            }
            finally
            {
                _haSensorTokenSource.Dispose();
            }
            
            _hwInfoTokenSource.Cancel();

            var hwInfoToken = _hwInfoTokenSource.Token;

            try
            {
                HWInfoTask?.Wait(hwInfoToken);
            }
            catch (OperationCanceledException)
            {
                Log.Info("HWINFO background task ended");
            }
            finally
            {
                _hwInfoTokenSource.Dispose();
            }

            Log.Info("exiting");

            base.OnExit(e);
        }
    }
}
